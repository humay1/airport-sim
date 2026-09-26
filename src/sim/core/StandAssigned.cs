namespace AirportSim.Sim.Core
{
    /// <summary>A stand was assigned to a flight. Spec: 10-events.md §10.9.</summary>
    public readonly struct StandAssigned : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The assigned stand.</summary>
        public StandId? Stand { get; }

        /// <summary>The flight that had been occupying the stand, if any.</summary>
        public FlightId? Occupying { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public StandAssigned(FlightId flight, StandId? stand, FlightId? occupying)
        {
            Flight = flight;
            Stand = stand;
            Occupying = occupying;
        }
    }
}
