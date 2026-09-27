using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// Same input, same result (02-determinism.md), and the save/load posture
    /// of a module with no runtime state (18 §18.4): rebuilding from the same
    /// bytes is the round trip, and it must be indistinguishable.
    /// </summary>
    public sealed class WorldDeterminismTests
    {
        private static List<Checkpoint> RunDay(ulong seed, in WalkGraph g)
        {
            var sink = new RecordingSink();
            ISimHostBuilder b = WorldKit.Builder(seed, sink);
            b.Register(WorldFactory.CreateSystem(b.Services, g));
            b.Build().Step((uint)SimConstants.TICKS_PER_SIM_DAY);
            return sink.Checkpoints;
        }

        [Fact]
        public void test_world_determinism_same_bytes_same_graph_and_routes()
        {
            byte[] bytes = Phase0Landside.ReadFixture();
            WalkGraph a = WorldFactory.CreateGraphLoader().Load(bytes, Phase0Landside.SourceName);
            WalkGraph b = WorldFactory.CreateGraphLoader().Load(bytes, "another-name.json");
            Assert.Equal(WorldKit.Content(a), WorldKit.Content(b));
            Assert.Equal(a.FixtureHash, b.FixtureHash);
            Assert.Equal(WorldSystemTests.Snapshot(WorldKit.Create(a)), WorldSystemTests.Snapshot(WorldKit.Create(b)));
        }

        [Fact]
        public void test_world_determinism_same_seed_same_checkpoints()
        {
            WalkGraph g = Phase0Landside.Load();
            List<Checkpoint> first = RunDay(77, g);
            List<Checkpoint> second = RunDay(77, Phase0Landside.Load());
            Assert.Equal(24, first.Count);
            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Tick, second[i].Tick);
                Assert.Equal(first[i].WorldHash, second[i].WorldHash);
                Assert.Equal(first[i].CoreHash, second[i].CoreHash);
                Assert.Equal(first[i].SystemHashes, second[i].SystemHashes);
            }
        }

        [Fact]
        public void test_world_determinism_rebuild_mid_run_matches_running_system()
        {
            // The world has no runtime state, so a system rebuilt from the same
            // bytes after a partial day is its save/load round trip.
            byte[] bytes = Phase0Landside.ReadFixture();
            ISimHostBuilder b = WorldKit.Builder();
            IWorldSystem running = WorldFactory.CreateSystem(b.Services, WorldFactory.CreateGraphLoader().Load(bytes, Phase0Landside.SourceName));
            b.Register(running);
            ISimHost host = b.Build();
            host.Step(5000);

            IWorldSystem rebuilt = WorldKit.Create(WorldFactory.CreateGraphLoader().Load(bytes, Phase0Landside.SourceName));
            Assert.Equal(running.ComputeStateHash(), rebuilt.ComputeStateHash());
            Assert.Equal(WorldSystemTests.Snapshot(running), WorldSystemTests.Snapshot(rebuilt));
        }
    }
}
