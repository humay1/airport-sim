namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// A fixed-capacity FIFO ring of ints. It never grows: a full queue
    /// refuses the insertion, and the caller reports the broken invariant
    /// (13-interfaces-turnaround.md §13.10, Q-092).
    /// </summary>
    internal sealed class IntQueue
    {
        private readonly int[] _items;
        private int _head;

        public IntQueue(int capacity)
        {
            _items = new int[capacity];
        }

        public int Count { get; private set; }

        public bool TryEnqueue(int value)
        {
            if (Count == _items.Length)
            {
                return false;
            }

            _items[(_head + Count) % _items.Length] = value;
            Count++;
            return true;
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
