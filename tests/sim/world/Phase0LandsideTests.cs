using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// The shared fixture tests/fixtures/world/phase0-landside.json and its
    /// binding requirements (18 §18.6). T-007, T-011, T-023 and the render
    /// layout (15 §15.12) build on it, so its shape is asserted here.
    /// </summary>
    public sealed class Phase0LandsideTests
    {
        [Fact]
        public void test_phase0_landside_loads_to_the_documented_graph()
        {
            Assert.Equal(WorldKit.Content(Phase0Landside.Build()), WorldKit.Content(Phase0Landside.Load()));
        }

        [Fact]
        public void test_phase0_landside_every_source_reaches_the_gate_only_through_security()
        {
            WalkGraph g = Phase0Landside.Load();
            var security = new HashSet<uint> { Phase0Landside.SecurityA, Phase0Landside.SecurityB };
            foreach (uint source in new[] { Phase0Landside.Kerb, Phase0Landside.RailBox })
            {
                Assert.True(new RouteOracle(g).Reaches(source, Phase0Landside.Gate), "source " + source);
                Assert.False(ReachesAvoiding(g, source, Phase0Landside.Gate, security), "source " + source + " bypasses security");
            }
        }

        [Fact]
        public void test_phase0_landside_has_alternative_security_queues_and_long_corridor()
        {
            WalkGraph g = Phase0Landside.Load();
            IWorldSystem world = WorldKit.Create(g);
            var gate = new NodeId(Phase0Landside.Gate);

            // Two security queues as alternative routes out of one node.
            IReadOnlyList<EdgeId> fork = world.OutEdges(new NodeId(Phase0Landside.LandsideCorridor));
            var reached = new HashSet<uint>();
            for (int i = 0; i < fork.Count; i++)
            {
                if (world.CanReachVia(fork[i], gate))
                {
                    reached.Add(world.EdgeTo(fork[i]).Value);
                }
            }

            Assert.Contains(Phase0Landside.SecurityA, reached);
            Assert.Contains(Phase0Landside.SecurityB, reached);

            // At least one corridor node with LengthMetres > 0.
            Assert.True(world.LengthMetres(new NodeId(Phase0Landside.LandsideCorridor)) > 0);
            Assert.True(world.LengthMetres(new NodeId(Phase0Landside.AirsideCorridor)) > 0);
        }

        private static bool ReachesAvoiding(in WalkGraph g, uint from, uint to, HashSet<uint> avoid)
        {
            var seen = new HashSet<uint> { from };
            var stack = new Stack<uint>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                uint n = stack.Pop();
                for (int i = 0; i < g.Edges.Count; i++)
                {
                    WalkEdgeDef e = g.Edges[i];
                    if (e.From.Value != n || avoid.Contains(e.To.Value))
                    {
                        continue;
                    }

                    if (e.To.Value == to)
                    {
                        return true;
                    }

                    if (seen.Add(e.To.Value))
                    {
                        stack.Push(e.To.Value);
                    }
                }
            }

            return false;
        }
    }
}
