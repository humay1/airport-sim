using System;
using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12 and 03 "How a budget is measured": 0.80 ms/tick at max tier
    /// (800 daily movements, 60 stands, 3 runways) over one full sim-day,
    /// passing iff mean ≤ 0.80 ms and p99 ≤ 1.60 ms, timing sim.airside's
    /// Tick only. Measured per 07 L11 with Stopwatch timestamps in long
    /// arithmetic. The authoritative measurement stays tools/SimHarness budget.
    /// </summary>
    public sealed class AirsideBudgetTests
    {
        // 0.80 ms = Frequency / 1250 timestamp units.
        private const long BudgetDivisor = 1250L;

        [Fact]
        [Trait("Category", "Budget")]
        public void test_airside_budget_max_tier_day_mean_and_p99_within_budget()
        {
            AirsideLayout layout = MaxTierLayout.Layout();
            Assert.Equal(60, layout.Stands.Count);
            Assert.Equal(3, layout.Runways.Count);
            byte[] load = ScheduleFixture.MaxTier();
            Assert.Equal(800, Csv.Day0Ids(load).Count);

            var flow = new RuleFlow();
            var rig = new DirectRig(load, layout, flow);

            // Day 0 warms every path; day 1, a full day of repeat_daily load, is measured.
            rig.RunTo(AirConst.TicksPerDay);
            long milestonesBefore = rig.Publisher.Milestones;
            int n = (int)AirConst.TicksPerDay;
            var samples = new long[n];
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                samples[i] = rig.TickTimed();
                total += samples[i];
            }

            // 400 daily arrivals give at least one InboundAirborne each in any one-day window.
            Assert.True(rig.Publisher.Milestones - milestonesBefore > 400L, "the measured day did too little work");

            Array.Sort(samples);
            long p99 = samples[(((n * 99) + 99) / 100) - 1]; // nearest rank, ceil(0.99 n)
            long freq = Stopwatch.Frequency;
            string report = string.Format(
                CultureInfo.InvariantCulture,
                "mean {0} ns (budget 800000), p99 {1} ns (budget 1600000), max {2} ns",
                total * 1000000000L / freq / n,
                p99 * 1000000000L / freq,
                samples[n - 1] * 1000000000L / freq);
            Assert.True(total * BudgetDivisor <= freq * n, "mean over budget: " + report);
            Assert.True(p99 * BudgetDivisor <= freq * 2L, "p99 over budget: " + report);
        }
    }
}
