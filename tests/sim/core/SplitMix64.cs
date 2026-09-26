namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// SplitMix64 as pinned by 08-interfaces-core.md §8.8, used as the input
    /// generator for property-style tests (07-conventions.md L4). Always
    /// seeded with an integer literal inside the test that uses it.
    /// </summary>
    internal sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
