using System;
using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12 and 03 "How a budget is measured": B = 800 µs/tick at max
    /// tier (800 daily movements, 60 stands, 3 runways), timing sim.airside's
    /// Tick only. 03 "Budget tests: window and arithmetic" (Q-044, Q-045):
    /// one window of exactly n = TICKS_PER_SIM_DAY consecutive ticks after
    /// warm-up, each sample converted to whole microseconds rounding up and
    /// capped at C = B × n + 1, everything in long. Pass iff Σu ≤ B × n and
    /// the nearest-rank p99 ≤ 2 × B. The authoritative measurement stays
    /// tools/SimHarness budget.
    /// </summary>
    public sealed class AirsideBudgetTests
    {
        private const long BudgetMicros = 800L;
        private const int N = (int)AirConst.TicksPerDay;

        /// <summary>03 Q-045 step 2: whole microseconds, rounded up, capped at C.</summary>
        internal static long Micros(long d, long f, long cap)
        {
            if (d > (long.MaxValue - f + 1L) / 1000000L)
            {
                return cap;
            }

            long u = ((d * 1000000L) + f - 1L) / f;
            return u < cap ? u : cap;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_airside_budget_max_tier_day_mean_and_p99_within_budget()
        {
            AirsideLayout layout = MaxTierLayout.Layout();
            Assert.Equal(60, layout.Stands.Count);
            Assert.Equal(3, layout.Runways.Count);
            byte[] load = ScheduleFixture.MaxTier();
            Assert.Equal(800, Csv.Ids(load).Count);

            // In a real host, so day 1's flights reach the pending list through
            // the FlightPlanPublished handler; AirsideProbe times the Tick only.
            var flow = new RuleFlow();
            var rig = new HostRig(load, layout: layout, flow: flow, record: false, probe: true);

            // Day 0 warms every path and is not sampled. The window is the
            // next 14 400 consecutive ticks, one sample each.
            rig.RunTo(AirConst.TicksPerDay);
            long absorbsBefore = flow.AbsorbCalls;
            rig.Probe!.Timing = true;
            long f = Stopwatch.Frequency;
            long cap = (BudgetMicros * N) + 1L;
            var u = new long[N];
            long sum = 0L;
            for (int i = 0; i < N; i++)
            {
                rig.Host.Step(1);
                u[i] = Micros(rig.Probe.LastElapsed, f, cap);
                sum += u[i];
            }

            // About 400 departures close their doors in any one-day window.
            Assert.True(flow.AbsorbCalls - absorbsBefore > 100L, "the measured window did too little work");

            Array.Sort(u);
            long p99 = u[((99 * N) + 99) / 100 - 1];
            long reportedMean = (sum + N - 1) / N;
            string report = string.Format(
                CultureInfo.InvariantCulture,
                "mean {0} us (budget {1}), p99 {2} us (budget {3}), max {4} us, f={5}",
                reportedMean,
                BudgetMicros,
                p99,
                2L * BudgetMicros,
                u[N - 1],
                f);
            Assert.True(sum <= BudgetMicros * N, "mean over budget: " + report);
            Assert.True(p99 <= 2L * BudgetMicros, "p99 over budget: " + report);
        }

        [Fact]
        public void test_airside_budget_microsecond_conversion_rounds_up_and_caps()
        {
            // The arithmetic of 03 Q-045 step 2, on literals.
            const long f = 10000000L; // 10 MHz
            long cap = (BudgetMicros * N) + 1L;
            Assert.Equal(0L, Micros(0L, f, cap));
            Assert.Equal(1L, Micros(1L, f, cap));
            Assert.Equal(1L, Micros(10L, f, cap));
            Assert.Equal(2L, Micros(11L, f, cap));
            Assert.Equal(800L, Micros(8000L, f, cap));
            Assert.Equal(cap, Micros(long.MaxValue, f, cap));
            Assert.Equal(cap, Micros(cap * 10L, f, cap));
            Assert.Equal(cap - 1L, Micros((cap - 1L) * 10L, f, cap));

            // Nearest rank: ceil(0.99 × 14 400) − 1.
            Assert.Equal(14255, ((99 * N) + 99) / 100 - 1);
        }
    }
}
