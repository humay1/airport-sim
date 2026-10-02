using AirportSim.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-010: with promotion on, sim.flow stays within 2.5 ms/tick
    /// (09 §9.10), measured by 03 "How a budget is measured": each tick's
    /// sample is sim.flow's Tick plus the bodies of the handlers it registers,
    /// timed through shims in its SystemServices (Q-064, <see cref="FlowClock"/>),
    /// over exactly TICKS_PER_SIM_DAY consecutive ticks (Q-044), mean ≤ 2.5 ms
    /// and p99 ≤ 5.0 ms in long arithmetic (Q-045, <see cref="FlowBudget"/>).
    /// Load: about 90 000 passengers a day (03's max tier) through the wide
    /// security graph, with every node promoted and all agents read every
    /// tick, between Steps and outside the sample. This fixture submits no
    /// command, so no Apply runs; the report says how many handlers the shims
    /// wrapped and how often they ran.
    /// </summary>
    public sealed class PromotionBudgetTests
    {
        private const long BudgetMicros = 2500;

        private readonly ITestOutputHelper _output;

        public PromotionBudgetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_promotion_budget_one_day_with_every_node_promoted()
        {
            var plan = new PromoPlan(0x0010_B0D6UL, 1500, 6, 20);
            Assert.InRange(plan.DailyPax, 80000L, 100000L);

            // A first host warms the process up (JIT), then the measured host
            // runs a day of warm-up, not sampled.
            var warm = new PromoRig(plan, PromoGraphs.Wide, record: false, recordCheckpoints: false, clock: new FlowClock((int)PromoConst.TicksPerDay));
            warm.SetAll(true);
            warm.Host.Step((uint)PromoConst.TicksPerDay);

            var clock = new FlowClock((int)PromoConst.TicksPerDay);
            var rig = new PromoRig(plan, PromoGraphs.Wide, record: false, recordCheckpoints: false, clock: clock);
            Assert.Equal(1, clock.CommandHandlers);
            rig.SetAll(true);
            rig.Host.Step((uint)PromoConst.TicksPerDay);

            int n = (int)PromoConst.TicksPerDay;
            long views = 0;
            clock.StartWindow();
            for (int i = 0; i < n; i++)
            {
                clock.Begin();
                rig.Host.Step(1);
                clock.End();
                foreach (uint node in PromoConst.AllNodes)
                {
                    views += rig.Flow.AgentsAt(new NodeId(node)).Count;
                }
            }

            clock.StopWindow();
            Assert.True(views > 0, "the promoted day must show agents");
            Assert.True(rig.Absorber.Absorbed > 0, "the day must board passengers");
            FlowBudget.Assert(clock, BudgetMicros, "sim.flow (Tick + handlers), every node promoted", _output);
        }
    }
}
