using System;
using System.Diagnostics;
using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9 and 03 "How a budget is measured" (Q-031): at max tier, mean ≤
    /// 0.10 ms and p99 ≤ 0.20 ms, timing sim.schedule's Tick only (it registers
    /// no handlers, so Q-064 adds nothing). Window and arithmetic per 03
    /// "Budget tests: window and arithmetic" (Q-044, Q-045): exactly 14 400
    /// consecutive ticks, one sample per tick, long only per 07 L11. The
    /// authoritative measurement stays tools/SimHarness budget.
    /// </summary>
    public sealed class BudgetTests
    {
        // B, the budget in whole microseconds.
        private const long BudgetMicros = 100L;

        private readonly ITestOutputHelper output;

        public BudgetTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private void AssertWithinBudget(bool withFlow)
        {
            byte[] load = Fixture.MaxTier();
            Assert.Equal(800, Load.Table(load, "max-tier.csv").Rows.Count);

            // Warm-up ticks are not sampled.
            var warm = new DirectRig(load, flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            warm.RunTo(SchedConst.TicksPerDay);

            var rig = new DirectRig(load, flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            long n = (long)SchedConst.TicksPerDay;
            Assert.Equal(14400L, n);
            var d = new long[n];
            for (long i = 0; i < n; i++)
            {
                long start = Stopwatch.GetTimestamp();
                rig.TickOnce();
                d[i] = Stopwatch.GetTimestamp() - start;
            }

            // The measured window must have done the work: days 0 and 1 published, demand injected.
            Assert.Equal(1600, ((CountingPublisher)rig.Publisher).Plans);
            Assert.True(!withFlow || rig.Flow!.InjectCalls > 0);

            // 03 Q-045 step 2: whole microseconds rounded up, capped at C.
            long f = Stopwatch.Frequency;
            long cap = (BudgetMicros * n) + 1;
            long guard = (long.MaxValue - f + 1) / 1_000_000;
            var u = new long[n];
            long sum = 0;
            for (long i = 0; i < n; i++)
            {
                u[i] = d[i] > guard ? cap : Math.Min(((d[i] * 1_000_000) + f - 1) / f, cap);
                sum += u[i];
            }

            // Step 4: nearest rank.
            Array.Sort(u);
            long p99 = u[(((99 * n) + 99) / 100) - 1];

            // Step 5: reported on every run, pass or fail.
            long reportedMean = (sum + n - 1) / n;
            string report = string.Format(
                CultureInfo.InvariantCulture,
                "sim.schedule {0}: n {1}, mean {2} us (budget {3}), p99 {4} us (budget {5}), max {6} us, f {7}",
                withFlow ? "with flow" : "without flow",
                n,
                reportedMean,
                BudgetMicros,
                p99,
                2 * BudgetMicros,
                u[n - 1],
                f);
            output.WriteLine(report);

            Assert.True(sum <= BudgetMicros * n, "mean over budget: " + report);
            Assert.True(p99 <= 2 * BudgetMicros, "p99 over budget: " + report);
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
