using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.5 step 3: free vehicles go to the Blocked jobs with the lowest
    /// EventId of their Blocked request, across every flight in turnaround,
    /// never by FlightId, JobId or distance; ties in which vehicle to hand out
    /// go to the ascending VehicleId.
    /// </summary>
    public sealed class VehicleDispatchTests
    {
        // Plenty(4, FuelTruck: 1): CleaningCrew 1-4, CateringTruck 5-8,
        // FuelTruck 9, BaggageTractor 10-13, PushbackTug 14-17.
        private const ushort OnlyFuelTruck = 9;

        private static byte[] ThreeDepartures()
        {
            return Csv.Of(Csv.Row("D1", "D", "06:00"), Csv.Row("D2", "D", "06:00"), Csv.Row("D3", "D", "06:00"));
        }

        [Fact]
        public void test_vehicle_dispatch_assigns_lowest_event_id_first()
        {
            // D2 takes the only fuel truck at 10 (Fuel: 80 ticks, due 90). D3
            // blocks on it at 20, D1 at 30. D1 has the lower FlightId and JobId,
            // D3 the lower EventId of its Blocked request: D3 is served first.
            var rig = new Rig(
                ThreeDepartures(),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.FuelTruck, 1))),
                onStand: new[] { ("D2", 10UL), ("D3", 20UL), ("D1", 30UL) });
            ulong d1 = rig.Id("D1");
            ulong d2 = rig.Id("D2");
            ulong d3 = rig.Id("D3");
            Assert.True(d1 < d3, "fixture: D1 must have the lower FlightId");

            rig.RunThrough(30UL);
            TurnaroundJob fuel2 = rig.Job(d2, JobKind.Fuel);
            Assert.Equal(JobStatus.Active, fuel2.Status);
            Assert.Equal(OnlyFuelTruck, fuel2.Vehicle!.Value.Value);
            Assert.Equal(JobStatus.Blocked, rig.Job(d3, JobKind.Fuel).Status);
            Assert.Equal(JobStatus.Blocked, rig.Job(d1, JobKind.Fuel).Status);

            List<(Rec Rec, TurnaroundJobBlocked Evt)> blocked = rig.Rec.Of<TurnaroundJobBlocked>();
            Rec b3 = blocked.Find(b => b.Evt.Flight.Value == d3 && b.Evt.Job == JobKind.Fuel).Rec;
            Rec b1 = blocked.Find(b => b.Evt.Flight.Value == d1 && b.Evt.Job == JobKind.Fuel).Rec;
            Assert.True(b3 != null && b1 != null, rig.Rec.Dump());
            Assert.True(b3!.Id.CompareTo(b1!.Id) < 0, "fixture: D3's Fuel must block first");

            // Tick 89: nothing has moved yet.
            rig.RunThrough(89UL);
            Assert.Equal(JobStatus.Active, rig.Job(d2, JobKind.Fuel).Status);
            Assert.Equal(JobStatus.Blocked, rig.Job(d3, JobKind.Fuel).Status);

            // Tick 90: D2's Fuel completes and the truck goes to D3, not D1.
            rig.RunThrough(90UL);
            Assert.Equal(JobStatus.Completed, rig.Job(d2, JobKind.Fuel).Status);
            TurnaroundJob fuel3 = rig.Job(d3, JobKind.Fuel);
            Assert.True(fuel3.Status == JobStatus.Active, Show.Job(fuel3) + "\n" + rig.Rec.Dump());
            Assert.Equal(OnlyFuelTruck, fuel3.Vehicle!.Value.Value);
            Assert.Equal(90UL, fuel3.StartedAt);
            Assert.Equal(170UL, fuel3.DueAt);
            TurnaroundJob fuel1 = rig.Job(d1, JobKind.Fuel);
            Assert.Equal(JobStatus.Blocked, fuel1.Status);
            Assert.Null(fuel1.Vehicle);
            Assert.Equal(TConst.TickUnscheduled, fuel1.StartedAt);
            Assert.Equal(TConst.TickUnscheduled, fuel1.DueAt);

            // Tick 170: D3's Fuel completes and D1 is served.
            rig.RunThrough(170UL);
            Assert.Equal(JobStatus.Completed, rig.Job(d3, JobKind.Fuel).Status);
            fuel1 = rig.Job(d1, JobKind.Fuel);
            Assert.Equal(JobStatus.Active, fuel1.Status);
            Assert.Equal(170UL, fuel1.StartedAt);
            Assert.Equal(250UL, fuel1.DueAt);

            List<Rec> unblocked = rig.Rec.All.FindAll(r => r.FromTurnaround && r.Payload is TurnaroundJobUnblocked && r.Job == JobKind.Fuel);
            Assert.Equal(2, unblocked.Count);
            Assert.Equal(d3, unblocked[0].Flight);
            Assert.Equal(90UL, unblocked[0].Tick);
            Assert.Equal(d1, unblocked[1].Flight);
            Assert.Equal(170UL, unblocked[1].Tick);
        }

        [Fact]
        public void test_vehicle_dispatch_breaks_vehicle_ties_by_ascending_vehicle_id()
        {
            // Three cleaning crews declared out of id order; every other kind plentiful.
            var fleet = new List<VehicleDef>(Setups.Fleet((7, VehicleKind.CleaningCrew), (3, VehicleKind.CleaningCrew), (5, VehicleKind.CleaningCrew)));
            ushort next = 20;
            foreach (VehicleKind k in new[] { VehicleKind.CateringTruck, VehicleKind.FuelTruck, VehicleKind.BaggageTractor, VehicleKind.PushbackTug })
            {
                for (int i = 0; i < 3; i++)
                {
                    fleet.Add(new VehicleDef(new VehicleId(next++), k));
                }
            }

            var rig = new Rig(ThreeDepartures(), Setups.Unit(fleet.ToArray()), onStand: new[] { ("D3", 10UL), ("D1", 11UL) });

            // 13 §13.7: FreeVehicles is ascending VehicleId.
            Assert.Equal(new ushort[] { 3, 5, 7 }, rig.Free(VehicleKind.CleaningCrew));

            rig.RunThrough(10UL);
            Assert.Equal((ushort)3, rig.Job("D3", JobKind.CabinClean).Vehicle!.Value.Value);
            Assert.Equal(new ushort[] { 5, 7 }, rig.Free(VehicleKind.CleaningCrew));

            rig.RunThrough(11UL);
            Assert.Equal((ushort)5, rig.Job("D1", JobKind.CabinClean).Vehicle!.Value.Value);
            Assert.Equal(new ushort[] { 7 }, rig.Free(VehicleKind.CleaningCrew));

            Assert.True(rig.Turnaround.TryGetVehicle(new VehicleId(3), out VehicleState v3));
            Assert.Equal(VehicleKind.CleaningCrew, v3.Kind);
            Assert.Equal(TConst.Job(rig.Id("D3"), JobKind.CabinClean), v3.Assignment!.Value);
            Assert.True(rig.Turnaround.TryGetVehicle(new VehicleId(7), out VehicleState v7));
            Assert.Null(v7.Assignment);
        }
    }
}
