namespace AirportSim.Sim.Flow
{
    /// <summary>A flow node's behaviour. Spec: 09-interfaces-flow.md §9.2.</summary>
    public enum NodeKind
    {
        /// <summary>Kerbside, rail box, arriving aircraft door.</summary>
        Source,

        /// <summary>A walkable link with a traversal time.</summary>
        Corridor,

        /// <summary>An unqueued dwell space with a capacity.</summary>
        Hall,

        /// <summary>A served node with a throughput model (§9.4).</summary>
        Queue,

        /// <summary>A boarding hold.</summary>
        Gate,

        /// <summary>A departed aircraft or a landside exit.</summary>
        Sink
    }
}
