using System.Collections.Generic;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// The fixed landside walk graph, loaded from a fixture and immutable for
    /// the session. Spec: 18-interfaces-world.md §18.2.
    /// </summary>
    public readonly struct WalkGraph
    {
        /// <summary>Every node, in ascending <c>NodeId</c> order.</summary>
        public IReadOnlyList<WalkNodeDef> Nodes { get; }

        /// <summary>Every edge, in ascending <c>EdgeId</c> order.</summary>
        public IReadOnlyList<WalkEdgeDef> Edges { get; }

        /// <summary>FNV-1a-64 over the raw fixture bytes the graph was loaded from.</summary>
        public ulong FixtureHash { get; }

        /// <summary>Constructs the graph from its nodes, edges and fixture hash.</summary>
        public WalkGraph(IReadOnlyList<WalkNodeDef> nodes, IReadOnlyList<WalkEdgeDef> edges, ulong fixtureHash)
        {
            Nodes = nodes;
            Edges = edges;
            FixtureHash = fixtureHash;
        }
    }
}
