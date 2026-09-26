namespace AirportSim.Sim.Core
{
    /// <summary>An aircraft is held waiting for a runway slot. Spec: 10-events.md §10.9.</summary>
    public readonly struct AircraftHeldForRunway : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The runway it is waiting for.</summary>
        public RunwayId Runway { get; }

        /// <summary>The flight's position in the runway queue.</summary>
        public int QueuePosition { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public AircraftHeldForRunway(FlightId flight, RunwayId runway, int queuePosition)
        {
            Flight = flight;
            Runway = runway;
            QueuePosition = queuePosition;
        }
    }
}
