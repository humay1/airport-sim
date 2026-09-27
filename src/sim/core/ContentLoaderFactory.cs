namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Builds the one <see cref="IContentLoader"/> implementation. Spec:
    /// 08-interfaces-core.md §8.11 "The loader" (Q-011). Stateless, caches
    /// nothing, reads nothing but its argument (07-conventions.md "Factories").
    /// </summary>
    public static class ContentLoaderFactory
    {
        /// <summary>Creates a fresh loader. Never throws.</summary>
        public static IContentLoader Create()
        {
            return new ContentLoader();
        }
    }
}
