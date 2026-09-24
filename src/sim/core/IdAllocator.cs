using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="IIdAllocator"/> implementation. Spec: 08-interfaces-core.md §8.4
    /// ("Allocation rule", Q-017).
    /// </summary>
    internal sealed class IdAllocator : IIdAllocator
    {
        // 2^48 - 1: the largest counter value that still fits alongside a 16-bit owner
        // in a 64-bit id.
        private const ulong MaxCounter = (1UL << 48) - 1;

        // Indexed by SystemId.Value (0..14); 0 and 8 are never used.
        private readonly ulong[] _counters = new ulong[15];

        public EntityId Next(SystemId owner)
        {
            ushort value = owner.Value;
            if (value == 0 || value == 8 || value > 14)
            {
                throw new ArgumentException("owner must be a registered system id, 1-14 excluding 8", nameof(owner));
            }

            ulong counter = _counters[value] + 1;
            if (counter > MaxCounter)
            {
                // The exact tick is not meaningful here: this guard is unreachable in any
                // realistic run (2^48 allocations by one owner), and IIdAllocator carries no
                // tick of its own.
                throw new SimInvariantException("IIdAllocator counter overflow", tick: 0);
            }

            _counters[value] = counter;
            return new EntityId(((ulong)value << 48) | counter);
        }

        /// <summary>
        /// Feeds the core hash's id-counter section (08-interfaces-core.md §8.9, Q-017):
        /// the number of owners with a non-zero counter, then each such owner's
        /// <see cref="SystemId.Value"/> and counter, in ascending <see cref="SystemId"/> order.
        /// </summary>
        internal void FeedCounters(ref StateHasher hasher)
        {
            int nonZero = 0;
            for (int owner = 1; owner <= 14; owner++)
            {
                if (owner == 8)
                {
                    continue;
                }
                if (_counters[owner] != 0)
                {
                    nonZero++;
                }
            }

            hasher.Feed((ulong)nonZero);

            for (int owner = 1; owner <= 14; owner++)
            {
                if (owner == 8)
                {
                    continue;
                }
                if (_counters[owner] != 0)
                {
                    hasher.Feed((ulong)owner);
                    hasher.Feed(_counters[owner]);
                }
            }
        }
    }
}
