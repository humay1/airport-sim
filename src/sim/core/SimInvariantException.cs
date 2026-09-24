using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A broken invariant, loudly, with the tick number (07-conventions.md "Error handling").
    /// Spec: 08-interfaces-core.md §8.5a (Q-014).
    /// </summary>
    public sealed class SimInvariantException : Exception
    {
        public ulong Tick { get; }

        /// <summary>Valid only if <see cref="HasWorldHash"/>.</summary>
        public ulong WorldHash { get; }

        public bool HasWorldHash { get; }

        /// <summary>The only public constructor. A module detecting a broken invariant during
        /// a tick uses this; the host's own wrapping (below) is not reachable from outside
        /// <c>sim.core</c>.</summary>
        public SimInvariantException(string message, ulong tick)
            : base(message)
        {
            Tick = tick;
            WorldHash = 0;
            HasWorldHash = false;
        }

        /// <summary>Used by the host to wrap any exception escaping phases 1-4 of a tick.</summary>
        internal SimInvariantException(string message, ulong tick, Exception inner, ulong worldHash, bool hasWorldHash)
            : base(message, inner)
        {
            Tick = tick;
            WorldHash = worldHash;
            HasWorldHash = hasWorldHash;
        }
    }
}
