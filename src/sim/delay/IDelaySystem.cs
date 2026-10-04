using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    /// <summary>The sim.delay queries (14-interfaces-delay.md §14.10). No mutating entry point.</summary>
    public interface IDelaySystem : ISimSystem
    {
        /// <summary>Looks a flight's delay record up; false if unknown or pruned.</summary>
        bool TryGetFlightDelay(FlightId flight, out FlightDelay delay);

        /// <summary>The retained flights, ascending FlightId.</summary>
        IReadOnlyList<FlightId> RetainedFlights();

        /// <summary>The flight's allocation leaves, ascending DelayEventId. Empty if the flight is unknown.</summary>
        IReadOnlyList<DelayEventId> LeavesOf(FlightId flight);

        /// <summary>Looks a node up by id; false if unknown, removed or pruned.</summary>
        bool TryGetNode(DelayEventId id, out DelayNode node);
    }
}
