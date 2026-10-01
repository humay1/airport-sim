using System.Diagnostics;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.KillGateKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-009, the Phase 0 kill gate (00-overview.md, 19 §19.6): 100 sim-days of the
    /// §19.2a composition, through the kit, in under 60 s and identical across
    /// runs. Both step more than 144 000 ticks, so both are Slow by 07 L11a
    /// rule (a).
    /// </summary>
    public sealed class KillGateTests
    {
        /// <summary>
        /// §19.6 (a): a whole-run wall-clock gate. One run of 1 440 000 ticks, seed
        /// 12345, through HarnessGates.FinalHash with the kit, takes under 60 s in
        /// total, composition included. There is one measurement, the
        /// Stopwatch.GetTimestamp() difference around the whole call, compared in
        /// long arithmetic (07 L11). 03's per-tick statistic does not apply to it
        /// (03 "Budget tests: window and arithmetic"), and it takes no per-tick
        /// samples.
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

            Assert.True(elapsed < 60L * Stopwatch.Frequency,
                "100 sim-days took " + (elapsed / Stopwatch.Frequency) + " s (" + elapsed + " timestamp units at "
                + Stopwatch.Frequency + " Hz), not under the 60 s kill gate");

            Assert.Equal(1, composer.Calls);
            Assert.Matches("^[0-9a-f]{16}$", final);
            AssertRunCarriedLoad(composer);
        }

        /// <summary>
        /// §19.6 (b): determinism_same_process at Phase 0 scale (02 "Gates"). Two
        /// independent 100-day runs from the same seed, compared checkpoint by
        /// checkpoint and then by final hash (19 §19.2). 1 440 000 ticks give
        /// checkpoints at 0, 600, ..., 1 439 400, which is 2 400 of them. The load
        /// check is on the second run. The gate has already shown the two equal.
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
