namespace AirportSim.Sim.Delay
{
    // Open-addressing map from a non-zero ulong key to a slot index. Linear probing
    // with backward-shift deletion, so removal leaves no tombstones. Never iterated;
    // lookups only, so its layout cannot reach any output. Allocates only when it grows.
    internal sealed class IdMap
    {
        private const ulong Multiplier = 0x9E3779B97F4A7C15UL;

        private ulong[] _keys;
        private int[] _vals;
        private int _mask;
        private int _count;

        public IdMap(int capacityPow2)
        {
            _keys = new ulong[capacityPow2];
            _vals = new int[capacityPow2];
            _mask = capacityPow2 - 1;
        }

        private int Home(ulong key)
        {
            return (int)((key * Multiplier) >> 32) & _mask;
        }

        public void Add(ulong key, int value)
        {
            if ((_count + 1) * 2 > _keys.Length)
            {
                Grow();
            }

            int i = Home(key);
            while (_keys[i] != 0UL)
            {
                i = (i + 1) & _mask;
            }

            _keys[i] = key;
            _vals[i] = value;
            _count++;
        }

        public bool TryGet(ulong key, out int value)
        {
            int i = Home(key);
            while (_keys[i] != 0UL)
            {
                if (_keys[i] == key)
                {
                    value = _vals[i];
                    return true;
                }

                i = (i + 1) & _mask;
            }

            value = -1;
            return false;
        }

        public void Remove(ulong key)
        {
            int i = Home(key);
            while (_keys[i] != key)
            {
                if (_keys[i] == 0UL)
                {
                    return;
                }

                i = (i + 1) & _mask;
            }

            _count--;
            int j = i;
            while (true)
            {
                _keys[i] = 0UL;
                while (true)
                {
                    j = (j + 1) & _mask;
                    if (_keys[j] == 0UL)
                    {
                        return;
                    }

                    int home = Home(_keys[j]);
                    bool stays = i <= j ? (i < home && home <= j) : (i < home || home <= j);
                    if (!stays)
                    {
                        break;
                    }
                }

                _keys[i] = _keys[j];
                _vals[i] = _vals[j];
                i = j;
            }
        }

        private void Grow()
        {
            ulong[] oldKeys = _keys;
            int[] oldVals = _vals;
            _keys = new ulong[oldKeys.Length * 2];
            _vals = new int[oldKeys.Length * 2];
            _mask = _keys.Length - 1;
            _count = 0;
            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldKeys[i] != 0UL)
                {
                    Add(oldKeys[i], oldVals[i]);
                }
            }
        }
    }
}
