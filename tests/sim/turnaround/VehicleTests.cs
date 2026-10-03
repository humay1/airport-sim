using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.5 step 1: a completion frees its vehicle, and the freed vehicle
    /// is eligible for assignment later in the same tick's pass, not next tick.
    /// </summary>
    public sealed class VehicleTests
    {
        [Fact]
        public void test_vehicle_freed_on_completion_is_reassigned_same_tick()
        {
            // One cleaning crew (id 1). D1's CabinClean holds it from 10 to 70
            // (60 ticks); D2's blocks on it at 20.
            var rig = new Rig(
                Csv.Of(Csv.Row("D1", "D", "06:00"), Csv.Row("D2", "D", "06:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.CleaningCrew, 1))),
                onStand: new[] { ("D1", 10UL), ("D2", 20UL) });
            ulong d1 = rig.Id("D1");
            ulong d2 = rig.Id("D2");
            JobId clean1 = TConst.Job(d1, JobKind.CabinClean);
            JobId clean2 = TConst.Job(d2, JobKind.CabinClean);

            rig.RunThrough(69UL);
            Assert.Equal(JobStatus.Active, rig.Job(d1, JobKind.CabinClean).Status);
            Assert.Equal(JobStatus.Blocked, rig.Job(d2, JobKind.CabinClean).Status);
            Assert.True(rig.Turnaround.TryGetVehicle(new VehicleId(1), out VehicleState before));
            Assert.Equal(clean1, before.Assignment!.Value);
            Assert.Empty(rig.Free(VehicleKind.CleaningCrew));

            rig.RunThrough(70UL);
            TurnaroundJob done = rig.Job(d1, JobKind.CabinClean);
            Assert.Equal(JobStatus.Completed, done.Status);
            TurnaroundJob next = rig.Job(d2, JobKind.CabinClean);
            Assert.True(next.Status == JobStatus.Active, Show.Job(next) + "\n" + rig.Rec.Dump());
            Assert.Equal((ushort)1, next.Vehicle!.Value.Value);
            Assert.Equal(70UL, next.StartedAt);
            Assert.Equal(130UL, next.DueAt);
            Assert.True(rig.Turnaround.TryGetVehicle(new VehicleId(1), out VehicleState after));
            Assert.Equal(clean2, after.Assignment!.Value);
            Assert.Empty(rig.Free(VehicleKind.CleaningCrew));

            // Tick 70's events about the two jobs: D1's completion, then D2's
            // Unblocked and Started, the pair caused by that completion.
            List<Rec> at70 = rig.Rec.All.FindAll(r => r.FromTurnaround && r.Tick == 70UL && r.Job == JobKind.CabinClean);
            Assert.True(at70.Count == 3, rig.Rec.Dump());
            Assert.True(at70[0].Payload is TurnaroundJobCompleted && at70[0].Flight == d1, rig.Rec.Dump());
            Assert.True(at70[1].Payload is TurnaroundJobUnblocked && at70[1].Flight == d2, rig.Rec.Dump());
            Assert.True(at70[2].Payload is TurnaroundJobStarted && at70[2].Flight == d2, rig.Rec.Dump());
            Assert.Equal(at70[0].Id, at70[1].Env.Cause.Id);
            Assert.Equal(at70[0].Id, at70[2].Env.Cause.Id);

            // Nothing happened to D2's CabinClean at 71 that should have happened at 70.
            rig.RunThrough(71UL);
            Assert.Single(rig.Rec.All.FindAll(r => r.FromTurnaround && r.Flight == d2 && r.Payload is TurnaroundJobStarted && r.Job == JobKind.CabinClean));
        }
    }
}
