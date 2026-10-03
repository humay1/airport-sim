using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.5 step 2 "Order" (Q-090): within one OnStand handler call the
    /// flight's jobs are created in ascending JobKind, each fully resolved
    /// (Started or Blocked published, the lowest free VehicleId of its kind
    /// taken) before the next; two OnStands in one tick are handled in
    /// dispatch order.
    /// </summary>
    public sealed class CreationEventsTests
    {
        private static List<string> Creation(Rig rig, ulong flight, ulong tick)
        {
            var seq = new List<string>();
            foreach (Rec r in rig.Rec.All)
            {
                if (r.FromTurnaround && r.Flight == flight && r.Tick == tick && r.Job.HasValue)
                {
                    string v = r.Vehicle.HasValue ? "@" + r.Vehicle.Value.Value : string.Empty;
                    seq.Add(r.Type.Replace("TurnaroundJob", string.Empty) + " " + r.Job.Value + v);
                }
            }

            return seq;
        }

        [Fact]
        public void test_creation_events_follow_job_kind_order()
        {
            // No catering truck and no tug: Catering and PushbackPrep block.
            // Two cleaning crews (5 and 2): CabinClean takes 2, the lowest.
            var rig = new Rig(
                Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("D1", "D", "06:00")),
                Setups.Unit(Setups.Fleet((5, VehicleKind.CleaningCrew), (2, VehicleKind.CleaningCrew), (7, VehicleKind.FuelTruck), (9, VehicleKind.BaggageTractor))),
                onStand: new[] { ("D1", 10UL), ("A1", 20UL) });
            rig.RunTo(30UL);

            Assert.Equal(
                new List<string>
                {
                    "Started CabinClean@2", "Blocked Catering", "Started Fuel@7", "Started BaggageLoad@9", "Blocked PushbackPrep", "Blocked Boarding",
                },
                Creation(rig, rig.Id("D1"), 10UL));

            // The arrival: Deboard straight to Active, then BaggageUnload,
            // which finds the only tractor held by D1's BaggageLoad.
            Assert.Equal(new List<string> { "Started Deboard", "Blocked BaggageUnload" }, Creation(rig, rig.Id("A1"), 20UL));
        }

        [Fact]
        public void test_creation_events_two_on_stands_in_one_tick_follow_dispatch_order()
        {
            // D3 and D1 on stand in the same tick; the driver publishes D1's
            // OnStand first (lower FlightId), so D1 creates first and takes
            // the only cleaning crew, and all of D1's creation events precede D3's.
            var rig = new Rig(
                Csv.Of(Csv.Row("D1", "D", "06:00"), Csv.Row("D3", "D", "06:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.CleaningCrew, 1))),
                onStand: new[] { ("D3", 10UL), ("D1", 10UL) });
            rig.RunTo(11UL);
            ulong d1 = rig.Id("D1");
            ulong d3 = rig.Id("D3");
            Assert.True(rig.Rec.OnStands[d1].Id.CompareTo(rig.Rec.OnStands[d3].Id) < 0, "fixture: D1's OnStand must be dispatched first");

            Assert.Equal(JobStatus.Active, rig.Job(d1, JobKind.CabinClean).Status);
            Assert.Equal(JobStatus.Blocked, rig.Job(d3, JobKind.CabinClean).Status);
            List<Rec> creation = rig.Rec.All.FindAll(r => r.FromTurnaround && r.Tick == 10UL && r.Job.HasValue);
            Assert.Equal(12, creation.Count);
            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(i < 6 ? d1 : d3, creation[i].Flight);
            }
        }
    }
}
