namespace AirportSim.Sim.Core
{
    /// <summary>A departure is held for outstanding passengers. Spec: 10-events.md §10.9.</summary>
    public readonly struct DepartureHeldForPassengers : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The number of passengers still outstanding.</summary>
        public int Outstanding { get; }

        /// <summary>The node the outstanding passengers are last known held at.</summary>
        public NodeId? HeldAt { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public DepartureHeldForPassengers(FlightId flight, int outstanding, NodeId? heldAt)
        {
            Flight = flight;
            Outstanding = outstanding;
            HeldAt = heldAt;
        }
    }
}
