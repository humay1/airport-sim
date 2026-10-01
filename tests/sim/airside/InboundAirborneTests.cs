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

        [Fact]
        public void test_inbound_airborne_clamps_to_tick_zero_before_cruise_lead()
        {
            // 12 §12.6 (Q-048): max(0, STA - CRUISE_LEAD_TICKS), planned and actual.
            var rig = new HostRig(Csv.Of(
                Csv.Row("A1", "A", "00:20"),
                Csv.Row("A2", "A", "01:00"),
                Csv.Row("A3", "A", "02:00"),
                Csv.Row("A4", "A", "02:01")));
            rig.RunTo(1UL);
            foreach (string name in new[] { "A1", "A2", "A3" })
            {
                Rec r = rig.Rec.Milestone(rig.Id(name), FlightMilestone.InboundAirborne);
                Assert.Equal(0UL, r.Tick);
                Assert.Equal(0UL, r.Milestone.PlannedTick);
                Assert.Equal(0UL, r.Milestone.ActualTick);
                Assert.Equal(AircraftLegPhase.AwaitingApproach, rig.Track(rig.Id(name)).Phase);
            }

            Assert.False(rig.Rec.Has(rig.Id("A4"), FlightMilestone.InboundAirborne));
            rig.RunTo(11UL);
            Rec a4 = rig.Rec.Milestone(rig.Id("A4"), FlightMilestone.InboundAirborne);
            Assert.Equal(10UL, a4.Milestone.PlannedTick);
            Assert.Equal(10UL, a4.Milestone.ActualTick);

            // The early arrival still lands at its own STA (12 §12.5, Q-052).
            rig.RunTo(300UL);
            Assert.Equal(AirConst.At(0, 20), rig.Rec.Milestone(rig.Id("A1"), FlightMilestone.Landed).Tick);
        }
    }
}
