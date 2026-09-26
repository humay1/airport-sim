namespace AirportSim.Sim.Core
{
    /// <summary>A passenger cohort is blocked at a flow node. Spec: 10-events.md §10.9.</summary>
    public readonly struct FlowBlocked : ISimEvent
    {
        /// <summary>The blocked cohort.</summary>
        public CohortId Cohort { get; }

        /// <summary>The node the cohort is held at.</summary>
        public NodeId Held { get; }

        /// <summary>The node blocking progress.</summary>
        public NodeId BlockedBy { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public FlowBlocked(CohortId cohort, NodeId held, NodeId blockedBy)
        {
            Cohort = cohort;
            Held = held;
            BlockedBy = blockedBy;
        }
    }
}
