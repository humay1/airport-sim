namespace AirportSim.Sim.Core
{
    /// <summary>A blocked turnaround job resumed. Spec: 10-events.md §10.9.</summary>
    public readonly struct TurnaroundJobUnblocked : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The job.</summary>
        public JobKind Job { get; }

        /// <summary>What kind of resource it had been waiting on.</summary>
        public ResourceKind WaitingOn { get; }

        /// <summary>The specific resource it had been waiting on, if known.</summary>
        public EntityId? Resource { get; }

        /// <summary>The delay category the wait was attributed to.</summary>
        public DelayCategory Category { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public TurnaroundJobUnblocked(FlightId flight, JobKind job, ResourceKind waitingOn, EntityId? resource, DelayCategory category)
        {
            Flight = flight;
            Job = job;
            WaitingOn = waitingOn;
            Resource = resource;
            Category = category;
        }
    }
}
