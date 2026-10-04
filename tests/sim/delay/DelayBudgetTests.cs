using System;
using System.Diagnostics;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 14 §14.13 and 03 "How a budget is measured": B = 400 µs/tick at max tier,
    /// timing sim.delay's Tick plus the bodies of every handler it registers,
    /// through 03's timing shims (Q-064). 03 "Budget tests: window and
    /// arithmetic" (Q-044, Q-045): one window of exactly n = TICKS_PER_SIM_DAY
    /// consecutive ticks after warm-up, each sample converted to whole
    /// microseconds rounding up and capped at C = B × n + 1, everything in long.
    /// Pass iff Σu ≤ B × n and the nearest-rank p99 ≤ 2 × B. The authoritative
    /// measurement stays tools/SimHarness budget.
    ///
    /// sim.delay sees only events, so the max-tier load is the event stream of
    /// 800 daily movements: four copies of tests/fixtures/schedule/phase0-200.csv
    /// (03's movement count), with lateness and blocking intervals from the
    /// seeded generator. The drivers' own work is not timed.
    /// </summary>
    public sealed class DelayBudgetTests
    {
        private const long BudgetMicros = 400L;
        private const int N = (int)DConst.TicksPerDay;
        private const ulong WarmUp = 600UL;

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public DelayBudgetTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

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

        internal static Script MaxTierScript(ulong seed, int lastDay)
        {
            byte[] load = ScheduleFixture.MaxTier();
            var rng = new SplitMix64(seed);
            var s = new Script();
            for (int day = 0; day <= lastDay; day++)
            {
                Generator.Day(rng, ScheduleFixture.Day(load, day), s, 10);
            }

            return s;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_delay_budget_max_tier_day_mean_and_p99_within_budget()
        {
            Assert.Equal(800, ScheduleFixture.Day(ScheduleFixture.MaxTier(), 0).Count);
            var handlers = new HandlerTimer();
            var rig = new DelayRig(MaxTierScript(0x0024_B0D6UL, 2), record: false, handlerTimer: handlers, probe: true);

            // The first 600 ticks warm every path and are not sampled. The window
            // is the next 14 400 consecutive ticks, one sample each, so it covers
            // every time of day once (day 1's movements are driven too).
            rig.RunTo(WarmUp);
            long callsBefore = handlers.Calls;
            rig.Probe!.Timing = true;
            long f = Stopwatch.Frequency;
            long cap = (BudgetMicros * N) + 1L;
            var u = new long[N];
            long sum = 0L;
            for (int i = 0; i < N; i++)
            {
                handlers.Elapsed = 0L;
                rig.Host.Step(1);
                u[i] = Micros(rig.Probe.LastElapsed + handlers.Elapsed, f, cap);
                sum += u[i];
            }

            // The window did the work: thousands of handler calls, and most of
            // a day's flights finalised inside it.
            Assert.True(handlers.Calls - callsBefore > 5000L, "the handlers ran too rarely to be timed: " + (handlers.Calls - callsBefore));
            int finalised = 0;
            foreach (FlightId fl in rig.Delay.RetainedFlights())
            {
                if (rig.Delay.TryGetFlightDelay(fl, out FlightDelay r) && r.Finalised && r.FinalisedAt >= WarmUp && r.FinalisedAt < WarmUp + (ulong)N)
                {
                    finalised++;
                }
            }

            Assert.True(finalised > 600, "the measured window did too little work: " + finalised + " flights finalised");

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

            // 03 Q-045 step 5: the mean and p99 are reported on every run.
            _output.WriteLine(report);
            Assert.True(sum <= BudgetMicros * N, "mean over budget: " + report);
            Assert.True(p99 <= 2L * BudgetMicros, "p99 over budget: " + report);
        }

        [Fact]
        public void test_delay_budget_handler_shims_forward_without_changing_the_run()
        {
            // The shims only time: a shimmed run is, publication for publication
            // and hash for hash, the run without them, and the handlers did run.
            var handlers = new HandlerTimer();
            var shimmed = new DelayRig(MaxTierScript(0x0024_5111UL, 0), handlerTimer: handlers, probe: true);
            var plain = new DelayRig(MaxTierScript(0x0024_5111UL, 0));
            shimmed.RunTo(DConst.TicksPerDay);
            plain.RunTo(DConst.TicksPerDay);
            Assert.True(handlers.Calls > 0L);
            Assert.True(plain.R.Delays.Count > 100);
            Assert.Equal(plain.R.Dump(), shimmed.R.Dump());
            Assert.Equal(plain.Sink.Describe(), shimmed.Sink.Describe());
            Assert.Equal(Show.Snapshot(plain.Delay), Show.Snapshot(shimmed.Delay));
        }
    }
}
