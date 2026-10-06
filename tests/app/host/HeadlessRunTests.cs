using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. IHeadlessRun (16 §16.8): it composes the bundle, submits no
    /// command (Q-071), calls Step(TICKS_PER_SIM_DAY) exactly Days times, and
    /// writes the dump. It has no seam: the tests compose the same bundle with
    /// ISimComposer.Compose themselves, step it, render the dump with their own
    /// code and compare it with Run's file.
    /// </summary>
    public sealed class HeadlessRunTests
    {
        [Fact]
        public void test_headless_run_result_independent_of_step_batch_size()
        {
            using var tmp = new TempDir();
            byte[] run = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp);

            uint day = B.Day;
            var batchings = new List<(string Label, Action<ISimHost> Step)>
            {
                ("14 400 × Step(1)", h => { for (int i = 0; i < 14400; i++) { h.Step(1); } }),
                ("14 × Step(997), then Step(442)", h => { for (int i = 0; i < 14; i++) { h.Step(997); } h.Step(442); }),
                ("one Step(14 400)", h => h.Step(day)),
            };

            Assert.Equal(14400u, day);
            foreach ((string label, Action<ISimHost> step) in batchings)
            {
                var sink = new RecordingSink();
                ComposedSim sim = ComposeTests.Compose(B.Phase1Bundle(), B.Phase1Content, sink);
                step(sim.Host);
                Assert.Equal((ulong)day, sim.Host.CurrentTick);
                Dumps.AssertBytesEqual(Dumps.Render(B.Seed, ComposeTests.Names(sim), sink), run, "Run's file against " + label);
            }
        }

        [Fact]
        public void test_headless_run_steps_days_times_ticks_per_sim_day()
        {
            // Run steps to Days × TICKS_PER_SIM_DAY, so two days record 48
            // checkpoints, and they equal the reference composition's two days.
            using var tmp = new TempDir();
            byte[] dump = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 2, tmp);

            KitRun kit = Kit.Compose(B.Phase1Bundle(), B.Phase1Content, B.Seed, B.RegistryOrder);
            kit.Host.Step(B.Day);
            kit.Host.Step(B.Day);
            Assert.Equal(2UL * SimConstants.TICKS_PER_SIM_DAY, kit.Host.CurrentTick);
            Dumps.AssertBytesEqual(kit.Dump(B.Seed), dump, "Run's two-day dump");
            CheckpointDumpTests.AssertShape(dump, "systems sim.world sim.schedule sim.airside sim.flow sim.turnaround sim.delay", 6, 48);
        }

        [Fact]
        public void test_headless_run_same_bundle_gives_identical_files()
        {
            // 16 §16.4: a session's checkpoints are a pure function of the bundle
            // and the content, so two runs of one bundle give identical files.
            using var tmp = new TempDir();
            byte[] a = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp, "a");
            byte[] b = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp, "b");
            Dumps.AssertBytesEqual(a, b, "the second run");
        }
    }
}
