using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.7 "Rotation-less flights": a rotation-less arrival completes
    /// its leg and stays on stand for the rest of the run; a rotation-less
    /// departure is created in OnStand at ScheduledTick - MinTurnaround on the
    /// earliest compatible free stand, and under the fallback its doors close
    /// the instant it is created (the §12.7 LOW CONFIDENCE note).
    /// </summary>
    public sealed class RotationlessTests
    {
        [Fact]
        public void test_rotationless_arrival_stays_on_stand_indefinitely()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("X1", "A", "06:00")));
            ulong x1 = rig.Id("X1");
            ulong onStand = AirConst.At(6, 0) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(FixtureLayout.S1);
            rig.StepEach(2UL * AirConst.TicksPerDay, t =>
            {
                if (t < onStand)
                {
                    return;
                }

                FlightId? o = rig.Occupant(FixtureLayout.S1);
                Assert.True(o.HasValue && o.Value.Value == x1, "t=" + t.ToString(CultureInfo.InvariantCulture) + ": S1 no longer held by the rotation-less arrival");
                Assert.DoesNotContain(FixtureLayout.S1, rig.Free());
            });

            Assert.Equal(onStand, rig.Rec.Milestone(x1, FlightMilestone.OnStand).Milestone.ActualTick);
            AircraftTrack tr = rig.Track(x1);
            Assert.Equal(AircraftLegPhase.OnStand, tr.Phase);
            Assert.Equal(FixtureLayout.S1, tr.Stand!.Value.Value);
            Assert.Equal(new[] { new FlightId(x1) }, rig.Airside.TrackedFlights());

            var got = rig.Rec.Milestones(x1).ConvertAll(r => r.Milestone.Milestone);
            Assert.Equal(
                new[] { FlightMilestone.InboundAirborne, FlightMilestone.Landed, FlightMilestone.OffRunway, FlightMilestone.OnStand, FlightMilestone.DoorsOpen },
                got.ToArray());

            // No departure track is ever created: only X1's milestones exist.
            Assert.All(rig.Rec.All, r => Assert.True(!r.FromAirside || r.Flight == x1, "unexpected event: " + r));
        }

        [Fact]
        public void test_rotationless_departure_is_created_on_stand_at_std_minus_min_turnaround()
        {
            // X1 holds S1 for good, so D1 (a320) must take S2, the next compatible free stand.
            var rig = new HostRig(Csv.Of(Csv.Row("D1", "D", "08:00"), Csv.Row("X1", "A", "06:00")));
            ulong d1 = rig.Id("D1");
            ulong created = AirConst.At(8, 0) - (35UL * AirConst.TicksPerMinute);
            rig.StepEach(created, t => Assert.False(rig.Airside.TryGetTrack(new FlightId(d1), out _), "departure tracked before STD - MinTurnaround"));
            rig.RunTo(AirConst.TicksPerDay);

            Rec onStand = rig.Rec.Milestone(d1, FlightMilestone.OnStand);
            Assert.Equal(created, onStand.Milestone.PlannedTick);
            Assert.Equal(created, onStand.Milestone.ActualTick);
            Assert.Equal(created, rig.Rec.Milestone(d1, FlightMilestone.DoorsClosed).Milestone.ActualTick);

            Rec pushback = rig.Rec.Milestone(d1, FlightMilestone.Pushback);
            Assert.Equal(created, pushback.Milestone.ActualTick);
            Assert.True(pushback.HasTrack, "departure untracked at Pushback");
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S2), pushback.Track.AtNode!.Value.Value);

            Assert.Equal(AirConst.At(8, 0) + FixtureLayout.RouteTicks(FixtureLayout.S2), rig.Rec.Milestone(d1, FlightMilestone.TakeoffRoll).Milestone.PlannedTick);
            Assert.Empty(rig.Rec.Of<StandUnavailable>(d1));
        }

        [Fact]
        public void test_rotationless_departure_waits_for_stand_with_no_track_and_fires_late_on_stand()
        {
            // 12 §12.7 "No stand free" (Q-053). X1-X3 hold S2-S4 for good;
            // R_A holds S1 until R_D pushes back. D9 (a320) is due at 07:00,
            // with no stand free: no track, no event, until S1 is assignable,
            // the tick after R_D's Pushback. Then OnStand fires late, with
            // PlannedTick still the due tick, and the fallback chain runs at once.
            var rows = new List<string>
            {
                Csv.Row("X1", "A", "06:00", aircraft: "a388"),
                Csv.Row("X2", "A", "06:10", aircraft: "a359"),
                Csv.Row("X3", "A", "06:20", aircraft: "a388"),
                Csv.Row("D9", "D", "07:35"),
            };
            rows.AddRange(Csv.Pair("R_A", "R_D", "06:30", "08:00"));
            ScriptedFlow flow = ScriptedFlow.None();
            var rig = new HostRig(Csv.Of(rows.ToArray()), flow: flow);
            ulong d9 = rig.Id("D9");
            ulong due = AirConst.At(7, 0);

            rig.RunTo(due + 1UL);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(d9), out _), "a waiting rotation-less departure has no track");
            Assert.DoesNotContain(new FlightId(d9), rig.Airside.TrackedFlights());

            rig.RunTo(AirConst.TicksPerDay);
            ulong pushback = rig.Rec.Milestone(rig.Id("R_D"), FlightMilestone.Pushback).Tick;
            Assert.True(pushback > due, "fixture assumption: S1 is still held at D9's due tick");
            ulong got = pushback + 1UL;

            List<Rec> mine = rig.Rec.All.FindAll(r => r.Flight == d9);
            Assert.NotEmpty(mine);
            Assert.All(mine, r => Assert.True(r.Tick >= got, "event before D9 got a stand: " + r));
            Assert.Empty(rig.Rec.Of<StandUnavailable>(d9));

            Rec onStand = rig.Rec.Milestone(d9, FlightMilestone.OnStand);
            Assert.Equal(due, onStand.Milestone.PlannedTick);
            Assert.Equal(got, onStand.Milestone.ActualTick);
            Assert.Equal(got, rig.Rec.Milestone(d9, FlightMilestone.DoorsClosed).Tick);
            Rec push = rig.Rec.Milestone(d9, FlightMilestone.Pushback);
            Assert.Equal(got, push.Tick);
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S1), push.Track.AtNode!.Value.Value);

            var absorbs = flow.AbsorbsOf(d9);
            Assert.Single(absorbs);
            Assert.Equal(got, absorbs[0].Tick);
            Assert.Equal(FixtureLayout.Sink(FixtureLayout.S1), absorbs[0].Sink);
        }
    }
}
