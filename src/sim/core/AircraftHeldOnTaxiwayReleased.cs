namespace AirportSim.Sim.Core
{
    /// <summary>A taxiway hold on an aircraft was released. Spec: 10-events.md §10.9.</summary>
    public readonly struct AircraftHeldOnTaxiwayReleased : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The taxiway edge it was held on.</summary>
        public TaxiEdgeId Edge { get; }

        /// <summary>The flight that was blocking it, if known.</summary>
        public FlightId? Blocking { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public AircraftHeldOnTaxiwayReleased(FlightId flight, TaxiEdgeId edge, FlightId? blocking)
        {
            Flight = flight;
            Edge = edge;
            Blocking = blocking;
        }
    }
}
