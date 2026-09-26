namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Constructs the one <see cref="IRandomService"/> implementation. Spec:
    /// 08-interfaces-core.md §8.8, §8.11a. Stateless, caches nothing, reads
    /// nothing but its argument (07-conventions.md "Factories").
    /// </summary>
    public static class RandomServiceFactory
    {
        /// <summary>Creates a fresh service seeded from <paramref name="masterSeed"/>. Never throws.</summary>
        public static IRandomService Create(ulong masterSeed)
        {
            return new RandomService(masterSeed);
        }
    }
}
