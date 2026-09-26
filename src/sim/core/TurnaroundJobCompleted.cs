namespace AirportSim.Sim.Core
{
    /// <summary>A turnaround job completed. Spec: 10-events.md §10.9.</summary>
    public readonly struct TurnaroundJobCompleted : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The job.</summary>
        public JobKind Job { get; }

        /// <summary>The job's planned start tick.</summary>
        public ulong PlannedStart { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public TurnaroundJobCompleted(FlightId flight, JobKind job, ulong plannedStart)
        {
            Flight = flight;
            Job = job;
            PlannedStart = plannedStart;
        }
    }
}
