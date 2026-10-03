using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>The sim.turnaround queries (13-interfaces-turnaround.md §13.7). No mutating entry point.</summary>
    public interface ITurnaroundSystem : ISimSystem
    {
        /// <summary>Looks a job up by its derived id.</summary>
        bool TryGetJob(JobId id, out TurnaroundJob job);

        /// <summary>The ids of the flight's jobs, ascending JobKind. Empty before its OnStand.</summary>
        IReadOnlyList<JobId> JobsForFlight(FlightId flight);

        /// <summary>Looks a vehicle up by id.</summary>
        bool TryGetVehicle(VehicleId id, out VehicleState vehicle);

        /// <summary>The free vehicles of a kind, ascending VehicleId.</summary>
        IReadOnlyList<VehicleId> FreeVehicles(VehicleKind kind);
    }
}
