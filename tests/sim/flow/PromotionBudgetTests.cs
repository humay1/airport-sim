using System;
using System.Diagnostics;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-010: with promotion on, sim.flow stays within 2.5 ms/tick
    /// (09 §9.10), measured by 03 "How a budget is measured": over the ticks
    /// of one full sim-day, mean ≤ 2.5 ms and p99 ≤ 5.0 ms, timing sim.flow's
    /// Tick only. The time is taken between the probe at position 3 and the
    /// probe at position 5 (07 L11: Stopwatch timestamps, long arithmetic).
    /// Load: about 90 000 passengers a day (03's max tier) through the wide
    /// security graph, with every node promoted and all agents read every tick.
    /// </summary>
    public sealed class PromotionBudgetTests
    {
        // 2.5 ms = Frequency * 25 / 10000 timestamp units.
        [Fact]
        [Trait("Category", "Budget")]
        public void test_promotion_budget_one_day_with_every_node_promoted()
        {
            var plan = new PromoPlan(0x0010_B0D6UL, 1500, 6, 20);
            Assert.InRange(plan.DailyPax, 80000L, 100000L);

            var warm = new PromoRig(plan, PromoGraphs.Wide, record: false, recordCheckpoints: false);
            warm.SetAll(true);
            warm.Host.Step((uint)PromoConst.TicksPerDay);

            var rig = new PromoRig(plan, PromoGraphs.Wide, record: false, recordCheckpoints: false);
            rig.SetAll(true);
            rig.Host.Step((uint)PromoConst.TicksPerDay);

            int n = (int)PromoConst.TicksPerDay;
            var samples = new long[n];
            rig.Absorber.Timing = true;
            rig.AfterFlow.TimingFrom = rig.Absorber;
            rig.AfterFlow.Samples = samples;
            long views = 0;
            for (int i = 0; i < n; i++)
            {
                rig.Host.Step(1);
                foreach (uint node in PromoConst.AllNodes)
                {
                    views += rig.Flow.AgentsAt(new NodeId(node)).Count;
                }
            }

            Assert.True(views > 0, "the promoted day must show agents");
            Assert.True(rig.Absorber.Absorbed > 0, "the day must board passengers");
            long total = 0;
            foreach (long s in samples)
            {
                total += s;
            }

            Array.Sort(samples);
            long p99 = samples[(((n * 99) + 99) / 100) - 1];
            long freq = Stopwatch.Frequency;
            string report = string.Format(
                CultureInfo.InvariantCulture,
                "mean {0} us (budget 2500), p99 {1} us (budget 5000), max {2} us",
                total * 1000000L / freq / n,
                p99 * 1000000L / freq,
                samples[n - 1] * 1000000L / freq);
            Assert.True(total * 10000L <= freq * 25L * n, "mean over budget: " + report);
            Assert.True(p99 * 10000L <= freq * 50L, "p99 over budget: " + report);
        }
    }
}
