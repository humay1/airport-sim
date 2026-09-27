using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// A flight's passengers still upstream of the gate. Spec:
    /// 09-interfaces-flow.md §9.7a (D6, LOW CONFIDENCE).
    /// </summary>
    public readonly struct OutstandingPassengers
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>The count still upstream; always &gt; 0.</summary>
        public int Count { get; }

        /// <summary>The node holding the largest share; ties by ascending <see cref="NodeId"/>.</summary>
        public NodeId MostHeldAt { get; }

        /// <summary>Constructs the value from its three fields.</summary>
        public OutstandingPassengers(FlightId flight, int count, NodeId mostHeldAt)
        {
            Flight = flight;
            Count = count;
            MostHeldAt = mostHeldAt;
        }
    }
}
