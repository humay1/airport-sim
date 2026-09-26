namespace AirportSim.Sim.Core
{
    /// <summary>A turnaround job is blocked waiting on a resource. Spec: 10-events.md §10.9.</summary>
    public readonly struct TurnaroundJobBlocked : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The job.</summary>
        public JobKind Job { get; }

        /// <summary>What kind of resource it is waiting on.</summary>
        public ResourceKind WaitingOn { get; }

        /// <summary>The specific resource it is waiting on, if known.</summary>
        public EntityId? Resource { get; }

        /// <summary>The delay category this wait is attributed to.</summary>
        public DelayCategory Category { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public TurnaroundJobBlocked(FlightId flight, JobKind job, ResourceKind waitingOn, EntityId? resource, DelayCategory category)
        {
            Flight = flight;
            Job = job;
            WaitingOn = waitingOn;
            Resource = resource;
            Category = category;
        }
    }
}
