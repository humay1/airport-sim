using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.6 "PlannedTick" and 10 §10.4: sim.turnaround's three milestones
    /// carry schedule-anchored, cumulative plans that an actual lateness never
    /// shifts, are emitted by sim.turnaround, and only these three.
    /// </summary>
    public sealed class MilestoneTests
    {
        [Fact]
        public void test_milestone_planned_ticks_are_schedule_anchored_not_shifted_by_lateness()
        {
            // A1: STA 06:00 = 3600, on stand 100 late at 3700. Deboard 40.
            // D1: STD 07:00 = 4200, MinTurnaround 35 min = 350, planned OnStand
            // 3850, on stand 50 late at 3900. Unit prep max is Fuel, 80;
            // Boarding 100. A second departure, D0, holds the only fuel truck
            // first, so D1 is later still at ReadyToBoard: none of it moves a plan.
            var rig = new Rig(
                Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("D0", "D", "06:30"), Csv.Row("D1", "D", "07:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.FuelTruck, 1))),
                onStand: new[] { ("A1", 3700UL), ("D0", 3850UL), ("D1", 3900UL) });
            rig.RunTo(5000UL);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");

            Rec deboard = Assert.Single(rig.Rec.Milestones(FlightMilestone.DeboardComplete, a1));
            Assert.Equal(3600UL + 40UL, deboard.Milestone.PlannedTick);
            Assert.Equal(3740UL, deboard.Milestone.ActualTick);

            // D0's Fuel holds the truck 3850-3930, so D1's runs 3930-4010.
            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard, d1));
            Assert.Equal(3850UL + 80UL, ready.Milestone.PlannedTick);
            Assert.Equal(4010UL, ready.Milestone.ActualTick);

            Rec boarded = Assert.Single(rig.Rec.Milestones(FlightMilestone.BoardingComplete, d1));
            Assert.Equal(3850UL + 80UL + 100UL, boarded.Milestone.PlannedTick);
            Assert.Equal(4110UL, boarded.Milestone.ActualTick);
        }

        [Fact]
        public void test_milestone_ready_to_board_plan_uses_largest_prerequisite_duration()
        {
            // Change which prerequisite is longest: CabinClean 150 now dominates.
            JobDef[] jobs = Setups.UnitJobs();
            for (int i = 0; i < jobs.Length; i++)
            {
                if (jobs[i].Kind == JobKind.CabinClean)
                {
                    jobs[i] = new JobDef(JobKind.CabinClean, VehicleKind.CleaningCrew, 150U, DelayCategory.Cleaning);
                }
            }

            var rig = new Rig(Csv.Of(Csv.Row("D1", "D", "07:00")), Setups.Of(jobs, Setups.Plenty()), onStand: new[] { ("D1", 3850UL) });
            rig.RunTo(5000UL);
            ulong d1 = rig.Id("D1");
            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard, d1));
            Assert.Equal(3850UL + 150UL, ready.Milestone.PlannedTick);
            Assert.Equal(4000UL, ready.Milestone.ActualTick);
            Rec boarded = Assert.Single(rig.Rec.Milestones(FlightMilestone.BoardingComplete, d1));
            Assert.Equal(3850UL + 150UL + 100UL, boarded.Milestone.PlannedTick);
        }

        [Fact]
        public void test_milestone_turnaround_emits_only_its_own_three()
        {
            var rig = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1);
            rig.RunTo(TConst.TicksPerDay);
            Assert.NotEmpty(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            foreach (Rec r in rig.Rec.All)
            {
                if (r.FromTurnaround && r.Payload is FlightMilestoneReached m)
                {
                    Assert.True(
                        m.Milestone == FlightMilestone.DeboardComplete || m.Milestone == FlightMilestone.ReadyToBoard || m.Milestone == FlightMilestone.BoardingComplete,
                        "sim.turnaround emitted " + r);
                }
            }
        }
    }
}
