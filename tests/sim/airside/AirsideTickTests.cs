using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12: no RNG at Phase 0/1 (no stream declared, none drawn), and
    /// no allocation in the update path at max tier (03 "The update path",
    /// "allocation test", Q-061). The RNG test runs sim.airside behind
    /// AirsideProbe, which hands its Tick a trap RNG. The allocation test is
    /// 03's first form: the T-037 meter over ISimHost.Step windows that hold
    /// no checkpoint tick and no tick allowed to allocate. The handshake
    /// records and ReassignStand Apply are metered by
    /// AirsideUpdatePathTests.
    /// </summary>
    public sealed class AirsideTickTests
    {
        [Fact]
        public void test_airside_tick_consumes_no_rng()
        {
            foreach (bool withFlow in new[] { false, true })
            {
                var trap = new TrapRandomService();
                var rig = new HostRig(ScheduleFixture.Bytes(), flow: withFlow ? new RuleFlow() : null, probe: true);
                rig.Probe!.Rng = trap;
                rig.RunTo(2UL * AirConst.TicksPerDay);
                Assert.Equal(0, trap.StreamCalls);
                Assert.Equal(0, trap.Draws);

                // The run must have exercised the choices §12.12 lists: runway
                // holds, stand assignment and, with flow, the boarding hold.
                Assert.NotEmpty(rig.Rec.Of<AircraftHeldForRunway>());
                Assert.NotEmpty(rig.Rec.Of<StandUnavailable>());
                Assert.True(!withFlow || rig.Rec.Of<DepartureHeldForPassengers>().Count > 0);
            }
        }

        [Fact]
        public void test_airside_tick_and_handlers_allocate_nothing_over_a_max_tier_day()
        {
            // Every registered system allocates nothing per tick: sim.schedule
            // (outside its day boundary, 11 §11.9), sim.airside, the RuleFlow
            // fake, and no recorder. Day 0 warms every path, including tick
            // 14 400 (a checkpoint and sim.schedule's day boundary). Day 1 is
            // then metered in its 24 windows of 599 ticks, each between two
            // checkpoints. In it run sim.airside's Tick, its
            // FlightPlanPublished handler (day 2's flights are published
            // during day 1) and its FlightMilestoneReached subscription, if
            // it has one in this build.
            var flow = new RuleFlow();
            var rig = new HostRig(ScheduleFixture.MaxTier(), layout: MaxTierLayout.Layout(), flow: flow, record: false);
            rig.RunTo(AirConst.TicksPerDay + 1UL);
            long absorbsBefore = flow.AbsorbCalls;

            const ulong checkpoint = 600UL;
            for (ulong window = 0; window < AirConst.TicksPerDay / checkpoint; window++)
            {
                ulong first = AirConst.TicksPerDay + (window * checkpoint) + 1UL;
                Assert.Equal(first, rig.Host.CurrentTick);
                long start = Allocation.Start();
                rig.Host.Step((uint)(checkpoint - 1UL));
                long bytes = Allocation.Since(start);
                Assert.True(
                    bytes == 0L,
                    string.Format(CultureInfo.InvariantCulture, "ticks {0}-{1} allocated {2} bytes", first, first + checkpoint - 2UL, bytes));

                // The checkpoint tick itself is outside the update path (03), unmetered.
                rig.Host.Step(1);
            }

            Assert.Equal(2UL * AirConst.TicksPerDay + 1UL, rig.Host.CurrentTick);
            Assert.True(flow.AbsorbCalls - absorbsBefore > 100L, "day 1 did too little work");
        }
    }
}
