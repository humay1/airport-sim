using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// One directed edge of the walk graph. A two-way link is two edges. Spec:
    /// 18-interfaces-world.md §18.2.
    /// </summary>
    public readonly struct WalkEdgeDef
    {
        /// <summary>The edge's id.</summary>
        public EdgeId Id { get; }

        /// <summary>The node this edge leaves.</summary>
        public NodeId From { get; }

        /// <summary>The node this edge enters.</summary>
        public NodeId To { get; }

        /// <summary>Constructs the definition from its id, source and target.</summary>
        public WalkEdgeDef(EdgeId id, NodeId from, NodeId to)
        {
            Id = id;
            From = from;
            To = to;
        }
    }
}
