using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// A presentation-only view of one agent; never sim state. Spec:
    /// 09-interfaces-flow.md §9.7, §9.1.
    /// </summary>
    public readonly struct AgentView
    {
        /// <summary>The agent's derived identity.</summary>
        public PassengerRef Ref { get; }

        /// <summary>The node the agent is on.</summary>
        public NodeId Node { get; }

        /// <summary>Progress along the current edge, 0..1.</summary>
        public Fx ProgressAlongEdge { get; }

        /// <summary>Constructs the view from its three fields.</summary>
        public AgentView(PassengerRef @ref, NodeId node, Fx progressAlongEdge)
        {
            Ref = @ref;
            Node = node;
            ProgressAlongEdge = progressAlongEdge;
        }
    }
}
