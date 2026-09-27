using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 02-determinism.md for sim.flow: same inputs, same checkpoints; replaying
    /// the recorded command log reproduces the run (the save/load posture
    /// before sim.save exists, Q-027); promotion and queries change no hashed
    /// state (09 §9.1, §9.10); the hash does move with population and lanes.
    /// </summary>
    public sealed class FlowDeterminismTests
    {
        private const int Ticks = 3 * (int)SimConstants.TICKS_PER_SIM_HOUR;

        private static string Checkpoints(Rig rig)
        {
            var lines = new List<string>();
            foreach (Checkpoint cp in rig.Checkpoints.Checkpoints)
            {
                lines.Add(cp.Tick + ":" + cp.WorldHash.ToString("X16") + ":" + string.Join(",", cp.SystemHashes));
            }

            return string.Join("\n", lines);
        }

        private static (string Checkpoints, string Events, Rig Rig) Run(ulong inputSeed, bool promote = false, IReadOnlyList<Command>? replay = null, ulong masterSeed = 1)
        {
            var rig = Rig.Fixture(DayScenario.Content(), masterSeed);
            new DayScenario().Run(rig, inputSeed, Ticks, promote, replay);
            return (Checkpoints(rig), string.Join("\n", rig.Events!.Log), rig);
        }

        [Fact]
        public void test_flow_determinism_same_inputs_same_checkpoints_and_events()
        {
            var a = Run(0xDA7_0001UL);
            var b = Run(0xDA7_0001UL);
            Assert.Equal(3, a.Rig.Checkpoints.Checkpoints.Count);
            Assert.Equal(a.Checkpoints, b.Checkpoints);
            Assert.Equal(a.Events, b.Events);
            Assert.Equal(a.Rig.Flow.ComputeStateHash(), b.Rig.Flow.ComputeStateHash());
            Assert.NotEmpty(a.Rig.Events!.Arrived);
        }

        [Fact]
        public void test_flow_determinism_master_seed_does_not_move_flow()
        {
            // sim.flow draws nothing from its hashed streams at Phase 0/1 in this
            // scenario, so its hash must not depend on the master seed.
            var a = Run(0xDA7_0002UL, masterSeed: 1);
            var b = Run(0xDA7_0002UL, masterSeed: 0xABCDEF);
            Assert.Equal(a.Events, b.Events);
            Assert.Equal(a.Rig.Flow.ComputeStateHash(), b.Rig.Flow.ComputeStateHash());
        }

        [Fact]
        public void test_flow_determinism_replayed_command_log_reproduces_the_run()
        {
            var original = Run(0xDA7_0003UL);
            IReadOnlyList<Command> log = original.Rig.Host.CommandLogSince(0);
            Assert.NotEmpty(log);
            var replayed = Run(0xDA7_0003UL, replay: log);
            Assert.Equal(original.Checkpoints, replayed.Checkpoints);
            Assert.Equal(original.Events, replayed.Events);
        }

        [Fact]
        public void test_flow_determinism_promotion_is_outcome_neutral()
        {
            // 09 §9.1: promotion adds no state any cohort update reads.
            var plain = Run(0xDA7_0004UL);
            var promoted = Run(0xDA7_0004UL, promote: true);
            Assert.Equal(plain.Checkpoints, promoted.Checkpoints);
            Assert.Equal(plain.Events, promoted.Events);
        }

        [Fact]
        public void test_flow_determinism_queries_do_not_change_the_hash()
        {
            var rig = Rig.Fixture(DayScenario.Content());
            new DayScenario().Run(rig, 0xDA7_0005UL, 900);
            ulong before = rig.Flow.ComputeStateHash();
            for (uint n = 1; n <= 9; n++)
            {
                var node = new NodeId(n);
                rig.Flow.Population(node);
                rig.Flow.PredictedWaitMinutes(node);
                rig.Flow.CohortsAt(node);
                rig.Flow.TryGetLaneState(node, out _);
                rig.Flow.AgentsAt(node);
            }

            for (ulong f = 1; f <= 3; f++)
            {
                rig.Flow.PopulationForFlight(new FlightId(f), FlowDirection.Departing);
                rig.Flow.TryGetOutstanding(new FlightId(f), out _);
            }

            Assert.Equal(before, rig.Flow.ComputeStateHash());
        }

        [Fact]
        public void test_flow_determinism_hash_moves_with_population_and_lanes()
        {
            IContentIndex content = DayScenario.Content();
            var a = Rig.Fixture(content);
            var b = Rig.Fixture(content);
            var c = Rig.Fixture(content);
            Assert.Equal(a.Flow.ComputeStateHash(), b.Flow.ComputeStateHash());
            a.InjectNow(Landside.Kerb, 5, 1);
            b.InjectNow(Landside.Kerb, 6, 1);
            c.InjectNow(Landside.Kerb, 5, 1);
            Assert.True(c.Submit(Landside.SecurityA, 2, out _));
            a.Step(3);
            b.Step(3);
            c.Step(3);
            Assert.NotEqual(a.Flow.ComputeStateHash(), b.Flow.ComputeStateHash());
            Assert.NotEqual(a.Flow.ComputeStateHash(), c.Flow.ComputeStateHash());
        }
    }
}
