namespace AirportSim.Sim.Core
{
    /// <summary>A runway hold on an aircraft was released. Spec: 10-events.md §10.9.</summary>
    public readonly struct AircraftHeldForRunwayReleased : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The runway it was waiting for.</summary>
        public RunwayId Runway { get; }

        /// <summary>The flight's position in the runway queue at release.</summary>
        public int QueuePosition { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public AircraftHeldForRunwayReleased(FlightId flight, RunwayId runway, int queuePosition)
        {
            Flight = flight;
            Runway = runway;
            QueuePosition = queuePosition;
        }
    }
}
