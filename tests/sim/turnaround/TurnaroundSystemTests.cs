using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>13 §13.7 and 08 §8.5: identity, registry position and the queries before any flight is on stand.</summary>
    public sealed class TurnaroundSystemTests
    {
        [Fact]
        public void test_turnaround_system_is_registry_position_5_named_after_its_module()
        {
            var rig = new Rig(Csv.Of(Csv.Row("D1", "D", "06:00")), Setups.Unit(Setups.Plenty(1)));
            Assert.Equal((ushort)5, rig.Turnaround.Id.Value);
            Assert.Equal("sim.turnaround", rig.Turnaround.Name);
        }

        [Fact]
        public void test_turnaround_system_queries_before_any_on_stand_show_the_idle_fleet()
        {
            VehicleDef[] fleet = Setups.Fleet((9, VehicleKind.FuelTruck), (2, VehicleKind.FuelTruck), (4, VehicleKind.PushbackTug), (30, VehicleKind.CleaningCrew));
            var rig = new Rig(Csv.Of(Csv.Row("D1", "D", "06:00")), Setups.Unit(fleet), onStand: new[] { ("D1", 500UL) });
            rig.RunTo(100UL);

            Assert.Equal(new ushort[] { 2, 9 }, rig.Free(VehicleKind.FuelTruck));
            Assert.Equal(new ushort[] { 4 }, rig.Free(VehicleKind.PushbackTug));
            Assert.Equal(new ushort[] { 30 }, rig.Free(VehicleKind.CleaningCrew));
            Assert.Empty(rig.Free(VehicleKind.CateringTruck));
            Assert.Empty(rig.Free(VehicleKind.BaggageTractor));

            foreach (VehicleDef d in fleet)
            {
                Assert.True(rig.Turnaround.TryGetVehicle(d.Id, out VehicleState v));
                Assert.Equal(d.Id, v.Id);
                Assert.Equal(d.Kind, v.Kind);
                Assert.Null(v.Assignment);
            }

            Assert.False(rig.Turnaround.TryGetVehicle(new VehicleId(3), out _));
            Assert.Empty(rig.Turnaround.JobsForFlight(new FlightId(rig.Id("D1"))));
            Assert.False(rig.Turnaround.TryGetJob(TConst.Job(rig.Id("D1"), JobKind.Fuel), out _));
            Assert.Empty(rig.Turnaround.JobsForFlight(new FlightId(424242UL)));
        }

        [Fact]
        public void test_turnaround_system_jobs_for_flight_lists_derived_ids_in_ascending_job_kind()
        {
            // 13 §13.3: JobId.Value = (Flight.Value << 8) | Kind; 13 §13.7: ascending JobKind.
            var rig = new Rig(
                Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("D1", "D", "07:00")),
                Setups.Unit(Setups.Plenty(1)),
                onStand: new[] { ("A1", 3700UL), ("D1", 3800UL) });
            rig.RunThrough(3800UL);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");

            var arrival = new List<JobId>(rig.Turnaround.JobsForFlight(new FlightId(a1)));
            Assert.Equal(new[] { new JobId((a1 << 8) | 0UL), new JobId((a1 << 8) | 1UL) }, arrival);

            var departure = new List<JobId>(rig.Turnaround.JobsForFlight(new FlightId(d1)));
            var expected = new List<JobId>();
            for (ulong k = 2UL; k <= 7UL; k++)
            {
                expected.Add(new JobId((d1 << 8) | k));
            }

            Assert.Equal(expected, departure);

            foreach (JobId id in departure)
            {
                Assert.True(rig.Turnaround.TryGetJob(id, out TurnaroundJob j));
                Assert.Equal(id, j.Id);
                Assert.Equal(d1, j.Flight.Value);
                Assert.Equal((ulong)(int)j.Kind, id.Value & 0xFFUL);
                Assert.Equal(3800UL, j.CreatedAt);
            }

            // An arrival never gets departure jobs, nor a departure arrival ones.
            Assert.False(rig.Turnaround.TryGetJob(TConst.Job(a1, JobKind.Boarding), out _));
            Assert.False(rig.Turnaround.TryGetJob(TConst.Job(d1, JobKind.Deboard), out _));
        }
    }
}
