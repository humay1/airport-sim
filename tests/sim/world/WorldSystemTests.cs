using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// Interface conformance for IWorldSystem (18 §18.3), its construction
    /// (18 §18.4 "Construction") and its registry position (08 §8.5).
    /// </summary>
    public sealed class WorldSystemTests
    {
        private static WalkGraph Small()
        {
            // Ids deliberately inserted out of order; the builder sorts them as
            // WalkGraph promises (18 §18.2).
            return new GraphBuilder()
                .Node(30, 7)
                .Node(10, 0)
                .Node(20, 15)
                .Edge(9, 10, 30)
                .Edge(4, 10, 20)
                .Edge(6, 20, 30)
                .Edge(2, 30, 10)
                .Build();
        }

        [Fact]
        public void test_world_system_factory_creates_loader_and_system()
        {
            IWalkGraphLoader loader = WorldFactory.CreateGraphLoader();
            Assert.NotNull(loader);
            IWorldSystem world = WorldKit.Create(Small());
            Assert.NotNull(world);
            Assert.IsAssignableFrom<ISimSystem>(world);
        }

        [Fact]
        public void test_world_system_id_is_registry_position_one_and_name_is_module()
        {
            IWorldSystem world = WorldKit.Create(Small());
            Assert.Equal((ushort)1, world.Id.Value);
            Assert.Equal("sim.world", world.Name);
        }

        [Fact]
        public void test_world_system_registers_first_before_every_other_position()
        {
            // 08 §8.5: registration is strictly ascending, so position 1 must
            // precede sim.schedule (2), sim.airside (3) and sim.flow (4).
            ISimHostBuilder b = WorldKit.Builder();
            IWorldSystem world = WorldFactory.CreateSystem(b.Services, Small());
            b.Register(world);
            b.Register(new ProbeSystem(2));
            b.Register(new ProbeSystem(4));
            ISimHost host = b.Build();
            host.Step(1);
            Assert.Equal(1UL, host.CurrentTick);
        }

        [Fact]
        public void test_world_system_nodes_are_ascending_node_ids()
        {
            IWorldSystem world = WorldKit.Create(Small());
            Assert.Equal(new uint[] { 10, 20, 30 }, WorldKit.Ids(world.Nodes()));
        }

        [Fact]
        public void test_world_system_length_metres_matches_definition()
        {
            IWorldSystem world = WorldKit.Create(Small());
            Assert.Equal(0u, world.LengthMetres(new NodeId(10)));
            Assert.Equal(15u, world.LengthMetres(new NodeId(20)));
            Assert.Equal(7u, world.LengthMetres(new NodeId(30)));
        }

        [Fact]
        public void test_world_system_out_edges_are_ascending_edge_ids()
        {
            IWorldSystem world = WorldKit.Create(Small());
            Assert.Equal(new uint[] { 4, 9 }, WorldKit.Ids(world.OutEdges(new NodeId(10))));
            Assert.Equal(new uint[] { 6 }, WorldKit.Ids(world.OutEdges(new NodeId(20))));
            Assert.Equal(new uint[] { 2 }, WorldKit.Ids(world.OutEdges(new NodeId(30))));
        }

        [Fact]
        public void test_world_system_out_edges_empty_for_node_without_edges()
        {
            WalkGraph g = new GraphBuilder().Node(1, 5).Node(2, 5).Edge(1, 1, 2).Build();
            IWorldSystem world = WorldKit.Create(g);
            IReadOnlyList<EdgeId> none = world.OutEdges(new NodeId(2));
            Assert.NotNull(none);
            Assert.Empty(none);
        }

        [Fact]
        public void test_world_system_edge_to_is_edge_destination()
        {
            IWorldSystem world = WorldKit.Create(Small());
            Assert.Equal(new NodeId(30), world.EdgeTo(new EdgeId(9)));
            Assert.Equal(new NodeId(20), world.EdgeTo(new EdgeId(4)));
            Assert.Equal(new NodeId(30), world.EdgeTo(new EdgeId(6)));
            Assert.Equal(new NodeId(10), world.EdgeTo(new EdgeId(2)));
        }

        [Fact]
        public void test_world_system_unknown_node_or_edge_throws()
        {
            // 18 §18.3: a query naming an unknown id is a programmer error and
            // throws ArgumentException (Q-030), whichever argument is unknown.
            IWorldSystem world = WorldKit.Create(Small());
            var node = new NodeId(10);
            var edge = new EdgeId(4);
            var badNode = new NodeId(99);
            var badEdge = new EdgeId(99);
            var zeroNode = new NodeId(0);
            var zeroEdge = new EdgeId(0);

            Assert.Throws<ArgumentException>(() => world.LengthMetres(badNode));
            Assert.Throws<ArgumentException>(() => world.LengthMetres(zeroNode));
            Assert.Throws<ArgumentException>(() => world.OutEdges(badNode));
            Assert.Throws<ArgumentException>(() => world.EdgeTo(badEdge));
            Assert.Throws<ArgumentException>(() => world.EdgeTo(zeroEdge));
            Assert.Throws<ArgumentException>(() => world.CanReach(badNode, node));
            Assert.Throws<ArgumentException>(() => world.CanReach(node, badNode));
            Assert.Throws<ArgumentException>(() => world.CanReachVia(badEdge, node));
            Assert.Throws<ArgumentException>(() => world.CanReachVia(edge, badNode));
            Assert.Throws<ArgumentException>(() => world.PathVia(badEdge, node));
            Assert.Throws<ArgumentException>(() => world.PathVia(edge, badNode));
        }

        [Fact]
        public void test_world_system_queries_are_read_only()
        {
            // 18 §18.3: every query is read-only. Querying everything, twice,
            // must leave every answer and the hash unchanged.
            WalkGraph g = Small();
            IWorldSystem world = WorldKit.Create(g);
            ulong hashBefore = world.ComputeStateHash();
            string first = Snapshot(world);
            string second = Snapshot(world);
            Assert.Equal(first, second);
            Assert.Equal(hashBefore, world.ComputeStateHash());
        }

        internal static string Snapshot(IWorldSystem world)
        {
            var sb = new System.Text.StringBuilder();
            IReadOnlyList<NodeId> nodes = world.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId n = nodes[i];
                sb.Append('N').Append(n.Value).Append(':').Append(world.LengthMetres(n)).Append(';');
                IReadOnlyList<EdgeId> outs = world.OutEdges(n);
                for (int k = 0; k < outs.Count; k++)
                {
                    EdgeId e = outs[k];
                    sb.Append('E').Append(e.Value).Append('>').Append(world.EdgeTo(e).Value).Append(';');
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        NodeId d = nodes[j];
                        sb.Append(world.CanReachVia(e, d) ? 'y' : 'n');
                        sb.Append('[').Append(string.Join(",", WorldKit.Ids(world.PathVia(e, d)))).Append(']');
                    }
                }

                for (int j = 0; j < nodes.Count; j++)
                {
                    if (nodes[j] != n)
                    {
                        sb.Append(world.CanReach(n, nodes[j]) ? 'Y' : 'N');
                    }
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
