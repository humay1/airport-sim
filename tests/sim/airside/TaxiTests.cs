using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.6 "Same-tick edge release" (Q-054): edge entry reads the S1
    /// snapshot, so an edge left during t is entered, and its hold released,
    /// at t + 1; a free edge goes to its queue head, else to the lowest
    /// FlightId asking for it that tick, and the others queue.
    /// </summary>
    public sealed class TaxiTests
    {
        [Fact]
        public void test_taxi_hold_released_the_tick_after_blocker_leaves_edge()
        {
            // D1 (rotation-less) pushes back from S1 at 3600 and is on E1
            // (J1 -> T) 3620-3650. A1 lands at 3630 and leaves the runway at
            // 3640, wanting E1. At the end of 3650 E1 is empty, yet A1 still
            // waits; it enters at 3651.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:03"), Csv.Row("D1", "D", "06:35")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(3651UL);
            AircraftTrack blocker = rig.Track(d1);
            Assert.False(blocker.OnEdge.HasValue, "D1 should have left E1 at 3650: " + Show.Track(blocker));
            Assert.Equal(FixtureLayout.Threshold, blocker.AtNode!.Value.Value);
            AircraftTrack waiting = rig.Track(a1);
            Assert.Equal(AircraftLegPhase.HeldOnTaxiway, waiting.Phase);
            Assert.False(waiting.OnEdge.HasValue);
            Assert.Empty(rig.Rec.Of<AircraftHeldOnTaxiwayReleased>(a1));

            rig.RunTo(3652UL);
            var released = rig.Rec.Of<AircraftHeldOnTaxiwayReleased>(a1);
            Assert.Single(released);
            Assert.Equal(3651UL, released[0].Rec.Tick);
            Assert.Equal(rig.Rec.Of<AircraftHeldOnTaxiway>(a1)[0].Rec.Id, released[0].Rec.Env.Cause.Id);
            AircraftTrack entered = rig.Track(a1);
            Assert.Equal(FixtureLayout.E1, entered.OnEdge!.Value.Value);
            Assert.Equal(3651UL, entered.PhaseEnteredAt);
            Assert.Equal(3681UL, entered.DueAt);
        }

        [Fact]
        public void test_taxi_free_edge_goes_to_lowest_flight_id_among_same_tick_askers()
        {
            // Two stands 20 ticks from junction J, one edge (1) from J to T.
            // D1 and D2 (rotation-less) are due on stand at 06:00, push back at
            // once (12 §12.8a S5 chains) and both reach J at 3620 asking for
            // edge 1, free in the snapshot: it goes to D1, the lower FlightId,
            // and D2 holds until D1 leaves it at 3650, so enters at 3651.
            AirsideLayout layout = new LayoutBuilder()
                .Runway(1, 1, 15, 10)
                .Node(1, TaxiNodeKind.RunwayThreshold).Node(2, TaxiNodeKind.Junction)
                .Node(11, TaxiNodeKind.StandPosition).Node(12, TaxiNodeKind.StandPosition)
                .Edge(1, 1, 2, 30).Edge(2, 2, 11, 20).Edge(3, 2, 12, 20)
                .Stand(1, 11, AirsideContent.Super, 901).Stand(2, 12, AirsideContent.Super, 902)
                .Load();
            var rig = new HostRig(Csv.Of(Csv.Row("D2", "D", "06:35"), Csv.Row("D1", "D", "06:35")), layout: layout);
            ulong d1 = rig.Id("D1");
            ulong d2 = rig.Id("D2");
            rig.RunTo(3700UL);

            Assert.Equal(AirConst.At(6, 0), rig.Rec.Milestone(d1, FlightMilestone.Pushback).Tick);
            Assert.Equal(AirConst.At(6, 0), rig.Rec.Milestone(d2, FlightMilestone.Pushback).Tick);
            Assert.Empty(rig.Rec.Of<AircraftHeldOnTaxiway>(d1));
            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<AircraftHeldOnTaxiway, AircraftHeldOnTaxiwayReleased>(rig.Rec, d2);
            Assert.Single(pairs);
            Assert.Equal(3620UL, pairs[0].Hold.Tick);
            Assert.Equal(1, ((AircraftHeldOnTaxiway)pairs[0].Hold.Payload).Edge.Value);
            Assert.Equal(3651UL, pairs[0].Release.Tick);
        }

        /// <summary>Two stands 20 ticks from junction J, one edge (1) from J to T.</summary>
        private static AirsideLayout TwoStands()
        {
            return new LayoutBuilder()
                .Runway(1, 1, 15, 10)
                .Node(1, TaxiNodeKind.RunwayThreshold).Node(2, TaxiNodeKind.Junction)
                .Node(11, TaxiNodeKind.StandPosition).Node(12, TaxiNodeKind.StandPosition)
                .Edge(1, 1, 2, 30).Edge(2, 2, 11, 20).Edge(3, 2, 12, 20)
                .Stand(1, 11, AirsideContent.Super, 901).Stand(2, 12, AirsideContent.Super, 902)
                .Load();
        }

        [Fact]
        public void test_taxi_hold_blocking_names_same_step_grantee_and_release_blocking_is_null()
        {
            // 12 §12.6 (Q-060): edge 1 is free in the 3620 snapshot; D1 and D2
            // both ask for it. D1 (lower FlightId) is granted it in this step,
            // so D2's hold names D1. The release carries null.
            var rig = new HostRig(Csv.Of(Csv.Row("D2", "D", "06:35"), Csv.Row("D1", "D", "06:35")), layout: TwoStands());
            ulong d1 = rig.Id("D1");
            ulong d2 = rig.Id("D2");
            rig.RunTo(3700UL);

            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<AircraftHeldOnTaxiway, AircraftHeldOnTaxiwayReleased>(rig.Rec, d2);
            Assert.Single(pairs);
            var hold = (AircraftHeldOnTaxiway)pairs[0].Hold.Payload;
            Assert.Equal(3620UL, pairs[0].Hold.Tick);
            Assert.True(hold.Blocking.HasValue, "Blocking is never null on the opening event");
            Assert.Equal(d1, hold.Blocking!.Value.Value);
            var release = (AircraftHeldOnTaxiwayReleased)pairs[0].Release.Payload;
            Assert.Equal(1, release.Edge.Value);
            Assert.False(release.Blocking.HasValue, "Blocking is null on the release (10 §10.6)");
        }

        [Fact]
        public void test_taxi_hold_blocking_names_snapshot_occupant_that_left_this_tick()
        {
            // 12 §12.6 (Q-060): D1 (rotation-less) is on E1 (J1 -> T) from 3620
            // and reaches T at 3650, leaving E1 in S6.1. A1 lands at 3640 and
            // leaves the runway at 3650, asking for E1 in S6.2 of the same
            // tick: E1 is occupied in the snapshot, so A1 holds, naming D1,
            // and enters at 3651.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:04"), Csv.Row("D1", "D", "06:35")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(3700UL);

            Assert.Equal(3650UL, rig.Rec.Milestone(a1, FlightMilestone.OffRunway).Tick);
            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<AircraftHeldOnTaxiway, AircraftHeldOnTaxiwayReleased>(rig.Rec, a1);
            Assert.Single(pairs);
            var hold = (AircraftHeldOnTaxiway)pairs[0].Hold.Payload;
            Assert.Equal(3650UL, pairs[0].Hold.Tick);
            Assert.Equal(FixtureLayout.E1, hold.Edge.Value);
            Assert.Equal(d1, hold.Blocking!.Value.Value);
            Assert.Equal(3651UL, pairs[0].Release.Tick);
            Assert.False(((AircraftHeldOnTaxiwayReleased)pairs[0].Release.Payload).Blocking.HasValue);
        }
    }
}
