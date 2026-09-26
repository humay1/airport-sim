using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 18 §18.3: cost is the sum of LengthMetres of the nodes entered after the
    /// start, including the destination; ties go to the lexicographically
    /// smallest EdgeId sequence; nothing depends on edge-list or file order.
    /// </summary>
    public sealed class RouteTests
    {
        [Fact]
        public void test_route_is_shortest_by_length_ties_by_edge_sequence()
        {
            // Shortest by length: through M the lexicographically first and
            // fewest-hop branch (e2: X, 100 m) loses to the longer chain of short
            // nodes (e4, e5, e6: P and Q, 1 m each).
            WalkGraph byLength = new GraphBuilder()
                .Node(1, 0).Node(2, 5).Node(3, 100).Node(4, 1).Node(5, 1).Node(6, 5)
                .Edge(1, 1, 2)
                .Edge(2, 2, 3).Edge(3, 3, 6)
                .Edge(4, 2, 4).Edge(5, 4, 5).Edge(6, 5, 6)
                .Build();
            IWorldSystem w1 = WorldKit.Create(byLength);
            Assert.Equal(new uint[] { 2, 4, 5, 6 }, WorldKit.Ids(w1.PathVia(new EdgeId(1), new NodeId(6))));

            // Tie by edge sequence: A and B both cost 10 + 5. [e1, e3, e8] is
            // lexicographically smaller than [e1, e7, e2], although e7 + e2 has
            // the smaller edge-id sum and A has the smaller node id.
            WalkGraph tie = new GraphBuilder()
                .Node(1, 0).Node(2, 5).Node(3, 10).Node(4, 10).Node(5, 5)
                .Edge(1, 1, 2)
                .Edge(7, 2, 3).Edge(2, 3, 5)
                .Edge(3, 2, 4).Edge(8, 4, 5)
                .Build();
            IWorldSystem w2 = WorldKit.Create(tie);
            Assert.Equal(new uint[] { 2, 4, 5 }, WorldKit.Ids(w2.PathVia(new EdgeId(1), new NodeId(5))));

            // Mirror: renumber so the A branch is now lexicographically first.
            WalkGraph mirrored = new GraphBuilder()
                .Node(1, 0).Node(2, 5).Node(3, 10).Node(4, 10).Node(5, 5)
                .Edge(1, 1, 2)
                .Edge(3, 2, 3).Edge(8, 3, 5)
                .Edge(7, 2, 4).Edge(2, 4, 5)
                .Build();
            IWorldSystem w3 = WorldKit.Create(mirrored);
            Assert.Equal(new uint[] { 2, 3, 5 }, WorldKit.Ids(w3.PathVia(new EdgeId(1), new NodeId(5))));
        }

        [Fact]
        public void test_route_zero_length_cycle_is_never_repeated()
        {
            // A(0) <-> B(0) is a zero-cost cycle. A route may not repeat a node
            // (Q-031), so from A the candidates are [e9] direct and [e2, e10]
            // via B. Both cost LengthMetres(D); [e2, e10] is lexicographically
            // smaller. [e2, e3, e9] (A, B, A, D) would be smaller still, but
            // repeats A and is not a candidate.
            WalkGraph g = new GraphBuilder()
                .Node(1, 4).Node(2, 0).Node(3, 0).Node(4, 6)
                .Edge(1, 1, 2)
                .Edge(2, 2, 3).Edge(3, 3, 2)
                .Edge(9, 2, 4).Edge(10, 3, 4)
                .Build();
            IWorldSystem world = WorldKit.Create(g);
            Assert.Equal(new uint[] { 2, 3, 4 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(4))));

            // Without the B -> D edge only the direct route is simple.
            WalkGraph noShortcut = new GraphBuilder()
                .Node(1, 4).Node(2, 0).Node(3, 0).Node(4, 6)
                .Edge(1, 1, 2)
                .Edge(2, 2, 3).Edge(3, 3, 2)
                .Edge(9, 2, 4)
                .Build();
            IWorldSystem w2 = WorldKit.Create(noShortcut);
            Assert.Equal(new uint[] { 2, 4 }, WorldKit.Ids(w2.PathVia(new EdgeId(1), new NodeId(4))));
        }

        [Fact]
        public void test_route_cost_is_node_length_not_hop_count()
        {
            // Two zero-length nodes (Z1, Z2) cost nothing to cross; one 1 m node
            // (O) costs 1. The zero-length branch wins despite an extra hop and
            // lexicographically larger edge ids.
            WalkGraph g = new GraphBuilder()
                .Node(1, 0).Node(2, 3).Node(3, 0).Node(4, 0).Node(5, 1).Node(6, 4)
                .Edge(1, 1, 2)
                .Edge(8, 2, 3).Edge(9, 3, 4).Edge(10, 4, 6)
                .Edge(2, 2, 5).Edge(3, 5, 6)
                .Build();
            IWorldSystem world = WorldKit.Create(g);
            Assert.Equal(new uint[] { 2, 3, 4, 6 }, WorldKit.Ids(world.PathVia(new EdgeId(1), new NodeId(6))));
        }

        [Fact]
        public void test_route_property_matches_exhaustive_oracle()
        {
            // 07 L4 property test: SplitMix64 inputs, seed literal in the test.
            CheckAgainstOracle(0x12F0_0012UL, 1);
        }

        [Fact]
        public void test_route_property_with_zero_length_cycles_matches_simple_path_oracle()
        {
            // Q-031: only simple paths are candidates, so zero-length nodes and
            // zero-cost cycles are well defined and must match the oracle.
            CheckAgainstOracle(0x0C1C_1E00UL, 0);
        }

        private static void CheckAgainstOracle(ulong seed, int minLength)
        {
            var rng = new SplitMix64(seed);
            for (int iteration = 0; iteration < 300; iteration++)
            {
                WalkGraph g = WorldKit.RandomGraph(rng, 7, 3, 35, minLength);
                IWorldSystem world = WorldKit.Create(g);
                var oracle = new RouteOracle(g);
                string where = "seed 0x" + seed.ToString("X") + ", iteration " + iteration + ", graph " + WorldKit.Content(g);

                IReadOnlyList<NodeId> nodes = world.Nodes();
                Assert.True(nodes.Count == g.Nodes.Count, where);
                for (int i = 0; i < nodes.Count; i++)
                {
                    Assert.True(nodes[i] == g.Nodes[i].Id, where);
                    Assert.True(world.LengthMetres(nodes[i]) == g.Nodes[i].LengthMetres, where);
                }

                for (int i = 0; i < g.Edges.Count; i++)
                {
                    Assert.True(world.EdgeTo(g.Edges[i].Id) == g.Edges[i].To, where);
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    NodeId from = nodes[i];
                    IReadOnlyList<EdgeId> outs = world.OutEdges(from);
                    var expectedOuts = new List<uint>();
                    for (int k = 0; k < g.Edges.Count; k++)
                    {
                        if (g.Edges[k].From == from)
                        {
                            expectedOuts.Add(g.Edges[k].Id.Value);
                        }
                    }

                    Assert.True(string.Join(",", expectedOuts) == string.Join(",", WorldKit.Ids(outs)), where + ", OutEdges(" + from.Value + ")");

                    for (int j = 0; j < nodes.Count; j++)
                    {
                        NodeId dest = nodes[j];
                        bool anyVia = false;
                        for (int k = 0; k < outs.Count; k++)
                        {
                            uint[]? expected = oracle.PathVia(outs[k].Value, dest.Value);
                            bool via = world.CanReachVia(outs[k], dest);
                            uint[] actual = WorldKit.Ids(world.PathVia(outs[k], dest));
                            string at = where + ", via e" + outs[k].Value + " to n" + dest.Value;
                            Assert.True(via == (expected != null), at + ": CanReachVia");
                            Assert.True(string.Join(",", expected ?? new uint[0]) == string.Join(",", actual), at + ": PathVia [" + string.Join(",", actual) + "]");
                            anyVia |= via;
                        }

                        bool reach = world.CanReach(from, dest);
                        Assert.True(reach == oracle.Reaches(from.Value, dest.Value), where + ", CanReach(" + from.Value + ", " + dest.Value + ")");
                        if (from != dest)
                        {
                            Assert.True(reach == anyVia, where + ", CanReach agrees with CanReachVia over OutEdges");
                        }

                        for (int k = 0; k < outs.Count; k++)
                        {
                            // Q-031: CanReachVia(e, d) is CanReach(EdgeTo(e), d).
                            Assert.True(world.CanReachVia(outs[k], dest) == world.CanReach(world.EdgeTo(outs[k]), dest), where + ", CanReachVia(e" + outs[k].Value + ", " + dest.Value + ")");
                        }
                    }
                }
            }
        }
    }
}
