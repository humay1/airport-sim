using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 18 §18.4: no runtime state, Tick does nothing, no RNG, and
    /// ComputeStateHash() feeds WalkGraph.FixtureHash only (the posture of
    /// 11 §11.9), so an edited graph fails the determinism gate loudly.
    /// </summary>
    public sealed class WorldHashTests
    {
        [Fact]
        public void test_world_hash_is_fixture_hash_and_tick_consumes_no_rng()
        {
            byte[] bytes = Phase0Landside.ReadFixture();
            WalkGraph g = WorldFactory.CreateGraphLoader().Load(bytes, Phase0Landside.SourceName);
            Assert.Equal(WorldKit.Fnv1a64(bytes), g.FixtureHash);

            IWorldSystem world = WorldKit.Create(g);
            ulong expected = WorldKit.ExpectedHash(g.FixtureHash);
            Assert.Equal(expected, world.ComputeStateHash());

            string before = WorldSystemTests.Snapshot(world);
            var rng = new CountingRandom();
            var events = new CountingPublisher();
            for (ulong t = 0; t < 2 * SimConstants.TICKS_PER_SIM_HOUR; t++)
            {
                world.Tick(WorldKit.Context(t, rng, events));
            }

            Assert.Equal(0, rng.Touches);
            Assert.Equal(0, events.Published);
            Assert.Equal(expected, world.ComputeStateHash());
            Assert.Equal(before, WorldSystemTests.Snapshot(world));
        }

        [Fact]
        public void test_world_hash_feeds_fixture_hash_only()
        {
            // Two different graphs carrying the same FixtureHash hash the same:
            // the route tables are derived data and are not fed (08 §8.9).
            WalkGraph one = new GraphBuilder().Node(1, 5).Node(2, 6).Edge(1, 1, 2).Build(0xAB);
            WalkGraph other = new GraphBuilder().Node(3, 9).Node(4, 1).Node(5, 2).Edge(9, 5, 3).Edge(4, 3, 4).Build(0xAB);
            Assert.Equal(WorldKit.ExpectedHash(0xAB), WorldKit.Create(one).ComputeStateHash());
            Assert.Equal(WorldKit.ExpectedHash(0xAB), WorldKit.Create(other).ComputeStateHash());

            // The same graph under another FixtureHash hashes differently.
            WalkGraph relabelled = new GraphBuilder().Node(1, 5).Node(2, 6).Edge(1, 1, 2).Build(0xAC);
            Assert.Equal(WorldKit.ExpectedHash(0xAC), WorldKit.Create(relabelled).ComputeStateHash());
            Assert.NotEqual(WorldKit.Create(one).ComputeStateHash(), WorldKit.Create(relabelled).ComputeStateHash());
        }

        [Fact]
        public void test_world_hash_changes_when_fixture_edited()
        {
            byte[] original = Phase0Landside.ReadFixture();
            string edited = System.Text.Encoding.UTF8.GetString(original)
                .Replace("\"length_metres\": 120", "\"length_metres\": 121");
            Assert.NotEqual(System.Text.Encoding.UTF8.GetString(original), edited);

            IWorldSystem a = WorldKit.Create(WorldFactory.CreateGraphLoader().Load(original, Phase0Landside.SourceName));
            IWorldSystem b = WorldKit.Create(WorldKit.Load(edited, Phase0Landside.SourceName));
            Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
            Assert.Equal(WorldKit.ExpectedHash(WorldKit.Fnv1a64(WorldKit.Utf8(edited))), b.ComputeStateHash());
        }

        [Fact]
        public void test_world_hash_in_checkpoints_is_constant_and_seed_independent()
        {
            // Registered at position 1, the world is SystemHashes[0] of every
            // checkpoint (08 §8.9). With no state and no RNG it never changes and
            // does not depend on the master seed.
            WalkGraph g = Phase0Landside.Load();
            ulong expected = WorldKit.ExpectedHash(g.FixtureHash);
            foreach (ulong seed in new ulong[] { 1, 2, 0xDEADBEEF })
            {
                var sink = new RecordingSink();
                ISimHostBuilder b = WorldKit.Builder(seed, sink);
                b.Register(WorldFactory.CreateSystem(b.Services, g));
                ISimHost host = b.Build();
                host.Step((uint)(3 * SimConstants.TICKS_PER_SIM_HOUR));
                Assert.Equal(3, sink.Checkpoints.Count);
                foreach (Checkpoint cp in sink.Checkpoints)
                {
                    Assert.Single(cp.SystemHashes);
                    Assert.Equal(expected, cp.SystemHashes[0]);
                }
            }
        }
    }
}
