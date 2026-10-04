using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.9: missed passengers live on the record, not in the tree.</summary>
    public sealed class DelayMissedPassengersTests
    {
        private const ulong Dep = 12UL;

        [Fact]
        public void test_delay_missed_passengers_recorded_on_record_not_tree()
        {
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            s.Missed(1100, Dep, 3, 42);
            s.Missed(1200, Dep, 2, 43);
            s.Milestone(1300, Dep, FlightMilestone.Pushback, 1300);
            s.Milestone(1380, Dep, FlightMilestone.Airborne, 1380);
            s.Missed(2000, Dep, 4, 44);
            var rig = new DelayRig(s);

            rig.RunThrough(1000);
            ulong before = rig.Delay.ComputeStateHash();
            rig.RunThrough(1200);
            FlightDelay r = rig.Record(Dep);
            Assert.Equal(5, r.MissedPassengers);
            Assert.True(r.MissedLastBlockedAt.HasValue);
            Assert.Equal(42U, r.MissedLastBlockedAt!.Value.Value);
            Assert.Equal(0UL, r.TotalTicks);
            Assert.Empty(rig.Leaves(Dep));
            Assert.Empty(rig.R.Delays);
            Assert.NotEqual(before, rig.Delay.ComputeStateHash());

            // Accepted after finalisation; the first lastBlockedAt is kept. Only
            // the root (0 ticks) was ever published, at Airborne.
            rig.RunThrough(2000);
            r = rig.Record(Dep);
            Assert.True(r.Finalised);
            Assert.Equal(9, r.MissedPassengers);
            Assert.Equal(42U, r.MissedLastBlockedAt!.Value.Value);
            Assert.Empty(rig.Leaves(Dep));
            Assert.Single(rig.R.Delays);
            Assert.Equal(0UL, rig.R.Delays[0].Evt.Node.Ticks);
        }

        [Fact]
        public void test_delay_missed_passengers_for_unknown_flight_throws_with_tick()
        {
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Missed(500, 99, 1, 1);
            new DelayRig(s).AssertThrowsAt(500, "PassengersMissedFlight for unknown flight 99");
        }
    }
}
