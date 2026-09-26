namespace AirportSim.Sim.Core
{
    /// <summary>A passenger hold on a departure was released. Spec: 10-events.md §10.9. <see cref="HeldAt"/> is always null.</summary>
    public readonly struct DepartureHeldForPassengersReleased : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The number of passengers still outstanding (0 at release).</summary>
        public int Outstanding { get; }

        /// <summary>Always null: the hold has cleared.</summary>
        public NodeId? HeldAt { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public DepartureHeldForPassengersReleased(FlightId flight, int outstanding, NodeId? heldAt)
        {
            Flight = flight;
            Outstanding = outstanding;
            HeldAt = heldAt;
        }
    }
}
