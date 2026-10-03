using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.6: Boarding is created Blocked on JobDependency with the other
    /// departure jobs, and stays Blocked until CabinClean, Catering, Fuel,
    /// BaggageLoad and PushbackPrep are all Completed. The tick the fifth
    /// completes it unblocks and starts, and ReadyToBoard fires.
    /// </summary>
    public sealed class BoardingTests
    {
        private static void AssertWaiting(Rig rig, ulong flight, ulong tick)
        {
            TurnaroundJob b = rig.Job(flight, JobKind.Boarding);
            string at = " at tick " + tick.ToString(CultureInfo.InvariantCulture) + ": " + Show.Job(b);
            Assert.True(b.Status == JobStatus.Blocked, "Boarding not Blocked" + at);
            Assert.True(b.Vehicle == null, "Boarding has a vehicle" + at);
            Assert.True(b.StartedAt == TConst.TickUnscheduled, "Blocked Boarding has a StartedAt" + at);
            Assert.True(b.DueAt == TConst.TickUnscheduled, "Blocked Boarding has a DueAt" + at);
        }

        [Fact]
        public void test_boarding_waits_for_all_other_departure_jobs()
        {
            // Unimpeded, from OnStand at 10: PushbackPrep done 40, BaggageLoad
            // 65, CabinClean 70, Catering 80, Fuel 90 (the fifth). Boarding
            // waits through 89, starts at 90 and completes at 190.
            var rig = new Rig(Csv.Of(Csv.Row("D1", "D", "06:00")), Setups.Unit(Setups.Plenty()), onStand: new[] { ("D1", 10UL) });
            ulong d1 = rig.Id("D1");
            rig.RunThrough(10UL);
            TurnaroundJob created = rig.Job(d1, JobKind.Boarding);
            Assert.Equal(10UL, created.CreatedAt);
            Assert.Equal(d1, created.Flight.Value);

            for (ulong t = 10UL; t < 90UL; t++)
            {
                rig.RunThrough(t);
                AssertWaiting(rig, d1, t);
            }

            Assert.Equal(JobStatus.Active, rig.Job(d1, JobKind.Fuel).Status);
            foreach (JobKind k in new[] { JobKind.CabinClean, JobKind.Catering, JobKind.BaggageLoad, JobKind.PushbackPrep })
            {
                Assert.Equal(JobStatus.Completed, rig.Job(d1, k).Status);
            }

            Assert.Empty(rig.Rec.Milestones(FlightMilestone.ReadyToBoard));

            rig.RunThrough(90UL);
            TurnaroundJob boarding = rig.Job(d1, JobKind.Boarding);
            Assert.True(boarding.Status == JobStatus.Active, Show.Job(boarding) + "\n" + rig.Rec.Dump());
            Assert.Equal(90UL, boarding.StartedAt);
            Assert.Equal(190UL, boarding.DueAt);
            Assert.Null(boarding.Vehicle);
            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard));
            Assert.Equal(d1, ready.Flight);
            Assert.Equal(90UL, ready.Tick);
            Assert.Equal(90UL, ready.Milestone.ActualTick);

            // Unblocked, then Started, then ReadyToBoard, all at 90 (13 §13.5 step 1).
            List<Rec> at90 = rig.Rec.All.FindAll(r => r.FromTurnaround && r.Tick == 90UL && r.Flight == d1 && (r.Job == JobKind.Boarding || r.IsMilestone(FlightMilestone.ReadyToBoard)));
            Assert.True(at90.Count == 3, rig.Rec.Dump());
            Assert.IsType<TurnaroundJobUnblocked>(at90[0].Payload);
            Assert.IsType<TurnaroundJobStarted>(at90[1].Payload);
            Assert.True(at90[2].IsMilestone(FlightMilestone.ReadyToBoard), rig.Rec.Dump());
            var unblock = (TurnaroundJobUnblocked)at90[0].Payload;
            Assert.Equal(ResourceKind.JobDependency, unblock.WaitingOn);

            rig.RunThrough(189UL);
            Assert.Empty(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            rig.RunThrough(190UL);
            Assert.Equal(JobStatus.Completed, rig.Job(d1, JobKind.Boarding).Status);
            Rec complete = Assert.Single(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            Assert.Equal(d1, complete.Flight);
            Assert.Equal(190UL, complete.Tick);
        }

        [Fact]
        public void test_boarding_blocked_event_names_job_dependency_at_creation()
        {
            JobDef[] jobs = Setups.UnitJobs();
            var rig = new Rig(Csv.Of(Csv.Row("D1", "D", "06:00")), Setups.Of(jobs, Setups.Plenty()), onStand: new[] { ("D1", 10UL) });
            rig.RunThrough(10UL);
            List<(Rec Rec, TurnaroundJobBlocked Evt)> blocked = rig.Rec.Of<TurnaroundJobBlocked>(rig.Id("D1"));
            (Rec Rec, TurnaroundJobBlocked Evt) b = Assert.Single(blocked);
            Assert.Equal(JobKind.Boarding, b.Evt.Job);
            Assert.Equal(ResourceKind.JobDependency, b.Evt.WaitingOn);
            Assert.Null(b.Evt.Resource);
            Assert.Equal(Setups.Category(jobs, JobKind.Boarding), b.Evt.Category);
            Assert.Equal(10UL, b.Rec.Tick);
        }

        [Fact]
        public void test_boarding_never_starts_while_a_prerequisite_never_gets_a_vehicle()
        {
            // No fuel truck at all (13 §13.4: a legitimate, if grim, fleet).
            // The other four complete; Fuel and Boarding stay Blocked for good.
            var rig = new Rig(
                Csv.Of(Csv.Row("D1", "D", "06:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.FuelTruck, 0))),
                onStand: new[] { ("D1", 10UL) });
            ulong d1 = rig.Id("D1");
            rig.RunThrough(2000UL);
            foreach (JobKind k in new[] { JobKind.CabinClean, JobKind.Catering, JobKind.BaggageLoad, JobKind.PushbackPrep })
            {
                Assert.Equal(JobStatus.Completed, rig.Job(d1, k).Status);
            }

            Assert.Equal(JobStatus.Blocked, rig.Job(d1, JobKind.Fuel).Status);
            AssertWaiting(rig, d1, 2000UL);
            Assert.Empty(rig.Rec.Milestones(FlightMilestone.ReadyToBoard));
            Assert.Empty(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            Assert.Empty(rig.Rec.ForJob(d1, JobKind.Boarding).FindAll(r => r.Payload is TurnaroundJobStarted || r.Payload is TurnaroundJobUnblocked));
        }

        [Fact]
        public void test_boarding_waits_for_a_prerequisite_delayed_by_a_vehicle_wait()
        {
            // One fuel truck: D2's Fuel holds it 10-90, so D1's Fuel (OnStand
            // 20) runs 90-170 and is the fifth prerequisite by far. D1's other
            // four are done by 100. Boarding starts at 170, not at 100.
            var rig = new Rig(
                Csv.Of(Csv.Row("D1", "D", "06:00"), Csv.Row("D2", "D", "06:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.FuelTruck, 1))),
                onStand: new[] { ("D2", 10UL), ("D1", 20UL) });
            ulong d1 = rig.Id("D1");
            for (ulong t = 20UL; t < 170UL; t++)
            {
                rig.RunThrough(t);
                AssertWaiting(rig, d1, t);
            }

            rig.RunThrough(170UL);
            TurnaroundJob boarding = rig.Job(d1, JobKind.Boarding);
            Assert.True(boarding.Status == JobStatus.Active, Show.Job(boarding) + "\n" + rig.Rec.Dump());
            Assert.Equal(170UL, boarding.StartedAt);
            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard, d1));
            Assert.Equal(170UL, ready.Tick);
        }
    }
}
