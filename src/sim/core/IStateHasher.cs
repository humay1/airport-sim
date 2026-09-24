using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Feeds a system's serialisable state, in declared order, into an FNV-1a-64 hash.
    /// Spec: 08-interfaces-core.md §8.9. No public construction path is published yet
    /// (pending the Architect's decision on how modules obtain one); sim.core's own
    /// world-hash computation uses an internal implementation.
    /// </summary>
    public interface IStateHasher
    {
        void Feed(ulong v);
        void Feed(long v);
        void Feed(in Fx v);
        void Feed(bool v);
        void Feed(ReadOnlySpan<byte> v);
        ulong Result { get; }
    }
}
