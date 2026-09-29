using System;
using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9 and 03 "How a budget is measured" (Q-031): over the ticks of
    /// one full sim-day at max tier, mean ≤ 0.10 ms and p99 ≤ 0.20 ms, timing
    /// sim.schedule's Tick only. Measured per 07 L11 with Stopwatch timestamps
    /// in long arithmetic. The authoritative measurement stays
    /// tools/SimHarness budget.
    /// </summary>
    public sealed class BudgetTests
    {
        // 0.10 ms = Frequency / 10000 timestamp units.
        private const long BudgetDivisor = 10000L;

        private static void AssertWithinBudget(bool withFlow)
        {
            byte[] load = Fixture.MaxTier();
            Assert.Equal(800, Load.Table(load, "max-tier.csv").Rows.Count);

            var warm = new DirectRig(load, flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            warm.RunTo(SchedConst.TicksPerDay);

            var rig = new DirectRig(load, flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            int n = (int)SchedConst.TicksPerDay;
            var samples = new long[n];
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                long start = Stopwatch.GetTimestamp();
                rig.TickOnce();
                samples[i] = Stopwatch.GetTimestamp() - start;
                total += samples[i];
            }

            // The measured day must have done the work: days 0 and 1 published, demand injected.
            Assert.Equal(1600, ((CountingPublisher)rig.Publisher).Plans);
            Assert.True(!withFlow || rig.Flow!.InjectCalls > 0);

            Array.Sort(samples);
            long p99 = samples[(((n * 99) + 99) / 100) - 1]; // nearest rank, ceil(0.99 n)
            long freq = Stopwatch.Frequency;
            string report = string.Format(
                CultureInfo.InvariantCulture,
                "mean {0} ns (budget 100000), p99 {1} ns (budget 200000), max {2} ns",
                total * 1000000000L / freq / n,
                p99 * 1000000000L / freq,
                samples[n - 1] * 1000000000L / freq);
            Assert.True(total * BudgetDivisor <= freq * n, "mean over budget: " + report);
            Assert.True(p99 * BudgetDivisor <= freq * 2L, "p99 over budget: " + report);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_one_day_mean_and_p99_within_budget_with_flow()
        {
            AssertWithinBudget(withFlow: true);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_one_day_mean_and_p99_within_budget_without_flow()
        {
            AssertWithinBudget(withFlow: false);
        }
    }
}
