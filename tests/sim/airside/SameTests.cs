using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// Same-tick ordering (12 §12.8a: within a step, ascending FlightId).
    /// §12.5 (Q-049, Q-052): new runway requests at one tick are taken in
    /// ascending FlightId, so hold EventIds and queue order are too. §12.7
    /// (Q-050): new stand requests at one tick take stands in ascending
    /// FlightId. Rows are given in reverse order so file order cannot pass.
    /// </summary>
    public sealed class SameTests
    {
        [Fact]
        public void test_same_sta_arrivals_request_runway_in_flight_id_order()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A3", "A", "06:00"), Csv.Row("A2", "A", "06:00"), Csv.Row("A1", "A", "06:00")));
            ulong a1 = rig.Id("A1");
            ulong a2 = rig.Id("A2");
            ulong a3 = rig.Id("A3");
            rig.RunTo(3800UL);

            Assert.Equal(3600UL, rig.Rec.Milestone(a1, FlightMilestone.Landed).Tick);
            Assert.Empty(rig.Rec.Of<AircraftHeldForRunway>(a1));

            var h2 = rig.Rec.Of<AircraftHeldForRunway>(a2);
            var h3 = rig.Rec.Of<AircraftHeldForRunway>(a3);
            Assert.Single(h2);
            Assert.Single(h3);
            Assert.Equal(3600UL, h2[0].Rec.Tick);
            Assert.Equal(3600UL, h3[0].Rec.Tick);
            Assert.True(h2[0].Rec.Id.CompareTo(h3[0].Rec.Id) < 0, "holds must be emitted in FlightId order");

            AirsideAsserts.RunwayReleasedInto(rig.Rec, a2, FlightMilestone.Landed, 3640UL);
            AirsideAsserts.RunwayReleasedInto(rig.Rec, a3, FlightMilestone.Landed, 3680UL);
        }

        [Fact]
        public void test_same_tick_stand_requests_take_stands_in_flight_id_order()
        {
            // At 3600: C1 leaves the runway (STA 05:59), and D1 and D2
            // (rotation-less) reach their due tick. A stand granted earlier in
            // the step is unavailable to the rest of it, even though D1 pushes
            // back at once.
            var rig = new HostRig(Csv.Of(Csv.Row("D2", "D", "06:35"), Csv.Row("D1", "D", "06:35"), Csv.Row("C1", "A", "05:59")));
            ulong c1 = rig.Id("C1");
            ulong d1 = rig.Id("D1");
            ulong d2 = rig.Id("D2");
            rig.RunTo(3700UL);

            Assert.Equal(3600UL, rig.Rec.Milestone(c1, FlightMilestone.OffRunway).Tick);
            Assert.Equal(FixtureLayout.S1, rig.Rec.Milestone(c1, FlightMilestone.OnStand).Track.Stand!.Value.Value);
            foreach ((ulong d, ushort stand) in new[] { (d1, FixtureLayout.S2), (d2, FixtureLayout.S3) })
            {
                Assert.Equal(3600UL, rig.Rec.Milestone(d, FlightMilestone.OnStand).Tick);
                Rec push = rig.Rec.Milestone(d, FlightMilestone.Pushback);
                Assert.Equal(3600UL, push.Tick);
                Assert.Equal(FixtureLayout.StandNode(stand), push.Track.AtNode!.Value.Value);
            }

            Assert.True(rig.Rec.Milestone(d1, FlightMilestone.OnStand).Id.CompareTo(rig.Rec.Milestone(d2, FlightMilestone.OnStand).Id) < 0);
        }
    }
}
