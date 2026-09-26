namespace AirportSim.Sim.Core
{
    /// <summary>Passengers missed their flight. Spec: 10-events.md §10.9.</summary>
    public readonly struct PassengersMissedFlight : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The number of passengers that missed it.</summary>
        public int Count { get; }

        /// <summary>The last flow node they were blocked at.</summary>
        public NodeId LastBlockedAt { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public PassengersMissedFlight(FlightId flight, int count, NodeId lastBlockedAt)
        {
            Flight = flight;
            Count = count;
            LastBlockedAt = lastBlockedAt;
        }
    }
}
