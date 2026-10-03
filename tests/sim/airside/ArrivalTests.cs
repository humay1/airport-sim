using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.7 "Arriving passengers": a Phase 0/1 arrival carries zero pax
    /// (11 §11.4), so no Inject is made at DoorsOpen; the call is skipped,
    /// never made with count 0 (09 §9.7 precondition).
    /// </summary>
    public sealed class ArrivalTests
    {
        [Fact]
        public void test_arrival_pax_count_zero_skips_inject()
        {
            ScriptedFlow flow = ScriptedFlow.None();
            var rig = new HostRig(ScheduleFixture.Bytes(), flow: flow);
            rig.RunTo(AirConst.TicksPerDay);

            int doorsOpen = 0;
            foreach (Rec r in rig.Rec.All)
            {
                if (r.FromAirside && r.IsMilestone(FlightMilestone.DoorsOpen))
                {
                    Assert.Equal(0, rig.Flight(r.Flight).PaxCount);
                    doorsOpen++;
                }
            }

            Assert.True(doorsOpen > 0, "no arrival reached DoorsOpen in a day of the Phase 0 fixture");
            Assert.Equal(0L, flow.InjectCalls);
        }
    }
}
