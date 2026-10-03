using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>The state of one vehicle (13-interfaces-turnaround.md §13.3).</summary>
    public readonly struct VehicleState
    {
        /// <summary>The vehicle's id.</summary>
        public VehicleId Id { get; }

        /// <summary>The vehicle's kind.</summary>
        public VehicleKind Kind { get; }

        /// <summary>The Active job it serves, or null when free.</summary>
        public JobId? Assignment { get; }

        /// <summary>Creates a vehicle snapshot.</summary>
        public VehicleState(VehicleId id, VehicleKind kind, JobId? assignment)
        {
            Id = id;
            Kind = kind;
            Assignment = assignment;
        }
    }
}
