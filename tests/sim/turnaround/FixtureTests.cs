using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.11's requirements on the Phase 0/1 setup: five vehicles, all
    /// eight JobKinds, and durations short enough that an uncontended rotation
    /// completes within its scheduled ground time. The contended half of the
    /// requirement is asserted by the headless day (TurnaroundJobBlocked fires).
    /// </summary>
    public sealed class FixtureTests
    {
        /// <summary>13 §13.4's table (Q-088).</summary>
        private static readonly VehicleKind?[] Required =
        {
            null, VehicleKind.BaggageTractor, VehicleKind.CleaningCrew, VehicleKind.CateringTruck,
            VehicleKind.FuelTruck, VehicleKind.BaggageTractor, VehicleKind.PushbackTug, null,
        };

        [Fact]
        public void test_fixture_file_loads_with_one_vehicle_per_kind()
        {
            // 13 §13.11 (Q-086): Load on phase1-five-vehicles.json returns eight
            // jobs in ascending JobKind, each with 13 §13.4's RequiresVehicle,
            // and five vehicles, one per kind, ids 1 to 5 ascending. It equals
            // the built copy the rigs use.
            TurnaroundSetup s = TurnaroundFactory.CreateSetupLoader().Load(Phase1Fixture.Bytes(), Phase1Fixture.SourceName);
            JobDef[] built = Phase1Fixture.Jobs();
            Assert.Equal(8, s.Catalogue.Jobs.Count);
            for (int i = 0; i < 8; i++)
            {
                JobDef j = s.Catalogue.Jobs[i];
                Assert.Equal((JobKind)i, j.Kind);
                Assert.Equal(Required[i], j.RequiresVehicle);
                Assert.Equal(built[i].NominalDurationTicks, j.NominalDurationTicks);
                Assert.Equal(built[i].Category, j.Category);
                Assert.Equal(built[i].RequiresVehicle, j.RequiresVehicle);
            }

            Assert.Equal(5, s.Fleet.Vehicles.Count);
            var kinds = new HashSet<VehicleKind>();
            for (int i = 0; i < 5; i++)
            {
                VehicleDef v = s.Fleet.Vehicles[i];
                Assert.Equal((ushort)(i + 1), v.Id.Value);
                Assert.Equal(Phase1Fixture.Fleet()[i].Kind, v.Kind);
                Assert.True(kinds.Add(v.Kind), "two vehicles of kind " + v.Kind);
            }
        }

        [Fact]
        public void test_fixture_lone_rotation_completes_within_its_ground_time()
        {
            JobDef[] jobs = Phase1Fixture.Jobs();
            VehicleDef[] fleet = Phase1Fixture.Fleet();
            Assert.Equal(5, fleet.Length);
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
