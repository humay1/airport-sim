using System;
using System.Diagnostics;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-023's budget: lane commands add command handling only, no new
    /// per-tick work, so sim.flow stays within 2.5 ms/tick (03-module-map.md,
    /// 09 §9.10) and allocates nothing in its Tick while every lane of the
    /// T-011 stress terminal is switched every tick. The switching is fixture
    /// load, not balance: each queue alternates between its initial
    /// ServersOpen and one lane fewer (the passport queue) or one more (the
    /// two security queues), so the day's throughput stays close to
    /// StressDay's while every queue's wait, and so every route cost, moves
    /// with every tick.
    /// </summary>
    public sealed class SecurityLaneBudgetTests
    {
        private const long BudgetMicros = 2500;
        private const long P99BudgetMicros = 2 * BudgetMicros;
        private const ulong Seed = 0x7011_3000UL;

        // StressDay.Terminal()'s queues: (node, initial ServersOpen, the other value).
        private static readonly (uint Node, int A, int B)[] Lanes = { (40, 9, 10), (41, 7, 8), (70, 6, 5) };

        private static void Switch(StressDay s)
        {
            ulong t = s.Rig.NextTick;
            foreach ((uint node, int a, int b) in Lanes)
            {
                Assert.True(s.Rig.Submit(node, t % 2 == 0 ? b : a, out CommandRejection reason), "lane command on node " + node + " rejected: " + reason);
            }
        }

        private static void Day(StressDay s)
        {
            for (int t = 0; t < (int)StressDay.Day; t++)
            {
                Switch(s);
                s.Rig.Step(1);
            }
        }

        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_security_lane_switching_every_tick_keeps_flow_within_budget()
        {
            // 03 "How a budget is measured": sim.flow's Tick only (TimedSystem),
            // one full sim-day after a day's warm-up, mean <= budget and
            // p99 <= 2x budget. Command application is phase 1, outside it.
            // 03 "Budget tests: window and arithmetic": exactly
            // TICKS_PER_SIM_DAY consecutive per-tick samples (Q-044; day 1, and
            // day 0's warm-up is not sampled), in long arithmetic only (Q-045).
            StressDay s = StressDay.Create(Seed, 2);
            Day(s);
            s.Timed.Recording = true;
            Day(s);
            s.Timed.Recording = false;
            Assert.Equal((int)SimConstants.TICKS_PER_SIM_DAY, s.Timed.Count);

            // The day carried the load and the commands took effect.
            Assert.Equal(2L * StressDay.DailyPassengers, s.Injected);
            Assert.True(s.Boarded > 0, "nobody boarded");
            Assert.True(s.Rig.Flow.TryGetLaneState(new NodeId(40), out LaneState last));
            Assert.Equal(12, last.ServerCount);
            Assert.Contains(last.ServersOpen, new[] { 9, 10 });

            // Q-045 step 2: each raw Stopwatch difference d in whole
            // microseconds, rounded up and capped at C = B x n + 1.
            long n = s.Timed.Count;
            long f = Stopwatch.Frequency;
            long cap = BudgetMicros * n + 1;
            long[] u = new long[n];
            long sum = 0;
            for (long i = 0; i < n; i++)
            {
                long d = s.Timed.Samples[i];
                u[i] = d > (long.MaxValue - f + 1) / 1_000_000 ? cap : Math.Min((d * 1_000_000 + f - 1) / f, cap);
                sum += u[i];
            }

            // Steps 3 to 5: mean iff sum <= B x n; p99 nearest rank; mean
            // reported rounded up.
            Array.Sort(u);
            long p99 = u[(99 * n + 99) / 100 - 1];
            string report = "sim.flow Tick over " + n + " ticks with every lane switched every tick: mean "
                + ((sum + n - 1) / n) + " us, p99 " + p99 + " us, max " + u[n - 1] + " us; Stopwatch.Frequency " + f;
            Assert.True(sum <= BudgetMicros * n, "mean over budget " + BudgetMicros + " us. " + report);
            Assert.True(p99 <= P99BudgetMicros, "p99 over " + P99BudgetMicros + " us. " + report);
        }

        [Fact]
        public void test_security_lane_switching_every_tick_allocates_nothing_in_flow_tick()
        {
            // 09 §9.10: no allocation in the update path. Lanes are switched
            // from tick 0 (warm-up), then sim.flow's Tick alone is metered for
            // 300 ticks from 07:00, the day's largest bank, while switching
            // continues. Submitting a command allocates (08 §8.7 copies the
            // payload) but happens between Steps, outside the meter.
            StressDay s = StressDay.Create(Seed, 1);
            ulong seven = 7 * SimConstants.TICKS_PER_SIM_HOUR;
            while (s.Rig.NextTick < seven)
            {
                Switch(s);
                s.Rig.Step(1);
            }

            Assert.True(FlowKit.TotalPopulation(s.Rig.Flow, s.Rig.World) > 500, "the morning never loaded the terminal");
            s.Timed.AllocatedInTick = 0;
            s.Timed.MeterAllocation = true;
            for (int i = 0; i < 300; i++)
            {
                Switch(s);
                s.Rig.Step(1);
            }

            s.Timed.MeterAllocation = false;
            Assert.True(s.Timed.AllocatedInTick == 0, "sim.flow's Tick allocated " + s.Timed.AllocatedInTick + " bytes over 300 ticks of lane switching");
        }
    }
}
