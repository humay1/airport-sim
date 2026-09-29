using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.6: InboundAirborne fires CRUISE_LEAD_TICKS (1200) before
    /// ScheduledTick, always on time; PlannedTick and ActualTick both equal
    /// ScheduledTick - CRUISE_LEAD_TICKS, even when the landing is later held.
    /// </summary>
    public sealed class InboundAirborneTests
    {
        [Fact]
        public void test_inbound_airborne_fires_cruise_lead_before_sta_on_time()
        {
            var rig = new HostRig(Csv.Of(
                Csv.Row("A1", "A", "06:00"),
                Csv.Row("A2", "A", "06:01"),
                Csv.Row("A3", "A", "09:13"),
                Csv.Row("A4", "A", "23:59")));
            rig.RunTo(AirConst.TicksPerDay);

            foreach ((string name, int hh, int mm) in new[] { ("A1", 6, 0), ("A2", 6, 1), ("A3", 9, 13), ("A4", 23, 59) })
            {
                ulong expected = AirConst.At(hh, mm) - AirConst.CruiseLead;
                Rec r = rig.Rec.Milestone(rig.Id(name), FlightMilestone.InboundAirborne);
                Assert.Equal(expected, r.Milestone.PlannedTick);
                Assert.Equal(expected, r.Milestone.ActualTick);
                Assert.Equal(expected, r.Tick);
            }

            // A2's landing was held (40-tick slots), its InboundAirborne was not.
            Assert.NotEmpty(rig.Rec.Of<AircraftHeldForRunway>(rig.Id("A2")));
        }
    }
}
