using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.8 "Finalisation": a flight that never reaches its terminal checkpoint is retained, open intervals included, across day boundaries.</summary>
    public sealed class DelayUnfinalisedTests
    {
        [Fact]
        public void test_delay_unfinalised_flight_survives_day_boundary()
        {
            // Roots: 11 → 1, 12 → 2, 21 → 3.
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival, 12);
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Plan(0, 21, MovementKind.Arrival);
            s.Milestone(12000, 11, FlightMilestone.Landed, 12000);
            s.Milestone(12100, 11, FlightMilestone.OnStand, 12100);
            s.Milestone(400, 21, FlightMilestone.Landed, 400);
            s.Milestone(500, 21, FlightMilestone.OnStand, 500);

            // 12 reaches its stand on time at 13000 and its Catering waits from
            // 13100 for a truck that comes only on day 3.
            s.Milestone(13000, 12, FlightMilestone.OnStand, 13000);
            Step wait = s.JobBlocked(13100, 12, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            var rig = new DelayRig(s);

            rig.RunThrough(43200);
            FlightDelay r = rig.Record(12);
            Assert.False(r.Finalised);
            Assert.Equal(1, r.CheckpointsReached);
            Assert.Equal(DConst.TickUnscheduled, r.FinalisedAt);
            Assert.True(rig.Delay.TryGetFlightDelay(new FlightId(11), out _), "the unfinalised flight's rotation was pruned");
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(21), out _), "the rotation-less day-0 flight was not pruned");

            // The wait is still open: closing it does not throw, and it is
            // allocated. Pushback planned 13500, actual 43400: late 29900 over
            // W = [13000, 43400); Catering owns 13100-43300 = 30200, capped.
            var more = new Script();
            more.JobUnblocked(43300, 12, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            more.Milestone(43400, 12, FlightMilestone.Pushback, 13500);
            more.Milestone(43480, 12, FlightMilestone.Airborne, 13580);
            rig.Append(more);
            rig.RunThrough(43480);
            Assert.Equal(
                new List<string> { Job(4, 12, 2, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 29900, wait, 43400) },
                LeafTexts(rig, 12));
            r = rig.Record(12);
            Assert.True(r.Finalised);
            Assert.Equal(43480UL, r.FinalisedAt);

            // Finalised on day 3: retained through the start of day 4, pruned with
            // its rotation at the start of day 5 (threshold 57600).
            rig.RunThrough(57600);
            Assert.True(rig.Delay.TryGetFlightDelay(new FlightId(12), out _));
            rig.RunThrough(72000);
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(12), out _));
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(11), out _));
        }
    }
}
