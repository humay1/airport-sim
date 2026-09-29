using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// The published timetable: flight records, publication and rotations.
    /// Spec: 11-interfaces-schedule.md §11.7. There is no mutating entry point.
    /// </summary>
    public interface IScheduleSystem : ISimSystem
    {
        /// <summary>Looks up a flight, published or not, by id.</summary>
        bool TryGetFlight(FlightId id, out FlightRecord flight);

        /// <summary>Every published flight, ascending <see cref="FlightId"/>.</summary>
        IReadOnlyList<FlightId> PublishedFlights();

        /// <summary>
        /// Flights of <paramref name="kind"/> whose <see cref="FlightRecord.ScheduledTick"/>
        /// falls in <c>[fromInclusive, toExclusive)</c>, published and unpublished alike,
        /// ascending <see cref="FlightRecord.ScheduledTick"/> then <see cref="FlightId"/>.
        /// </summary>
        IReadOnlyList<FlightId> MovementsBetween(ulong fromInclusive, ulong toExclusive, MovementKind kind);

        /// <summary>The flight's rotation counterpart, if it has one.</summary>
        bool TryGetRotation(FlightId flight, out FlightId counterpart);

        /// <summary>
        /// Passengers of <paramref name="flight"/> not yet injected; 0 for an unknown or
        /// not-yet-published flight.
        /// </summary>
        int PendingInjectionCount(FlightId flight);
    }
}
