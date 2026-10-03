using System;
using System.Collections.Generic;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-045. HarnessGates.BudgetFromSamples, the pure budget statistic of
    /// 19-interfaces-harness.md §19.4 (Q-058), against 03 "Budget tests: window and
    /// arithmetic" (Q-045) with B = 6000 and n = TICKS_PER_SIM_DAY: each sample rounded
    /// up to whole µs and capped at C, the mean passes iff Σu ≤ B × n and is reported
    /// rounded up, p99 is the nearest rank. The six tests are §19.7's table and its
    /// argument test; every call checks that samples is left unchanged.
    /// </summary>
    public sealed class BudgetStatisticTests
    {
        private const long F = 10_000_000L;
        private const int N = (int)TicksPerDay;

        // 03 step 4: p99 = u[(99 × n + 99) / 100 − 1] = u[14 255] for n = 14 400, so
        // 14 256 zero samples keep the rank on a zero and 14 255 put it on a slow one.
        private const int SlowAt144 = 144;
        private const int SlowAt145 = 145;

        // -------------------------------------------------------------- §19.7 table

        [Fact]
        public void test_harness_gates_budget_from_samples_at_budget_passes()
        {
            // 60 000 ticks at 10^7 Hz is exactly 6000 µs: Σu = B × n passes (03 step 3).
            AssertReport(Filled(60_000L), F, true,
                "PASS budget ticks=14400 mean_us=6000 p99_us=6000");
        }

        [Fact]
        public void test_harness_gates_budget_from_samples_rounds_each_sample_up()
        {
            // 60 001 ticks is 6000.1 µs. Rounded up, u = 6001 and Σu > B × n; the old
            // flooring gave u = 6000 and a PASS, which §19.4 says it replaces.
            AssertReport(Filled(60_001L), F, false,
                "FAIL budget ticks=14400 mean_us=6001 p99_us=6001");
        }

        [Fact]
        public void test_harness_gates_budget_from_samples_p99_nearest_rank_passes_at_144_slow()
        {
            // u = 12 001 for each slow sample; Σu = 1 728 144 ≤ 86 400 000, the reported
            // mean is ⌈1 728 144 / 14 400⌉ = 121, and rank 14 255 is still a zero.
            foreach (long[] samples in SlowPlacements(SlowAt144))
            {
                AssertReport(samples, F, true,
                    "PASS budget ticks=14400 mean_us=121 p99_us=0");
            }
        }

        [Fact]
        public void test_harness_gates_budget_from_samples_p99_nearest_rank_fails_at_145_slow()
        {
            // Σu = 1 740 145, mean ⌈120.84…⌉ = 121 passes; rank 14 255 is now the lowest
            // slow sample, 12 001 > 2 × B, so p99 fails.
            foreach (long[] samples in SlowPlacements(SlowAt145))
            {
                AssertReport(samples, F, false,
                    "FAIL budget ticks=14400 mean_us=121 p99_us=12001");
            }
        }

        [Fact]
        public void test_harness_gates_budget_from_samples_caps_overflowing_sample()
        {
            // long.MaxValue trips 03 step 2's guard, so u = C = 6000 × 14 400 + 1 =
            // 86 400 001: Σu = C fails the mean, reported (C + n − 1) / n = 6001, and the
            // sum stays within long. Without the guard d × 10^6 would overflow.
            foreach (int at in new[] { 0, N / 2, N - 1 })
            {
                long[] samples = Filled(0L);
                samples[at] = long.MaxValue;
                AssertReport(samples, F, false,
                    "FAIL budget ticks=14400 mean_us=6001 p99_us=0");
            }
        }

        // ------------------------------------------------------------ §19.4 arguments

        [Fact]
        public void test_harness_gates_budget_from_samples_rejects_bad_arguments()
        {
            Assert.Throws<ArgumentNullException>(
                () => HarnessGates.BudgetFromSamples(null!, F));

            // 03's window is exactly TICKS_PER_SIM_DAY samples.
            AssertRejected(new long[N - 1], F);
            AssertRejected(new long[N + 1], F);
            AssertRejected(Array.Empty<long>(), F);

            // Any sample below 0, wherever it is.
            foreach (int at in new[] { 0, N / 2, N - 1 })
            {
                long[] negative = Filled(60_000L);
                negative[at] = -1L;
                AssertRejected(negative, F);
            }

            // frequency ≤ 0, or above long.MaxValue / (C + 1) (03 step 2's bound).
            const long c = 6000L * N + 1L;
            const long maxFrequency = long.MaxValue / (c + 1L);
            AssertRejected(Filled(0L), 0L);
            AssertRejected(Filled(0L), -1L);
            AssertRejected(Filled(0L), long.MinValue);
            AssertRejected(Filled(0L), maxFrequency + 1L);
            AssertRejected(Filled(0L), long.MaxValue);

            // The bound itself is inside the rule: it does not throw.
            GateResult atBound = HarnessGates.BudgetFromSamples(Filled(0L), maxFrequency);
            Assert.True(atBound.Passed);
            Assert.Equal("PASS budget ticks=14400 mean_us=0 p99_us=0", atBound.Report);
        }

        // ----------------------------------------------------------------- helpers

        private static long[] Filled(long value)
        {
            var samples = new long[N];
            Array.Fill(samples, value);
            return samples;
        }

        /// <summary>
        /// §19.7: the position of the slow samples varies and the result does not.
        /// Front, back, and spread through the window.
        /// </summary>
        private static IEnumerable<long[]> SlowPlacements(int slow)
        {
            const long slowTicks = 120_001L;

            long[] front = Filled(0L);
            for (int i = 0; i < slow; i++) front[i] = slowTicks;
            yield return front;

            long[] back = Filled(0L);
            for (int i = 0; i < slow; i++) back[N - 1 - i] = slowTicks;
            yield return back;

            long[] spread = Filled(0L);
            for (int i = 0; i < slow; i++) spread[37 + i * 97] = slowTicks;
            yield return spread;
        }

        private static void AssertReport(long[] samples, long frequency, bool passed, string report)
        {
            long[] before = (long[])samples.Clone();
            GateResult r = HarnessGates.BudgetFromSamples(samples, frequency);
            Assert.Equal(report, r.Report);
            Assert.Equal(passed, r.Passed);
            Assert.Equal(before, samples);
        }

        private static void AssertRejected(long[] samples, long frequency)
        {
            long[] before = (long[])samples.Clone();
            Assert.Throws<ArgumentOutOfRangeException>(
                () => HarnessGates.BudgetFromSamples(samples, frequency));
            Assert.Equal(before, samples);
        }
    }
}
