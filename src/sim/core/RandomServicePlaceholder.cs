using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A placeholder <see cref="IRandomService"/>: <see cref="MasterSeed"/> is real (the
    /// config's), but <see cref="Stream"/> throws. T-002 replaces this with the pinned
    /// xoshiro256** service (08-interfaces-core.md §8.8, Q-019). Shape only at T-001 (Q-014).
    /// </summary>
    internal sealed class RandomServicePlaceholder : IRandomService
    {
        public RandomServicePlaceholder(ulong masterSeed)
        {
            MasterSeed = masterSeed;
        }

        public ulong MasterSeed { get; }

        public IRandomStream Stream(RngStreamName name)
        {
            throw new InvalidOperationException(
                "IRandomService is a T-001 placeholder; T-002 gives it real streams (08-interfaces-core.md §8.8).");
        }
    }
}
