using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12: no RNG at Phase 0/1 (no stream declared, none drawn), and
    /// no allocation in the update path (07 "Performance", 03 "How a budget
    /// is measured": "zero bytes allocated in the update path, asserted as
    /// well as timed"). sim.airside is ticked directly so both are its own.
    /// </summary>
    public sealed class AirsideTickTests
    {
        [Fact]
        public void test_airside_tick_consumes_no_rng()
        {
            foreach (bool withFlow in new[] { false, true })
            {
                var trap = new TrapRandomService();
                var rig = new DirectRig(ScheduleFixture.Bytes(), FixtureLayout.Layout(), withFlow ? new RuleFlow() : null, rng: trap);
                rig.RunTo(2UL * AirConst.TicksPerDay);
                Assert.Equal(0, trap.StreamCalls);
                Assert.Equal(0, trap.Draws);

                // The run must have exercised the choices §12.12 lists: runway
                // holds, stand assignment and, with flow, the boarding hold.
                Assert.True(rig.Publisher.Milestones > 0L);
                Assert.True(rig.Publisher.RunwayHolds > 0L);
                Assert.True(!withFlow || rig.Publisher.PassengerHolds > 0L);
            }
        }

        [Fact]
        public void test_airside_tick_allocates_nothing_on_update_path()
        {
            var flow = new RuleFlow();
            var rig = new DirectRig(ScheduleFixture.MaxTier(), MaxTierLayout.Layout(), flow);

            // Day 0 warms every path; day 1 is measured, sim.airside's Tick only.
            rig.RunTo(AirConst.TicksPerDay);
            long milestonesBefore = rig.Publisher.Milestones;
            long bytes = 0L;
            long start = Allocation.Start();
            while (rig.NextTick < 2UL * AirConst.TicksPerDay)
            {
                bytes += rig.TickAllocated();
            }

            long window = Allocation.Since(start);
            Assert.True(bytes == 0L, "sim.airside's Tick allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1 (whole window " + window.ToString(CultureInfo.InvariantCulture) + ")");
            Assert.True(rig.Publisher.Milestones > milestonesBefore, "day 1 did no work");
            Assert.True(flow.OutstandingQueries > 0L && flow.AbsorbCalls > 0L);
        }
    }
}
