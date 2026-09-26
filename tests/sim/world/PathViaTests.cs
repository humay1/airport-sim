using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 18 §18.3 PathVia: the shortest path starting with firstEdge, listed as
    /// EdgeTo(firstEdge) ... destination inclusive; empty if !CanReachVia.
    /// </summary>
    public sealed class PathViaTests
    {
        [Fact]
        public void test_path_via_includes_endpoints_and_is_empty_when_unreachable()
        {
            // 1 -e1-> 2 -e2-> 3 -e3-> 4, and a dead end 1 -e4-> 5.
            WalkGraph g = new GraphBuilder()
                .Node(1, 0).Node(2, 4).Node(3, 6).Node(4, 8).Node(5, 2)
                .Edge(1, 1, 2).Edge(2, 2, 3).Edge(3, 3, 4).Edge(4, 1, 5)
                .Build();
            IWorldSystem world = WorldKit.Create(g);

            // First element is EdgeTo(firstEdge), last is the destination; the
            // start node (the edge's From) is not listed.
            Assert.Equal(new uint[] { 2, 3, 4 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(4))));

            // Destination is the first edge's own target: a one-element path.
            Assert.True(world.CanReachVia(new EdgeId(1), new NodeId(2)));
            Assert.Equal(new uint[] { 2 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(2))));

            // Unreachable through this first edge (though reachable from its
            // From node through another edge): empty, not null.
            Assert.False(world.CanReachVia(new EdgeId(4), new NodeId(4)));
            IReadOnlyList<NodeId> none = world.PathVia(new EdgeId(4), new NodeId(4));
            Assert.NotNull(none);
            Assert.Empty(none);

            // Against edge direction: empty.
            Assert.False(world.CanReachVia(new EdgeId(3), new NodeId(2)));
            Assert.Empty(world.PathVia(new EdgeId(3), new NodeId(2)));
        }

        [Fact]
        public void test_path_via_may_pass_back_through_the_start()
        {
            // The first edge is forced. From 2 the only way to 3 is back through
            // 1, so the path re-enters the start node.
            WalkGraph g = new GraphBuilder()
                .Node(1, 3).Node(2, 5).Node(3, 7)
                .Edge(1, 1, 2).Edge(2, 2, 1).Edge(3, 1, 3)
                .Build();
            IWorldSystem world = WorldKit.Create(g);
            Assert.True(world.CanReachVia(new EdgeId(1), new NodeId(3)));
            Assert.Equal(new uint[] { 2, 1, 3 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(3))));

            // Returning to the start itself through a cycle.
            Assert.True(world.CanReachVia(new EdgeId(1), new NodeId(1)));
            Assert.Equal(new uint[] { 2, 1 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(1))));
        }

        [Fact]
        public void test_path_via_is_consistent_path_of_existing_edges()
        {
            // Consecutive entries of every non-empty path are joined by an edge,
            // on the fixture graph.
            WalkGraph g = Phase0Landside.Load();
            IWorldSystem world = WorldKit.Create(g);
            IReadOnlyList<NodeId> nodes = world.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                IReadOnlyList<EdgeId> outs = world.OutEdges(nodes[i]);
                for (int k = 0; k < outs.Count; k++)
                {
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        IReadOnlyList<NodeId> path = world.PathVia(outs[k], nodes[j]);
                        Assert.Equal(world.CanReachVia(outs[k], nodes[j]), path.Count > 0);
                        if (path.Count == 0)
                        {
                            continue;
                        }

                        Assert.Equal(world.EdgeTo(outs[k]), path[0]);
                        Assert.Equal(nodes[j], path[path.Count - 1]);
                        for (int p = 1; p < path.Count; p++)
                        {
                            Assert.True(HasEdge(world, path[p - 1], path[p]), "no edge " + path[p - 1].Value + " -> " + path[p].Value);
                        }
                    }
                }
            }
        }

        private static bool HasEdge(IWorldSystem world, NodeId from, NodeId to)
        {
            IReadOnlyList<EdgeId> outs = world.OutEdges(from);
            for (int i = 0; i < outs.Count; i++)
            {
                if (world.EdgeTo(outs[i]) == to)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
