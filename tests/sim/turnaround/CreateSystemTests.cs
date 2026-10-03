using System;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.10a (Q-088): CreateSystem runs 13 §13.4's five checks on a setup
    /// built in code, throwing ArgumentException naming the same JobKind (file
    /// spelling) or VehicleId (decimal) that Load would.
    /// </summary>
    public sealed class CreateSystemTests
    {
        private static ArgumentException Rejects(JobDef[] jobs, VehicleDef[] fleet)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, TurnContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(b.Services, ScheduleFactory.CreateLoader().Load(Csv.Of(Csv.Row("D1", "D", "06:00")), ScheduleFixture.SourceName), null);
            return Assert.ThrowsAny<ArgumentException>(() => TurnaroundFactory.CreateSystem(b.Services, Setups.Of(jobs, fleet), schedule));
        }

        private static JobDef[] Replace(JobDef[] jobs, JobDef with)
        {
            var copy = (JobDef[])jobs.Clone();
            for (int i = 0; i < copy.Length; i++)
            {
                if (copy[i].Kind == with.Kind)
                {
                    copy[i] = with;
                }
            }

            return copy;
        }

        [Fact]
        public void test_create_system_rejects_wrong_required_vehicle()
        {
            JobDef[] jobs = Replace(Phase1Fixture.Jobs(), new JobDef(JobKind.PushbackPrep, VehicleKind.BaggageTractor, 20U, DelayCategory.GroundHandling));
            ArgumentException e = Rejects(jobs, Phase1Fixture.Fleet());
            Assert.Contains("pushback_prep", e.Message, StringComparison.Ordinal);

            // A vehicle on a job the table says has none.
            e = Rejects(Replace(Phase1Fixture.Jobs(), new JobDef(JobKind.Deboard, VehicleKind.CleaningCrew, 60U, DelayCategory.GroundHandling)), Phase1Fixture.Fleet());
            Assert.Contains("deboard", e.Message, StringComparison.Ordinal);

            // None on a job that needs one.
            e = Rejects(Replace(Phase1Fixture.Jobs(), new JobDef(JobKind.Fuel, null, 120U, DelayCategory.Fuel)), Phase1Fixture.Fleet());
            Assert.Contains("fuel", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_create_system_rejects_zero_or_duplicate_vehicle_id()
        {
            // Check 5: an id of 0 is reported before any duplicate; a duplicate names the lowest duplicated id.
            ArgumentException e = Rejects(Phase1Fixture.Jobs(), Setups.Fleet((83, VehicleKind.FuelTruck), (83, VehicleKind.CleaningCrew), (47, VehicleKind.PushbackTug), (47, VehicleKind.BaggageTractor)));
            Assert.Contains("47", e.Message, StringComparison.Ordinal);
            e = Rejects(Phase1Fixture.Jobs(), Setups.Fleet((9, VehicleKind.FuelTruck), (9, VehicleKind.CleaningCrew), (0, VehicleKind.PushbackTug)));
            Assert.Contains("0", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_create_system_rejects_zero_duration_like_load()
        {
            ArgumentException e = Rejects(Replace(Phase1Fixture.Jobs(), new JobDef(JobKind.Catering, VehicleKind.CateringTruck, 0U, DelayCategory.Catering)), Phase1Fixture.Fleet());
            Assert.Contains("catering", e.Message, StringComparison.Ordinal);
        }
    }
}
