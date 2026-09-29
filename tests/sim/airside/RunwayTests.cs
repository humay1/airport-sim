using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.5, the runway model: pacing (minSeparationTicks =
    /// ceil(TICKS_PER_SIM_HOUR / DeclaredCapacityPerHour), tracked by
    /// NextSlotTick, shared by arrivals and departures) and occupancy, holds
    /// queued by ascending EventId of the hold request, released at the head
    /// the first tick both checks pass. All flights here are rotation-less,
    /// and none of them competes for a stand or a taxi edge before landing.
    /// </summary>
    public sealed class RunwayTests
    {
        private static readonly RunwayId R1 = new RunwayId(FixtureLayout.Runway);

        [Fact]
        public void test_runway_pacing_holds_when_declared_capacity_exceeded()
        {
            // 15 per hour: 40 ticks between slots. A2 is due 10 ticks after A1.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:01")));
            ulong a1 = rig.Id("A1");
            ulong a2 = rig.Id("A2");
            ulong sta1 = AirConst.At(6, 0);
            ulong sta2 = AirConst.At(6, 1);
            ulong slot2 = sta1 + AirConst.MinSeparation(FixtureLayout.CapacityPerHour);
            Assert.Equal(3640UL, slot2);

            var queue = new Dictionary<ulong, int>();
            var tracks = new Dictionary<ulong, AircraftTrack>();
            rig.StepEach(3700UL, t =>
            {
                queue[t] = rig.Airside.RunwayQueueLength(R1);
                if (rig.Airside.TryGetTrack(new FlightId(a2), out AircraftTrack tr))
                {
                    tracks[t] = tr;
                }
            });

            Rec landed1 = rig.Rec.Milestone(a1, FlightMilestone.Landed);
            Assert.Equal(sta1, landed1.Milestone.ActualTick);
            Assert.Equal(sta1, landed1.Milestone.PlannedTick);

            Rec landed2 = rig.Rec.Milestone(a2, FlightMilestone.Landed);
            Assert.Equal(sta2, landed2.Milestone.PlannedTick);
            Assert.NotEmpty(rig.Rec.Of<AircraftHeldForRunway>(a2));
            AirsideAsserts.RunwayReleasedInto(rig.Rec, a2, FlightMilestone.Landed, slot2);

            for (ulong t = sta2; t < slot2; t++)
            {
                Assert.True(queue[t] == 1, "RunwayQueueLength at t=" + t.ToString(CultureInfo.InvariantCulture) + " was " + queue[t].ToString(CultureInfo.InvariantCulture));
                AircraftTrack tr = tracks[t];
                Assert.Equal(AircraftLegPhase.HeldForRunway, tr.Phase);
                Assert.False(tr.AtNode.HasValue, "an arrival held before Landed is off-graph (12 §12.9): " + Show.Track(tr));
                Assert.False(tr.OnEdge.HasValue);
            }

            Assert.Equal(0, queue[slot2]);
            Assert.Equal(AircraftLegPhase.OnRunway, tracks[slot2].Phase);
        }

        [Fact]
        public void test_runway_pacing_min_separation_rounds_up()
        {
            // 600 / 16 = 37.5 ticks, RUNWAY_SLOT_ROUNDING = ceiling: 38.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:01")), layout: FixtureLayout.Layout(16));
            rig.RunTo(3700UL);
            Assert.Equal(38UL, AirConst.MinSeparation(16));
            Assert.Equal(AirConst.At(6, 0), rig.Rec.Milestone(rig.Id("A1"), FlightMilestone.Landed).Milestone.ActualTick);
            AirsideAsserts.RunwayReleasedInto(rig.Rec, rig.Id("A2"), FlightMilestone.Landed, AirConst.At(6, 0) + 38UL);
        }

        [Fact]
        public void test_runway_hold_queue_releases_in_order_of_hold_request()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:01"), Csv.Row("A3", "A", "06:02")));
            var queue = new Dictionary<ulong, int>();
            rig.StepEach(3800UL, t => queue[t] = rig.Airside.RunwayQueueLength(R1));

            Assert.Equal(3600UL, rig.Rec.Milestone(rig.Id("A1"), FlightMilestone.Landed).Milestone.ActualTick);
            AirsideAsserts.RunwayReleasedInto(rig.Rec, rig.Id("A2"), FlightMilestone.Landed, 3640UL);
            AirsideAsserts.RunwayReleasedInto(rig.Rec, rig.Id("A3"), FlightMilestone.Landed, 3680UL);
            for (ulong t = 3620UL; t < 3640UL; t++)
            {
                Assert.Equal(2, queue[t]);
            }

            for (ulong t = 3640UL; t < 3680UL; t++)
            {
                Assert.Equal(1, queue[t]);
            }

            Assert.Equal(0, queue[3680UL]);
        }

        [Fact]
        public void test_runway_pacing_shared_by_arrivals_and_departures()
        {
            // D1 (rotation-less) is created on stand S1 at STD - MinTurnaround =
            // 06:00 and, under the fallback, closes its doors and pushes back
            // at once (12 §12.7). It reaches the threshold at 3650 (S1 -> T is
            // 50 ticks). A1 lands at 3630 and claims the slot, so D1 may not
            // take off before 3670: arrivals and departures draw from the same
            // slot sequence.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:03"), Csv.Row("D1", "D", "06:35")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(3800UL);

            Assert.Equal(3630UL, rig.Rec.Milestone(a1, FlightMilestone.Landed).Milestone.ActualTick);
            Assert.Equal(3600UL, rig.Rec.Milestone(d1, FlightMilestone.Pushback).Milestone.ActualTick);
            Assert.NotEmpty(rig.Rec.Of<AircraftHeldForRunway>(d1));
            AirsideAsserts.RunwayReleasedInto(rig.Rec, d1, FlightMilestone.TakeoffRoll, 3670UL);

            ulong std = AirConst.At(6, 35);
            Rec roll = rig.Rec.Milestone(d1, FlightMilestone.TakeoffRoll);
            Assert.Equal(std + FixtureLayout.RouteTicks(FixtureLayout.S1), roll.Milestone.PlannedTick);
            Rec airborne = rig.Rec.Milestone(d1, FlightMilestone.Airborne);
            Assert.Equal(3670UL + FixtureLayout.OccupancyTicks, airborne.Milestone.ActualTick);
            Assert.Equal(roll.Milestone.PlannedTick + FixtureLayout.OccupancyTicks, airborne.Milestone.PlannedTick);
        }

        [Fact]
        public void test_runway_occupancy_spans_occupancy_ticks_per_movement()
        {
            // 12 §12.6: OffRunway fires OccupancyTicks after Landed, Airborne
            // OccupancyTicks after TakeoffRoll.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("D1", "D", "07:35")));
            rig.RunTo(5000UL);
            ulong landed = rig.Rec.Milestone(rig.Id("A1"), FlightMilestone.Landed).Milestone.ActualTick;
            Assert.Equal(landed + FixtureLayout.OccupancyTicks, rig.Rec.Milestone(rig.Id("A1"), FlightMilestone.OffRunway).Milestone.ActualTick);
            ulong roll = rig.Rec.Milestone(rig.Id("D1"), FlightMilestone.TakeoffRoll).Milestone.ActualTick;
            Assert.Equal(roll + FixtureLayout.OccupancyTicks, rig.Rec.Milestone(rig.Id("D1"), FlightMilestone.Airborne).Milestone.ActualTick);
        }
    }
}
