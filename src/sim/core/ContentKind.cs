namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11.</summary>
    public enum ContentKind
    {
        /// <summary>An aircraft size category definition.</summary>
        SizeCategory,

        /// <summary>An aircraft type definition.</summary>
        Aircraft,

        /// <summary>A passenger profile definition.</summary>
        PaxProfile,

        /// <summary>A queue profile definition.</summary>
        QueueProfile
    }
}
