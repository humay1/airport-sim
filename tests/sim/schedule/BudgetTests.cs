using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9, 03 "Performance": 0.10 ms per tick at max tier, measured per
    /// 07 L11 with Stopwatch timestamps in long arithmetic, as a mean over
    /// whole fixture days like the sim.core budget tests. The Phase 0 fixture
    /// is the max-tier load at Phase 0. The authoritative measurement stays
    /// tools/SimHarness budget.
    /// </summary>
    public sealed class BudgetTests
    {
        // 0.10 ms = Frequency / 10000 timestamp units.
        private const long BudgetDivisor = 10000L;

        private static void AssertWithinBudget(bool withFlow)
        {
            var warm = new DirectRig(Fixture.Bytes(), flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            warm.RunTo(2UL * SchedConst.TicksPerDay);

            var rig = new DirectRig(Fixture.Bytes(), flow: withFlow ? new RecordingFlow(recording: false) : null, counting: true);
            ulong ticks = 2UL * SchedConst.TicksPerDay;
            long start = Stopwatch.GetTimestamp();
            rig.RunTo(ticks);
            long elapsed = Stopwatch.GetTimestamp() - start;

            // The measured run must have done the work: days 0-2 published, demand injected.
            Assert.Equal(600, ((CountingPublisher)rig.Publisher).Plans);
            Assert.True(!withFlow || rig.Flow!.InjectCalls > 0);
            Assert.True(
                elapsed * BudgetDivisor <= Stopwatch.Frequency * (long)ticks,
                string.Format(CultureInfo.InvariantCulture, "{0} ticks took {1} us, budget {2} us", ticks, elapsed * 1000000L / Stopwatch.Frequency, (long)ticks * 100L));
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_mean_tick_within_0_10_ms_over_two_fixture_days_with_flow()
        {
            AssertWithinBudget(withFlow: true);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_mean_tick_within_0_10_ms_over_two_fixture_days_without_flow()
        {
            AssertWithinBudget(withFlow: false);
        }
    }
}
