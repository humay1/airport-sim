using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 07 "Testing": one full simulated day, headless, against the fixture,
    /// asserting invariants. sim.world consumes no schedule (it emits and
    /// consumes no events, T-012), so the day is driven by a downstream probe
    /// at sim.flow's registry position (4) that queries the world every tick,
    /// as sim.flow will (09 §9.6).
    /// </summary>
    public sealed class WorldHeadlessDayTests
    {
        [Fact]
        public void test_world_headless_day_answers_are_stable_and_hash_is_fixture_hash()
        {
            WalkGraph g = Phase0Landside.Load();
            var sink = new RecordingSink();
            ISimHostBuilder b = WorldKit.Builder(2026, sink);
            IWorldSystem world = WorldFactory.CreateSystem(b.Services, g);
            string atStart = WorldSystemTests.Snapshot(world);

            var gate = new NodeId(Phase0Landside.Gate);
            var sources = new[] { new NodeId(Phase0Landside.Kerb), new NodeId(Phase0Landside.RailBox) };
            IReadOnlyList<EdgeId> fork = world.OutEdges(new NodeId(Phase0Landside.LandsideCorridor));
            int ticks = 0;
            int wrong = 0;
            var probe = new ProbeSystem(4);
            probe.OnTick = (in TickContext ctx) =>
            {
                ticks++;
                for (int s = 0; s < sources.Length; s++)
                {
                    if (!world.CanReach(sources[s], gate))
                    {
                        wrong++;
                    }
                }

                // Alternate between the two security queues, as a router would.
                EdgeId first = fork[(int)(ctx.Tick % (ulong)fork.Count)];
                IReadOnlyList<NodeId> path = world.PathVia(first, gate);
                if (path.Count != 3 || path[path.Count - 1] != gate)
                {
                    wrong++;
                }

                for (int i = 0; i < path.Count; i++)
                {
                    probe.Mix(path[i].Value);
                }
            };

            b.Register(world);
            b.Register(probe);
            ISimHost host = b.Build();
            host.Step((uint)SimConstants.TICKS_PER_SIM_DAY);

            Assert.Equal(SimConstants.TICKS_PER_SIM_DAY, host.CurrentTick);
            Assert.Equal((int)SimConstants.TICKS_PER_SIM_DAY, ticks);
            Assert.Equal(0, wrong);
            Assert.Equal(24, sink.Checkpoints.Count);
            ulong expected = WorldKit.ExpectedHash(g.FixtureHash);
            foreach (Checkpoint cp in sink.Checkpoints)
            {
                Assert.Equal(2, cp.SystemHashes.Length);
                Assert.Equal(expected, cp.SystemHashes[0]);
            }

            Assert.Equal(atStart, WorldSystemTests.Snapshot(world));
        }
    }
}
