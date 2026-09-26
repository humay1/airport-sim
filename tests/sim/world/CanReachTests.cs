using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>18 §18.3 CanReach over directed edges (18 §18.2).</summary>
    public sealed class CanReachTests
    {
        [Fact]
        public void test_can_reach_follows_edge_direction()
        {
            // 1 -> 2 -> 3, a two-way link 3 <-> 4 as two edges, and 5 isolated.
            WalkGraph g = new GraphBuilder()
                .Node(1, 1).Node(2, 1).Node(3, 1).Node(4, 1).Node(5, 1)
                .Edge(1, 1, 2).Edge(2, 2, 3).Edge(3, 3, 4).Edge(4, 4, 3)
                .Build();
            IWorldSystem world = WorldKit.Create(g);

            Assert.True(world.CanReach(new NodeId(1), new NodeId(2)));
            Assert.True(world.CanReach(new NodeId(1), new NodeId(4)));
            Assert.True(world.CanReach(new NodeId(4), new NodeId(3)));
            Assert.False(world.CanReach(new NodeId(3), new NodeId(1)));
            Assert.False(world.CanReach(new NodeId(2), new NodeId(1)));
            Assert.False(world.CanReach(new NodeId(1), new NodeId(5)));
            Assert.False(world.CanReach(new NodeId(5), new NodeId(1)));
        }

        [Fact]
        public void test_can_reach_fixture_sources_reach_the_gate_and_not_back()
        {
            IWorldSystem world = WorldKit.Create(Phase0Landside.Load());
            var gate = new NodeId(Phase0Landside.Gate);
            Assert.True(world.CanReach(new NodeId(Phase0Landside.Kerb), gate));
            Assert.True(world.CanReach(new NodeId(Phase0Landside.RailBox), gate));
            Assert.False(world.CanReach(gate, new NodeId(Phase0Landside.Kerb)));
            Assert.False(world.CanReach(new NodeId(Phase0Landside.Kerb), new NodeId(Phase0Landside.RailBox)));
        }
    }
}
