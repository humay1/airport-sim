using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>18 §18.3 CanReachVia: reachability through a given first edge.</summary>
    public sealed class CanReachViaTests
    {
        [Fact]
        public void test_can_reach_via_distinguishes_first_edges_of_one_node()
        {
            // From 1, e1 leads toward 3 and e2 toward a dead end 4.
            WalkGraph g = new GraphBuilder()
                .Node(1, 1).Node(2, 1).Node(3, 1).Node(4, 1)
                .Edge(1, 1, 2).Edge(3, 2, 3).Edge(2, 1, 4)
                .Build();
            IWorldSystem world = WorldKit.Create(g);

            Assert.True(world.CanReach(new NodeId(1), new NodeId(3)));
            Assert.True(world.CanReachVia(new EdgeId(1), new NodeId(3)));
            Assert.False(world.CanReachVia(new EdgeId(2), new NodeId(3)));
            Assert.True(world.CanReachVia(new EdgeId(2), new NodeId(4)));
            Assert.False(world.CanReachVia(new EdgeId(1), new NodeId(4)));
        }

        [Fact]
        public void test_can_reach_via_fixture_both_security_queues_are_alternatives()
        {
            // 18 §18.6: two security queues are alternative routes, so 09 §9.6's
            // choice is exercised. Each first edge out of the landside corridor
            // reaches the gate through its own queue.
            IWorldSystem world = WorldKit.Create(Phase0Landside.Load());
            var gate = new NodeId(Phase0Landside.Gate);
            var viaA = new EdgeId(Phase0Landside.CorridorToSecurityA);
            var viaB = new EdgeId(Phase0Landside.CorridorToSecurityB);

            Assert.True(world.CanReachVia(viaA, gate));
            Assert.True(world.CanReachVia(viaB, gate));
            Assert.Equal(
                new uint[] { Phase0Landside.SecurityA, Phase0Landside.AirsideCorridor, Phase0Landside.Gate },
                WorldKit.Ids(world.PathVia(viaA, gate)));
            Assert.Equal(
                new uint[] { Phase0Landside.SecurityB, Phase0Landside.AirsideCorridor, Phase0Landside.Gate },
                WorldKit.Ids(world.PathVia(viaB, gate)));

            // Upstream of the fork both queues cost the same; the tie goes to the
            // smaller edge sequence, [e3, e4, ...], through SecurityA.
            Assert.Equal(
                new uint[] { Phase0Landside.LandsideCorridor, Phase0Landside.SecurityA, Phase0Landside.AirsideCorridor, Phase0Landside.Gate },
                WorldKit.Ids(world.PathVia(new EdgeId(Phase0Landside.HallToCorridor), gate)));
        }
    }
}
