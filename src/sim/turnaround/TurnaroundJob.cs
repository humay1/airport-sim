using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>One ground-handling job of one flight (13-interfaces-turnaround.md §13.3).</summary>
    public readonly struct TurnaroundJob
    {
        /// <summary>The derived id: (Flight.Value &lt;&lt; 8) | Kind (§13.3).</summary>
        public JobId Id { get; }

        /// <summary>The flight the job serves.</summary>
        public FlightId Flight { get; }

        /// <summary>What the job is.</summary>
        public JobKind Kind { get; }

        /// <summary>Blocked, Active or Completed.</summary>
        public JobStatus Status { get; }

        /// <summary>The vehicle serving the job, or null. A completed job keeps the vehicle that served it.</summary>
        public VehicleId? Vehicle { get; }

        /// <summary>The tick the job was created, the flight's OnStand.</summary>
        public ulong CreatedAt { get; }

        /// <summary>The tick the job started, TICK_UNSCHEDULED while Blocked.</summary>
        public ulong StartedAt { get; }

        /// <summary>StartedAt plus the nominal duration, TICK_UNSCHEDULED while Blocked.</summary>
        public ulong DueAt { get; }

        /// <summary>Creates a job snapshot.</summary>
        public TurnaroundJob(JobId id, FlightId flight, JobKind kind, JobStatus status, VehicleId? vehicle, ulong createdAt, ulong startedAt, ulong dueAt)
        {
            Id = id;
            Flight = flight;
            Kind = kind;
            Status = status;
            Vehicle = vehicle;
            CreatedAt = createdAt;
            StartedAt = startedAt;
            DueAt = dueAt;
        }
    }
}
