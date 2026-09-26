using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One named, per-system draw stream: xoshiro256** 1.0, SplitMix64-seeded. Spec:
    /// 08-interfaces-core.md §8.8 "Exact reference". Part of the save format.
    /// </summary>
    public interface IRandomStream
    {
        /// <summary>The next raw 64-bit draw.</summary>
        ulong NextUInt64();

        /// <summary>The next integer in [minInclusive, maxExclusive).</summary>
        int NextInt(int minInclusive, int maxExclusive);

        /// <summary>The next fixed-point value in [0, 1).</summary>
        Fx NextFx01();

        /// <summary>True with the given probability, false otherwise.</summary>
        bool Chance(Fx probability);

        /// <summary>Shuffles items in place.</summary>
        void Shuffle<T>(Span<T> items);

        /// <summary>The stream's current state, hashed for determinism checks.</summary>
        ulong ComputeStateHash();
    }
}
