using System;
using AirportSim.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-023's budget: lane commands add command handling only, no new
    /// per-tick work, so sim.flow stays within 2.5 ms/tick (03-module-map.md,
    /// 09 §9.10) and allocates nothing in its update path while every lane of
    /// the T-011 stress terminal is switched every tick. The switching is
    /// fixture load, not balance: each queue alternates between its initial
    /// ServersOpen and one lane fewer (the passport queue) or one more (the
    /// two security queues), so the day's throughput stays close to
    /// StressDay's while every queue's wait, and so every route cost, moves
    /// with every tick.
    /// </summary>
    public sealed class SecurityLaneBudgetTests
    {
        private const long BudgetMicros = 2500;
        private const ulong Seed = 0x7011_3000UL;

        // StressDay.Terminal()'s queues: (node, initial ServersOpen, the other value).
        private static readonly (uint Node, int A, int B)[] Lanes = { (40, 9, 10), (41, 7, 8), (70, 6, 5) };

        private readonly ITestOutputHelper _output;

        public SecurityLaneBudgetTests(ITestOutputHelper output)
        {
            _output = output;
        }

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
                s.Step();
            }
        }

        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_security_lane_switching_every_tick_keeps_flow_within_budget()
        {
            // 03 "How a budget is measured" (Q-064): each tick's sample is
            // sim.flow's Tick plus its shimmed handlers, so the three
            // SetServersOpen Apply calls of phase 1 are inside it. Submitting
            // (admission, Validate) happens between Steps, outside it.
            // 03 "Budget tests: window and arithmetic": exactly
            // TICKS_PER_SIM_DAY consecutive per-tick samples (Q-044; day 1, and
            // day 0's warm-up is not sampled), in long arithmetic only (Q-045).
            StressDay s = StressDay.Create(Seed, 2);
            Assert.Equal(1, s.Clock.CommandHandlers);
            Day(s);
            s.Clock.StartWindow();
            Day(s);
            s.Clock.StopWindow();

            // The day carried the load and the commands took effect. Every
            // tick of day 1 had three commands due, submitted during the
            // previous ticks with the minimum lead (08 §8.7), so the shim
            // timed exactly three Apply calls a tick.
            Assert.Equal(2L * StressDay.DailyPassengers, s.Injected);
            Assert.True(s.Boarded > 0, "nobody boarded");
            Assert.True(s.Rig.Flow.TryGetLaneState(new NodeId(40), out LaneState last));
            Assert.Equal(12, last.ServerCount);
            Assert.Contains(last.ServersOpen, new[] { 9, 10 });
            Assert.Equal(3L * (long)StressDay.Day, s.Clock.ApplyCalls);

            FlowBudget.Assert(s.Clock, BudgetMicros, "sim.flow (Tick + handlers) with every lane switched every tick", _output);
        }

        [Fact]
        public void test_security_lane_switching_every_tick_allocates_nothing_in_flow_tick()
        {
            // 09 §9.10 and 03 "How a budget is measured" (Q-061): no
            // allocation in the update path, which includes the SetServersOpen
            // Apply. Lanes are switched from tick 0, so the warm-up has run
            // Apply many times already. Then 300 ticks of the morning bank,
            // from just after the 07:00 checkpoint, are metered one whole
            // Step(1) at a time (T-037's meter over ISimHost.Step), while
            // switching continues: each window holds that tick's three Apply
            // calls in phase 1, sim.flow's Tick and event dispatch. No window
            // holds a checkpoint tick (08 §8.9's SystemHashes array).
            // Submitting a command allocates (08 §8.7 copies the payload) but
            // happens between Steps, outside every window.
            StressDay s = StressDay.Create(Seed, 1);
            ulong seven = 7 * SimConstants.TICKS_PER_SIM_HOUR;
            while (s.Rig.NextTick <= seven)
            {
                Switch(s);
                s.Rig.Step(1);
            }

            Assert.True(FlowKit.TotalPopulation(s.Rig.Flow, s.Rig.World) > 500, "the morning never loaded the terminal");
            Assert.True(s.Clock.ApplyCalls > 0, "the warm-up never ran Apply");
            long applied = s.Clock.ApplyCalls;
            long bytes = 0;
            string first = "none";
            for (int i = 0; i < 300; i++)
            {
                ulong tick = s.Rig.NextTick;
                Assert.NotEqual(0UL, tick % SimConstants.HASH_CHECKPOINT_TICKS);
                Switch(s);
                long start = Allocation.Start();
                s.Rig.Step(1);
                long window = Allocation.Since(start);
                bytes += window;
                if (window != 0 && first == "none")
                {
                    first = "tick " + tick + ": " + window + " bytes";
                }
            }

            Assert.Equal(3L * 300L, s.Clock.ApplyCalls - applied);
            Assert.True(bytes == 0, "the update path allocated " + bytes + " bytes over 300 ticks of lane switching, Apply included; first: " + first);
        }
    }
}
