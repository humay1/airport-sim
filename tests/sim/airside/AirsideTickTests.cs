using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.12: no RNG at Phase 0/1 (no stream declared, none drawn), and
    /// no allocation in the update path (07 "Performance", 03 "How a budget
    /// is measured": "zero bytes allocated in the update path, asserted as
    /// well as timed"). sim.airside runs in a real host behind AirsideProbe,
    /// which hands its Tick a trap RNG and meters its Tick alone.
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
        public void test_airside_tick_allocates_nothing_on_update_path()
        {
            var flow = new RuleFlow();
            var rig = new HostRig(ScheduleFixture.MaxTier(), layout: MaxTierLayout.Layout(), flow: flow, record: false, probe: true);

            // Day 0 warms every path; day 1 is measured, sim.airside's Tick only.
            rig.RunTo(AirConst.TicksPerDay);
            long absorbsBefore = flow.AbsorbCalls;
            rig.Probe!.Allocations = true;
            long bytes = 0L;
            long start = Allocation.Start();
            while (rig.Host.CurrentTick < 2UL * AirConst.TicksPerDay)
            {
                rig.Host.Step(1);
                bytes += rig.Probe.LastBytes;
            }

            long window = Allocation.Since(start);
            Assert.True(bytes == 0L, "sim.airside's Tick allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1 (whole window " + window.ToString(CultureInfo.InvariantCulture) + ")");
            Assert.True(flow.AbsorbCalls - absorbsBefore > 100L, "day 1 did too little work");
        }
    }
}
