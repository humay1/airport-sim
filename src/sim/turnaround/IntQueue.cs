namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// A growable FIFO ring of ints. Growth allocates, and happens only when
    /// the queue outgrows its capacity; steady-state use allocates nothing.
    /// </summary>
    internal sealed class IntQueue
    {
        private int[] _items;
        private int _head;

        public IntQueue(int capacity)
        {
            _items = new int[capacity];
        }

        public int Count { get; private set; }

        public int Peek()
        {
            return _items[_head];
        }

        public void Enqueue(int value)
        {
            if (Count == _items.Length)
            {
                var bigger = new int[_items.Length * 2];
                for (int i = 0; i < Count; i++)
                {
                    bigger[i] = _items[(_head + i) % _items.Length];
                }

                _items = bigger;
                _head = 0;
            }

            _items[(_head + Count) % _items.Length] = value;
            Count++;
        }

        public int Dequeue()
        {
            int value = _items[_head];
            _head = (_head + 1) % _items.Length;
            Count--;
            return value;
        }
    }
}
