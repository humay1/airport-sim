namespace AirportSim.Sim.Core
{
    /// <summary>A previously blocked passenger cohort resumed. Spec: 10-events.md §10.9.</summary>
    public readonly struct FlowUnblocked : ISimEvent
    {
        /// <summary>The cohort.</summary>
        public CohortId Cohort { get; }

        /// <summary>The node the cohort was held at.</summary>
        public NodeId Held { get; }

        /// <summary>The node that was blocking progress.</summary>
        public NodeId BlockedBy { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public FlowUnblocked(CohortId cohort, NodeId held, NodeId blockedBy)
        {
            Cohort = cohort;
            Held = held;
            BlockedBy = blockedBy;
        }
    }
}
