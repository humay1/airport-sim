namespace AirportSim.Sim.Core
{
    /// <summary>An aircraft is held on a taxiway edge. Spec: 10-events.md §10.9.</summary>
    public readonly struct AircraftHeldOnTaxiway : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The taxiway edge it is held on.</summary>
        public TaxiEdgeId Edge { get; }

        /// <summary>The flight blocking it, if known.</summary>
        public FlightId? Blocking { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public AircraftHeldOnTaxiway(FlightId flight, TaxiEdgeId edge, FlightId? blocking)
        {
            Flight = flight;
            Edge = edge;
            Blocking = blocking;
        }
    }
}
