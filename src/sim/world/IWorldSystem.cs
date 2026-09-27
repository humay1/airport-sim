using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// The fixed landside walk graph and its precomputed routes. Spec:
    /// 18-interfaces-world.md §18.3. Every query is read-only, allocates
    /// nothing, and throws <see cref="System.ArgumentException"/> for an
    /// unknown id.
    /// </summary>
    public interface IWorldSystem : ISimSystem
    {
        /// <summary>Every node, in ascending <c>NodeId</c> order.</summary>
        IReadOnlyList<NodeId> Nodes();

        /// <summary>Metres walked when entering <paramref name="node"/>.</summary>
        uint LengthMetres(NodeId node);

        /// <summary>The edges leaving <paramref name="node"/>, in ascending <c>EdgeId</c> order.</summary>
        IReadOnlyList<EdgeId> OutEdges(NodeId node);

        /// <summary>The node <paramref name="edge"/> enters.</summary>
        NodeId EdgeTo(EdgeId edge);

        /// <summary>Whether any walk reaches <paramref name="destination"/> from <paramref name="from"/>.</summary>
        bool CanReach(NodeId from, NodeId destination);

        /// <summary>Whether the walk starting with <paramref name="firstEdge"/> reaches <paramref name="destination"/>.</summary>
        bool CanReachVia(EdgeId firstEdge, NodeId destination);

        /// <summary>
        /// The shortest path starting with <paramref name="firstEdge"/>: <c>EdgeTo(firstEdge)</c>
        /// through <paramref name="destination"/>, inclusive. Empty if
        /// <c>!CanReachVia(firstEdge, destination)</c>.
        /// </summary>
        IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination);
    }
}
