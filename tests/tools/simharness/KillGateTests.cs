using System.Diagnostics;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.KillGateKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-009, the Phase 0 kill gate (00-overview.md, tasks/T-009-100-day-run.md):
    /// 100 sim-days of sim.world, sim.schedule and sim.flow over the Phase 0
    /// fixtures, in under 60 s and identical across runs. Both step more than
    /// 144 000 ticks, so both are Slow by 07 L11a rule (a).
    /// </summary>
    public sealed class KillGateTests
    {
        /// <summary>
        /// (a) One 100-day run, stepped by the harness (19 §19.1 FinalHash), in
        /// under 60 s of wall clock. Measured per 07 L11: Stopwatch.GetTimestamp
        /// and Stopwatch.Frequency in long arithmetic. The whole run is timed,
        /// composition included, because the gate is the run's wall clock.
        /// </summary>
        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_kill_gate_hundred_days_one_run_completes_under_sixty_seconds()
        {
            IContentIndex content = Content();
            var composer = new Phase0Composer();

            long start = Stopwatch.GetTimestamp();
            string final = HarnessGates.FinalHash(content, composer.Compose, Seed, Ticks);
            long elapsed = Stopwatch.GetTimestamp() - start;

            long elapsedMs = elapsed * 1000L / Stopwatch.Frequency;
            Assert.True(elapsed < 60L * Stopwatch.Frequency,
                "100 sim-days took " + elapsedMs + " ms, over the 60 000 ms kill gate");

            Assert.Equal(1, composer.Calls);
            Assert.Matches("^[0-9a-f]{16}$", final);
            AssertRunCarriedLoad(composer);
        }

        /// <summary>
        /// (b) determinism_same_process at Phase 0 scale (02 "Gates"): two
        /// independent 100-day runs from the same seed, compared checkpoint by
        /// checkpoint and then by final hash (19 §19.2). 1 440 000 ticks give
        /// checkpoints at 0, 600, ..., 1 439 400: 2 400 of them.
        /// </summary>
        [Fact]
        [Trait("Category", "Slow")]
        public void test_kill_gate_hundred_days_same_seed_runs_identical_at_every_checkpoint()
        {
            IContentIndex content = Content();
            var composer = new Phase0Composer();

            GateResult result = HarnessGates.SameProcess(content, composer.Compose, Seed, Ticks);

            Assert.True(result.Passed, result.Report);
            Assert.Matches(new Regex("^PASS determinism_same_process ticks=1440000 checkpoints=2400 final=[0-9a-f]{16}$"), result.Report);
            Assert.Equal(2, composer.Calls);
            AssertRunCarriedLoad(composer);
        }
    }
}
