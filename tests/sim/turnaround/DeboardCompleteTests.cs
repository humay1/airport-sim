using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.6 and 10 §10.4: an arrival's OnStand creates Deboard and
    /// BaggageUnload; DeboardComplete fires once, for the arrival's own
    /// FlightId, the tick Deboard completes. Departures never get one.
    /// </summary>
    public sealed class DeboardCompleteTests
    {
        [Fact]
        public void test_deboard_complete_fires_once_per_arrival()
        {
            // A1 and A2 reach their stands in the same tick, A3 later; D1 is a
            // departure. No baggage tractor at all, so BaggageUnload never
            // completes: DeboardComplete hangs on Deboard alone.
            var rig = new Rig(
                Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:05"), Csv.Row("A3", "A", "06:30"), Csv.Row("D1", "D", "07:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.BaggageTractor, 0))),
                onStand: new[] { ("A1", 3700UL), ("A2", 3700UL), ("D1", 3720UL), ("A3", 3900UL) });
            rig.RunTo(6000UL);

            List<Rec> deboard = rig.Rec.Milestones(FlightMilestone.DeboardComplete);
            foreach ((string a, ulong onStand) in new[] { ("A1", 3700UL), ("A2", 3700UL), ("A3", 3900UL) })
            {
                ulong id = rig.Id(a);
                List<Rec> mine = deboard.FindAll(r => r.Flight == id);
                Assert.True(mine.Count == 1, a + ": expected exactly one DeboardComplete\n" + rig.Rec.Dump());
                Assert.Equal(onStand + 40UL, mine[0].Tick);
                Assert.Equal(onStand + 40UL, mine[0].Milestone.ActualTick);
                Assert.Equal(JobStatus.Completed, rig.Job(id, JobKind.Deboard).Status);
                Assert.Equal(JobStatus.Blocked, rig.Job(id, JobKind.BaggageUnload).Status);
            }

            Assert.Equal(3, deboard.Count);
            Assert.Empty(deboard.FindAll(r => r.Flight == rig.Id("D1")));

            // An arrival gets no departure milestone (12 §12.3 "Which FlightId").
            foreach (string a in new[] { "A1", "A2", "A3" })
            {
                Assert.Empty(rig.Rec.Milestones(FlightMilestone.ReadyToBoard, rig.Id(a)));
                Assert.Empty(rig.Rec.Milestones(FlightMilestone.BoardingComplete, rig.Id(a)));
            }
        }

        [Fact]
        public void test_deboard_complete_starts_deboard_at_on_stand_without_a_vehicle()
        {
            // 13 §13.5 step 2: Deboard (RequiresVehicle null) goes straight to Active.
            var rig = new Rig(Csv.Of(Csv.Row("A1", "A", "06:00")), Setups.Unit(Setups.Plenty(0)), onStand: new[] { ("A1", 3700UL) });
            rig.RunThrough(3700UL);
            TurnaroundJob deboard = rig.Job("A1", JobKind.Deboard);
            Assert.Equal(JobStatus.Active, deboard.Status);
            Assert.Null(deboard.Vehicle);
            Assert.Equal(3700UL, deboard.CreatedAt);
            Assert.Equal(3700UL, deboard.StartedAt);
            Assert.Equal(3740UL, deboard.DueAt);
            Assert.Empty(rig.Rec.ForJob(rig.Id("A1"), JobKind.Deboard).FindAll(r => r.Payload is TurnaroundJobBlocked));

            rig.RunThrough(3740UL);
            Assert.Single(rig.Rec.Milestones(FlightMilestone.DeboardComplete, rig.Id("A1")));
        }
    }
}
