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
    }
}
