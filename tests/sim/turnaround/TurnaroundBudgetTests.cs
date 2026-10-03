using System;
using System.Diagnostics;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.10 and 03 "How a budget is measured": B = 500 µs/tick at max
    /// tier (800 daily movements), timing sim.turnaround's Tick plus the
    /// bodies of the handlers it registers, through 03's timing shims (Q-064).
    /// 03 "Budget tests: window and arithmetic" (Q-044, Q-045): one window of
    /// exactly n = TICKS_PER_SIM_DAY consecutive ticks after warm-up, each
    /// sample converted to whole microseconds rounding up and capped at
    /// C = B × n + 1, everything in long. Pass iff Σu ≤ B × n and the
    /// nearest-rank p99 ≤ 2 × B. The authoritative measurement stays
    /// tools/SimHarness budget.
    ///
    /// The OnStand events come from the position-3 driver (an arrival 50 ticks
    /// after STA, a departure at its planned OnStand); its own work is not
    /// timed. The fleet is fixture sizing for the max-tier load, not balance:
    /// twelve vehicles of each kind, so waits happen at the banks without the
    /// blocked list growing all day.
    /// </summary>
    public sealed class TurnaroundBudgetTests
    {
        private const long BudgetMicros = 500L;
        private const int N = (int)TConst.TicksPerDay;
        private const ulong WarmUp = 600UL;

        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public TurnaroundBudgetTests(Xunit.Abstractions.ITestOutputHelper output)
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

        [Fact]
        [Trait("Category", "Budget")]
        public void test_turnaround_budget_max_tier_day_mean_and_p99_within_budget()
        {
            byte[] load = ScheduleFixture.MaxTier();
            Assert.Equal(800, Csv.Ids(load).Count);

            var handlers = new HandlerTimer();
            var rig = new Rig(load, Setups.Of(Phase1Fixture.Jobs(), Setups.Plenty(12)), driveDays: 2, record: false, handlerTimer: handlers, probe: true);

            // The first sim-hour warms every path and is not sampled. The window
            // is the next 14 400 consecutive ticks, one sample each, so it
            // covers every time of day once (day 1's movements are driven too).
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

            // About 800 OnStands reach the handler in any one-day window.
            Assert.True(handlers.Calls - callsBefore > 700L, "the handler ran too rarely to be timed");
            int boarded = 0;
            foreach (Movement m in rig.Moves.Values)
            {
                if (m.Departure && m.PlannedDepartureOnStand >= WarmUp && m.PlannedDepartureOnStand + 400UL < WarmUp + (ulong)N
                    && rig.Turnaround.TryGetJob(TConst.Job(m.Flight, JobKind.Boarding), out TurnaroundJob b) && b.Status == JobStatus.Completed)
                {
                    boarded++;
                }
            }

            Assert.True(boarded > 300, "the measured window did too little work: " + boarded.ToString(CultureInfo.InvariantCulture) + " departures boarded");

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
        public void test_turnaround_budget_handler_shims_forward_without_changing_the_run()
        {
            // The shims only time: a shimmed run is event for event, and hash
            // for hash, the run without them, and the handler did run.
            var handlers = new HandlerTimer();
            var shimmed = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1, handlerTimer: handlers, probe: true);
            var plain = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1);
            shimmed.RunTo(TConst.TicksPerDay);
            plain.RunTo(TConst.TicksPerDay);
            Assert.True(handlers.Calls > 0L);
            Assert.NotEmpty(plain.Rec.Of<TurnaroundJobStarted>());
            Assert.Equal(plain.Rec.Trace(), shimmed.Rec.Trace());
            Assert.Equal(plain.Sink.Describe(), shimmed.Sink.Describe());
        }
    }
}
