using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.10 "Retention" (Q-092): a finished flight (all jobs Completed;
    /// finish tick = its largest DueAt) is kept for the sim-day it finished on
    /// and the next, and pruned on the first tick of the day after; an
    /// unfinished flight is never pruned.
    /// </summary>
    public sealed class FinishedFlightTests
    {
        private static bool Has(Rig rig, string flightRef, JobKind kind)
        {
            return rig.Turnaround.TryGetJob(TConst.Job(rig.Id(flightRef), kind), out _);
        }

        private static int Count(Rig rig, string flightRef)
        {
            return rig.Turnaround.JobsForFlight(new FlightId(rig.Id(flightRef))).Count;
        }

        [Fact]
        public void test_finished_flight_jobs_pruned_after_retention_days()
        {
            // No tug: D1's PushbackPrep never starts, so D1 never finishes.
            // A1 finishes at 150 (day 0): Deboard 100-140, BaggageUnload 100-150.
            // A2 finishes at 14 400, the first tick of day 1: Deboard
            // 14 350-14 390, BaggageUnload 14 350-14 400.
            var rig = new Rig(
                Csv.Of(Csv.Row("A1", "A", "00:05"), Csv.Row("A2", "A", "23:50"), Csv.Row("D1", "D", "06:00")),
                Setups.Unit(Setups.Plenty(4, (VehicleKind.PushbackTug, 0))),
                onStand: new[] { ("A1", 100UL), ("D1", 200UL), ("A2", 14350UL) });

            rig.RunTo(14401UL);
            Assert.Equal(JobStatus.Completed, rig.Job("A1", JobKind.BaggageUnload).Status);
            Assert.Equal(JobStatus.Completed, rig.Job("A2", JobKind.BaggageUnload).Status);
            Assert.Equal(14400UL, rig.Job("A2", JobKind.BaggageUnload).DueAt);
            Assert.Equal(JobStatus.Blocked, rig.Job("D1", JobKind.PushbackPrep).Status);

            // The last tick of day 1 has run: A1 (finished day 0) is still there.
            rig.RunTo(2UL * TConst.TicksPerDay);
            Assert.True(Has(rig, "A1", JobKind.Deboard), "A1 pruned before the first tick of day 2");
            Assert.Equal(2, Count(rig, "A1"));

            // The first tick of day 2 has run: A1 is gone, A2 (finished day 1) is not.
            rig.RunTo((2UL * TConst.TicksPerDay) + 1UL);
            Assert.False(Has(rig, "A1", JobKind.Deboard), "A1 not pruned at the first tick of day 2");
            Assert.False(Has(rig, "A1", JobKind.BaggageUnload));
            Assert.Equal(0, Count(rig, "A1"));
            Assert.True(Has(rig, "A2", JobKind.Deboard), "A2 pruned a day early");
            Assert.Equal(6, Count(rig, "D1"));

            rig.RunTo(3UL * TConst.TicksPerDay);
            Assert.True(Has(rig, "A2", JobKind.BaggageUnload), "A2 pruned before the first tick of day 3");
            rig.RunTo((3UL * TConst.TicksPerDay) + 1UL);
            Assert.False(Has(rig, "A2", JobKind.Deboard), "A2 not pruned at the first tick of day 3");
            Assert.Equal(0, Count(rig, "A2"));

            // The unfinished flight is never pruned.
            rig.RunTo((5UL * TConst.TicksPerDay) + 1UL);
            Assert.Equal(6, Count(rig, "D1"));
            Assert.True(Has(rig, "D1", JobKind.Boarding));
            Assert.Equal(JobStatus.Completed, rig.Job("D1", JobKind.Fuel).Status);
        }
    }
}
