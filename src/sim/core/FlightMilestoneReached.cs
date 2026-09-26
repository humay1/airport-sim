namespace AirportSim.Sim.Core
{
    /// <summary>A flight reached a lifecycle milestone. Spec: 10-events.md §10.9.</summary>
    public readonly struct FlightMilestoneReached : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The milestone reached.</summary>
        public FlightMilestone Milestone { get; }

        /// <summary>The planned tick for this milestone.</summary>
        public ulong PlannedTick { get; }

        /// <summary>The actual tick this milestone was reached.</summary>
        public ulong ActualTick { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public FlightMilestoneReached(FlightId flight, FlightMilestone milestone, ulong plannedTick, ulong actualTick)
        {
            Flight = flight;
            Milestone = milestone;
            PlannedTick = plannedTick;
            ActualTick = actualTick;
        }
    }
}
