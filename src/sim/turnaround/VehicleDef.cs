using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>One fleet member (13-interfaces-turnaround.md §13.4).</summary>
    public readonly struct VehicleDef
    {
        /// <summary>The vehicle's id, unique within the fleet.</summary>
        public VehicleId Id { get; }

        /// <summary>The vehicle's kind.</summary>
        public VehicleKind Kind { get; }

        /// <summary>Creates a fleet member.</summary>
        public VehicleDef(VehicleId id, VehicleKind kind)
        {
            Id = id;
            Kind = kind;
        }
    }
}
