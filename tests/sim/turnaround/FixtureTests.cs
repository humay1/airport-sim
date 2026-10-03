using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.11's requirements on the Phase 0/1 setup: four vehicles, all
    /// eight JobKinds, and durations short enough that an uncontended rotation
    /// completes within its scheduled ground time. The contended half of the
    /// requirement is asserted by the headless day (TurnaroundJobBlocked fires).
    /// </summary>
    public sealed class FixtureTests
    {
        [Fact]
        public void test_fixture_lone_rotation_completes_within_its_ground_time()
        {
            JobDef[] jobs = Phase1Fixture.Jobs();
            VehicleDef[] fleet = Phase1Fixture.Fleet();
            Assert.Equal(4, fleet.Length);
            var kinds = new HashSet<JobKind>();
            foreach (JobDef j in jobs)
            {
                Assert.True(kinds.Add(j.Kind), "duplicate " + j.Kind);
                Assert.True(j.NominalDurationTicks >= 1U, "zero duration for " + j.Kind);
            }

            Assert.Equal(8, kinds.Count);
            Assert.Equal(DelayCategory.GroundHandling, Setups.Category(jobs, JobKind.PushbackPrep));

            // The schedule fixture's tightest ground time: MinTurnaround 25 min.
            // The arrival reaches the stand at STA + 50; the departure at its
            // planned OnStand, STD − 250.
            byte[] csv = Csv.Of(Csv.Row("A1", "A", "06:00", "D1", "25"), Csv.Row("D1", "D", "07:00", "A1", "25"));
            var rig = new Rig(csv, Setups.Of(jobs, fleet), onStand: new[] { ("A1", 3650UL), ("D1", 4200UL - 250UL) });
            rig.RunTo(5000UL);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");

            Rec deboard = Assert.Single(rig.Rec.Milestones(FlightMilestone.DeboardComplete, a1));
            Assert.Equal(3650UL + 60UL, deboard.Tick);
            Assert.Equal(JobStatus.Completed, rig.Job(a1, JobKind.BaggageUnload).Status);

            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard, d1));
            Assert.Equal(3950UL + Phase1Fixture.UnimpededReadyToBoardTicks, ready.Tick);
            Rec boarded = Assert.Single(rig.Rec.Milestones(FlightMilestone.BoardingComplete, d1));
            Assert.Equal(3950UL + Phase1Fixture.UnimpededBoardingCompleteTicks, boarded.Tick);
            Assert.True(boarded.Tick <= 4200UL, "BoardingComplete after STD");
            Assert.Equal(boarded.Milestone.PlannedTick, boarded.Milestone.ActualTick);
            foreach (JobKind k in TConst.DepartureJobs)
            {
                Assert.Equal(JobStatus.Completed, rig.Job(d1, k).Status);
            }
        }
    }
}
