namespace AirportSim.Sim.Core
{
    /// <summary>Passengers arrived at their departure gate. Spec: 10-events.md §10.9.</summary>
    public readonly struct PassengersArrivedAtGate : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The number of passengers that arrived.</summary>
        public int Count { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public PassengersArrivedAtGate(FlightId flight, int count)
        {
            Flight = flight;
            Count = count;
        }
    }
}
