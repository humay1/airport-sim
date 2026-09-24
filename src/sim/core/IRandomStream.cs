using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One named, per-system draw stream. Spec: 08-interfaces-core.md §8.8. Shape only at
    /// T-001 (pinned xoshiro256** algorithm is T-002's).
    /// </summary>
    public interface IRandomStream
    {
        ulong NextUInt64();
        int NextInt(int minInclusive, int maxExclusive);
        Fx NextFx01();
        bool Chance(Fx probability);
        void Shuffle<T>(Span<T> items);
        ulong ComputeStateHash();
    }
}
