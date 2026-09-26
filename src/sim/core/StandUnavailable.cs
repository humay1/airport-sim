namespace AirportSim.Sim.Core
{
    /// <summary>No stand is available for a flight. Spec: 10-events.md §10.9.</summary>
    public readonly struct StandUnavailable : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The stand it wanted, if a specific one was targeted.</summary>
        public StandId? Stand { get; }

        /// <summary>The flight occupying the stand, if known.</summary>
        public FlightId? Occupying { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public StandUnavailable(FlightId flight, StandId? stand, FlightId? occupying)
        {
            Flight = flight;
            Stand = stand;
            Occupying = occupying;
        }
    }
}
