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
        public void test_security_lane_switching_every_tick_keeps_flow_within_budget()
        {
            // 03 "How a budget is measured": sim.flow's Tick only (TimedSystem),
            // one full sim-day after a day's warm-up, mean <= budget and
            // p99 <= 2x budget. Command application is phase 1, outside it.
            // Q-044/Q-045: exactly 14 400 consecutive per-tick samples (day 1;
            // day 0's warm-up is not sampled), p99 the nearest-rank sample at
            // index (99n + 99) / 100 - 1 of the raw Stopwatch differences,
            // compared in exact integer arithmetic.
            StressDay s = StressDay.Create(Seed, 2);
            Day(s);
            s.Timed.Recording = true;
            Day(s);
            s.Timed.Recording = false;
            Assert.Equal(14400, s.Timed.Count);

            // The day carried the load and the commands took effect.
            Assert.Equal(2L * StressDay.DailyPassengers, s.Injected);
            Assert.True(s.Boarded > 0, "nobody boarded");
            Assert.True(s.Rig.Flow.TryGetLaneState(new NodeId(40), out LaneState last));
            Assert.Equal(12, last.ServerCount);
            Assert.Contains(last.ServersOpen, new[] { 9, 10 });

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
            Int128 freq = Stopwatch.Frequency;
            string report = "sim.flow Tick over " + n + " ticks with every lane switched every tick: mean "
                + (total * 1_000_000 / freq / n) + " us, p99 " + ((Int128)p99 * 1_000_000 / freq) + " us";
            Assert.True(total * 1_000_000 <= BudgetMicros * freq * n, "mean over budget " + BudgetMicros + " us. " + report);
            Assert.True((Int128)p99 * 1_000_000 <= P99BudgetMicros * freq, "p99 over " + P99BudgetMicros + " us. " + report);
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
