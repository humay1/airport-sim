using System.Collections.Generic;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>The vehicle fleet (13-interfaces-turnaround.md §13.4).</summary>
    public readonly struct TurnaroundFleet
    {
        /// <summary>The vehicles.</summary>
        public IReadOnlyList<VehicleDef> Vehicles { get; }

        /// <summary>Creates a fleet over the given vehicles.</summary>
        public TurnaroundFleet(IReadOnlyList<VehicleDef> vehicles)
        {
            Vehicles = vehicles;
        }
    }
}
