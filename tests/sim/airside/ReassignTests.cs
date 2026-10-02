using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.9 and §12.10 (Q-081, Q-083; HUMAN DECISION, owner: ReassignStand
    /// is open at any time, the door delay included). ReassignStand touches
    /// only the track's Stand and AtNode, and the two stands' Occupant and
    /// VacatedBy. Phase, PhaseEnteredAt, DueAt and PlannedOnStand are
    /// unchanged, so DoorsOpen keeps its tick and its PlannedTick
    /// (PlannedOnStand + the delay). A departure's TakeoffRoll plan is formed
    /// at Pushback, from the stand it pushes back from.
    /// </summary>
    public sealed class ReassignTests
    {
        [Fact]
        public void test_reassign_before_doors_open_keeps_planned_tick_and_moves_at_node()
        {
            // Arrival: X1 reaches S1 (route 50) at 3660, planned 3660; its doors
            // are due at 3680. Moved to S4 (route 65) at the 3670 boundary.
            var rig = new HostRig(Csv.Of(Csv.Row("X1", "A", "06:00")));
            ulong x1 = rig.Id("X1");
            ulong onStand = AirConst.At(6, 0) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(FixtureLayout.S1);
            ulong doorsOpen = onStand + AirConst.FixtureDoorDelayTicks;
            Assert.NotEqual(FixtureLayout.RouteTicks(FixtureLayout.S1), FixtureLayout.RouteTicks(FixtureLayout.S4));
            Assert.True(rig.Submit(Payload.ReassignCommand(onStand + 10UL, x1, FixtureLayout.S4), out CommandRejection reason));
            Assert.Equal(CommandRejection.None, reason);

            rig.RunTo(onStand + 10UL);
            AircraftTrack before = rig.Track(x1);
            Assert.Equal(onStand, before.PlannedOnStand);
            rig.RunTo(onStand + 11UL);
            AircraftTrack moved = rig.Track(x1);
            Assert.Equal(FixtureLayout.S4, moved.Stand!.Value.Value);
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S4), moved.AtNode!.Value.Value);
            Assert.Equal(AircraftLegPhase.OnStand, moved.Phase);
            Assert.Equal(before.PhaseEnteredAt, moved.PhaseEnteredAt);
            Assert.Equal(before.DueAt, moved.DueAt);
            Assert.Equal(doorsOpen, moved.DueAt);
            Assert.Equal(before.PlannedOnStand, moved.PlannedOnStand);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S4)!.Value.Value);
            Assert.False(rig.Occupant(FixtureLayout.S1).HasValue);

            rig.RunTo(doorsOpen + 1UL);
            Rec open = rig.Rec.Milestone(x1, FlightMilestone.DoorsOpen);
            Assert.Equal(doorsOpen, open.Tick);
            Assert.Equal(onStand + AirConst.FixtureDoorDelayTicks, open.Milestone.PlannedTick);

            // Departure: D1 (rotation-less) is created on S1 at 07:25 (4450) and
            // held 100 ticks for its passengers, so it stays OnStand until 4550.
            // Moved to S4 at the 4500 boundary, it pushes back from S4's node,
            // and its TakeoffRoll is planned over S4's route.
            var dep = new HostRig(Csv.Of(Csv.Row("D1", "D", "08:00")), flow: ScriptedFlow.For(1UL, 100UL, 3, 77U));
            ulong d1 = dep.Id("D1");
            Assert.Equal(1UL, d1);
            ulong created = AirConst.At(8, 0) - (35UL * AirConst.TicksPerMinute);
            Assert.True(dep.Submit(Payload.ReassignCommand(created + 50UL, d1, FixtureLayout.S4), out _));
            dep.RunTo(created + 50UL);
            Assert.Equal(FixtureLayout.S1, dep.Track(d1).Stand!.Value.Value);
            Assert.Equal(AircraftLegPhase.OnStand, dep.Track(d1).Phase);
            dep.RunTo(created + 51UL);
            AircraftTrack depMoved = dep.Track(d1);
            Assert.Equal(FixtureLayout.S4, depMoved.Stand!.Value.Value);
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S4), depMoved.AtNode!.Value.Value);
            Assert.Equal(AirConst.TickUnscheduled, depMoved.PlannedOnStand);

            dep.RunTo(AirConst.TicksPerDay);
            Rec push = dep.Rec.Milestone(d1, FlightMilestone.Pushback);
            Assert.Equal(created + 100UL, push.Tick);
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S4), push.Track.AtNode!.Value.Value);
            Rec roll = dep.Rec.Milestone(d1, FlightMilestone.TakeoffRoll);
            Assert.Equal(AirConst.At(8, 0) + FixtureLayout.RouteTicks(FixtureLayout.S4), roll.Milestone.PlannedTick);
            Assert.Equal(push.Tick + FixtureLayout.RouteTicks(FixtureLayout.S4), roll.Milestone.ActualTick);
        }
    }
}
