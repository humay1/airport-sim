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
    }
}
