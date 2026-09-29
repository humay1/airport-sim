using System;
using System.Diagnostics;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-011, the Phase 0 kill gate (tasks/queue.md: "if T-011 cannot meet
    /// budget, the architecture is redesigned here"): 30 000 daily
    /// passengers through a representative terminal (<see cref="StressDay"/>)
    /// within sim.flow's budget, measured per 03-module-map.md "How a budget
    /// is measured": the module's Tick only, excluding fixture setup and the
    /// checkpoint phase, across one full sim-day's ticks, passing if the mean
    /// is at most the 2.5 ms budget and the p99 at most twice it (09 §9.10);
    /// zero bytes allocated in the update path; and a ceiling on live cohorts,
    /// which mandatory merging (§9.3) is what bounds.
    ///
    /// Day 0 of each run is warm-up (fixture setup: JIT tiering and pooled
    /// storage reaching its high-water mark). Day 1, the same schedule with
    /// new flight ids, is the measured day.
    /// </summary>
    public sealed class FlowStressBudgetTests
    {
        private const long BudgetMicros = 2500;
        private const long P99BudgetMicros = 2 * BudgetMicros;
        private const ulong Seed = 0x7011_3000UL;

        private readonly ITestOutputHelper _output;

        public FlowStressBudgetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string Micros(Int128 stopwatchTicks)
        {
            Int128 nanos = stopwatchTicks * 1_000_000_000 / Stopwatch.Frequency;
            return (nanos / 1000).ToString(CultureInfo.InvariantCulture) + "." + (nanos % 1000).ToString("000", CultureInfo.InvariantCulture) + " us";
        }

        /// <summary>
        /// Fixture sanity, independent of sim.flow: the plan sums to exactly
        /// 30 000 passengers a day over 200 flights, with the 07:00 bank the
        /// largest, and every injection lands inside its own day.
        /// </summary>
        private static void AssertFixtureShape()
        {
            StressDay.Flight[] day = StressDay.Schedule(Seed);
            Assert.Equal(StressDay.Flights, day.Length);
            long pax = 0;
            var perHour = new int[24];
            foreach (StressDay.Flight f in day)
            {
                Assert.InRange(f.Pax, 80, 220);
                pax += f.Pax;
                perHour[f.Std / SimConstants.TICKS_PER_SIM_HOUR]++;
            }

            Assert.Equal(StressDay.DailyPassengers, pax);
            Assert.Equal(22, perHour[7]);
            for (int h = 0; h < 24; h++)
            {
                Assert.True(perHour[h] <= perHour[7], "hour " + h + " outbanks 07:00");
            }

            StressDay.Plan(day, 2, out StressDay.Injection[] injections, out StressDay.Boarding[] boardings);
            var perDay = new long[2];
            foreach (StressDay.Injection i in injections)
            {
                int d = (int)(i.Tick / StressDay.Day);
                Assert.Equal((ulong)d, (i.Key.Flight.Value - 1) / StressDay.Flights);
                perDay[d] += i.Count;
            }

            Assert.Equal(StressDay.DailyPassengers, perDay[0]);
            Assert.Equal(StressDay.DailyPassengers, perDay[1]);
            Assert.Equal(2 * StressDay.Flights, boardings.Length);
        }

        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_flow_stress_30k_day_tick_within_budget_and_bounded_cohorts()
        {
            AssertFixtureShape();
            StressDay s = StressDay.Create(Seed, 2);
            Assert.Equal(63, s.Rig.World.Nodes().Count);

            // Day 0: warm-up, untimed. After every tick, 09's per-tick
            // invariants hold, including mandatory merging itself: no two
            // cohorts on a node share a CohortKey (and, on a Corridor, a
            // DueAt), since none is blocked (§9.3, §9.12). That is what the
            // ceiling below bounds.
            var noEpisodes = new System.Collections.Generic.HashSet<ulong>();
            for (int t = 0; t < (int)StressDay.Day; t++)
            {
                s.Rig.Step(1);
                s.Rig.CheckInvariants("day 0 tick " + t, noEpisodes);
            }

            Assert.Equal(StressDay.DailyPassengers, s.Injected);
            Assert.Equal(StressDay.DailyPassengers, s.Boarded + s.Counters.MissedPassengers);

            // Day 1: every tick's sim.flow Tick timed; after each tick, the
            // live cohorts checked against that tick's ceiling and the head
            // count checked for conservation (09 §9.2).
            long[] ceiling = s.CeilingByTick(1);
            long peakCeiling = 0;
            int peakLive = 0;
            ulong peakTick = 0;
            s.Timed.Recording = true;
            for (int t = 0; t < (int)StressDay.Day; t++)
            {
                s.Rig.Step(1);
                int live = s.LiveCohorts();
                if (live > ceiling[t])
                {
                    Assert.Fail("tick " + (StressDay.Day + (ulong)t) + ": " + live + " live cohorts exceed the ceiling " + ceiling[t]);
                }

                if (live > peakLive)
                {
                    peakLive = live;
                    peakTick = StressDay.Day + (ulong)t;
                }

                peakCeiling = Math.Max(peakCeiling, ceiling[t]);
                long accounted = FlowKit.TotalPopulation(s.Rig.Flow, s.Rig.World) + s.Boarded + s.Counters.MissedPassengers;
                if (accounted != s.Injected)
                {
                    Assert.Fail("tick " + (StressDay.Day + (ulong)t) + ": head count not conserved, " + accounted + " accounted of " + s.Injected);
                }
            }

            s.Timed.Recording = false;
            Assert.Equal((int)StressDay.Day, s.Timed.Count);

            // The day really carried the load: every passenger of both days
            // was injected, reached a gate or missed their flight, and none is
            // left in the terminal; no cohort was ever blocked (the fixture
            // has no full node, 09 §9.5).
            Assert.Equal(2L * StressDay.DailyPassengers, s.Injected);
            Assert.Equal(0, FlowKit.TotalPopulation(s.Rig.Flow, s.Rig.World));
            Assert.Equal(2L * StressDay.DailyPassengers, s.Boarded + s.Counters.MissedPassengers);
            Assert.True(s.Counters.ArrivedPassengers >= s.Boarded, "boarded " + s.Boarded + " but only " + s.Counters.ArrivedPassengers + " arrived at a gate");
            Assert.True(s.Boarded > 0, "nobody boarded");
            Assert.Equal(0L, s.Counters.Blocked);
            Assert.True(s.Counters.Thresholds > 0, "no security queue ever crossed its threshold: the day never loaded the queues");
            Assert.True(peakLive > 0);

            // Statistic: mean <= budget, p99 <= 2x budget, across the day's
            // ticks. p99 is the nearest-rank value: the ceil(0.99 n)-th
            // smallest sample.
            long[] samples = new long[s.Timed.Count];
            Array.Copy(s.Timed.Samples, samples, samples.Length);
            Int128 total = 0;
            foreach (long x in samples)
            {
                total += x;
            }

            Array.Sort(samples);
            int n = samples.Length;
            long p99 = samples[(99 * n + 99) / 100 - 1];
            long max = samples[n - 1];
            Int128 freq = Stopwatch.Frequency;
            string report = "sim.flow Tick over " + n + " ticks: mean " + Micros(total / n)
                + ", p99 " + Micros(p99) + ", max " + Micros(max)
                + "; peak live cohorts " + peakLive + " at tick " + peakTick + " (ceiling there " + ceiling[(int)(peakTick - StressDay.Day)]
                + ", day peak ceiling " + peakCeiling + "); boarded " + s.Boarded + ", missed " + s.Counters.MissedPassengers
                + ", threshold events " + s.Counters.Thresholds + "; Stopwatch.Frequency " + Stopwatch.Frequency;
            _output.WriteLine(report);

            // mean <= BudgetMicros  <=>  total * 1e6 <= BudgetMicros * freq * n, exactly.
            Assert.True(total * 1_000_000 <= BudgetMicros * freq * n, "mean over budget " + BudgetMicros + " us. " + report);
            Assert.True((Int128)p99 * 1_000_000 <= P99BudgetMicros * freq, "p99 over " + P99BudgetMicros + " us. " + report);
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void test_flow_stress_30k_day_update_path_allocates_nothing()
        {
            // 03 "How a budget is measured": zero bytes allocated in the update
            // path, asserted as well as timed. After day 0's warm-up, all of
            // day 1 is metered. The checkpoint phase allocates a SystemHashes
            // array (08 §8.9) and is billed separately, so a checkpoint tick
            // (tick % HASH_CHECKPOINT_TICKS == 0) meters sim.flow's Tick alone,
            // and the 599 ticks between checkpoints meter the whole update path:
            // the injector's Inject and Absorb calls, sim.flow's Tick and event
            // dispatch. Each window starts after a full blocking collection, so
            // the thread holds no partly used allocation context that the
            // runtime could retire mid-window and over-report (T-037's
            // Allocation meter, copied into FlowTestKit).
            StressDay s = StressDay.Create(Seed, 2);
            s.Rig.Step((uint)StressDay.Day);

            long windows = 0;
            long checkpointTicks = 0;
            string first = "none";
            const uint Hour = (uint)SimConstants.HASH_CHECKPOINT_TICKS;
            for (int h = 0; h < (int)(StressDay.Day / Hour); h++)
            {
                ulong at = s.Rig.Host.CurrentTick;
                Assert.Equal(0UL, at % Hour);
                s.Timed.AllocatedInTick = 0;
                s.Timed.MeterAllocation = true;
                s.Rig.Step(1);
                s.Timed.MeterAllocation = false;
                checkpointTicks += s.Timed.AllocatedInTick;
                if (s.Timed.AllocatedInTick != 0 && first == "none")
                {
                    first = "sim.flow Tick at checkpoint tick " + at + ": " + s.Timed.AllocatedInTick + " bytes";
                }

                long start = Allocation.Start();
                s.Rig.Step(Hour - 1);
                long window = Allocation.Since(start);
                windows += window;
                if (window != 0 && first == "none")
                {
                    first = "ticks " + (at + 1) + ".." + (at + Hour - 1) + ": " + window + " bytes";
                }
            }

            Assert.Equal(2 * StressDay.Day, s.Rig.Host.CurrentTick);
            Assert.Equal(2L * StressDay.DailyPassengers, s.Injected);
            Assert.True(s.Boarded > 0, "nobody boarded");
            _output.WriteLine("allocated: update path between checkpoints " + windows + " bytes, sim.flow Tick at checkpoint ticks " + checkpointTicks + " bytes; first: " + first);
            Assert.True(windows == 0 && checkpointTicks == 0, "update path allocated " + windows + " bytes between checkpoints and sim.flow Tick " + checkpointTicks + " bytes at checkpoint ticks; first: " + first);
        }
    }
}
