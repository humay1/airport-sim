using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.4 checkpoints and §14.6's ordering rules.</summary>
    public sealed class DelayCheckpointTests
    {
        private const ulong Arr = 11UL;
        private const ulong Dep = 12UL;

        [Fact]
        public void test_delay_checkpoint_non_checkpoint_milestones_are_ignored()
        {
            // Every milestone outside §14.4's table for the flight's kind is
            // ignored, even with an unscheduled or absurd PlannedTick: no record
            // field, node or hashed state changes.
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Plan(0, Dep, MovementKind.Departure);
            ulong t = 100;
            foreach (FlightMilestone m in new[]
            {
                FlightMilestone.PlanPublished, FlightMilestone.InboundAirborne, FlightMilestone.OffRunway, FlightMilestone.DoorsOpen,
                FlightMilestone.DeboardComplete, FlightMilestone.ReadyToBoard, FlightMilestone.BoardingComplete, FlightMilestone.DoorsClosed,
                FlightMilestone.Pushback, FlightMilestone.TakeoffRoll, FlightMilestone.Airborne,
            })
            {
                s.Milestone(t, Arr, m, m == FlightMilestone.DoorsOpen ? DConst.TickUnscheduled : 1UL);
                t += 10;
            }

            foreach (FlightMilestone m in new[]
            {
                FlightMilestone.PlanPublished, FlightMilestone.InboundAirborne, FlightMilestone.Landed, FlightMilestone.OffRunway,
                FlightMilestone.DoorsOpen, FlightMilestone.DeboardComplete, FlightMilestone.ReadyToBoard, FlightMilestone.BoardingComplete,
                FlightMilestone.DoorsClosed, FlightMilestone.TakeoffRoll,
            })
            {
                s.Milestone(t, Dep, m, m == FlightMilestone.Landed ? DConst.TickUnscheduled : 1UL);
                t += 10;
            }

            var rig = new DelayRig(s);
            rig.RunTo(1);
            string arr = Show.Tree(rig.Delay, Arr);
            string dep = Show.Tree(rig.Delay, Dep);
            ulong hash = rig.Delay.ComputeStateHash();
            rig.RunThrough(t);
            Assert.Equal(arr, Show.Tree(rig.Delay, Arr));
            Assert.Equal(dep, Show.Tree(rig.Delay, Dep));
            Assert.Equal(hash, rig.Delay.ComputeStateHash());
            Assert.Equal(0, rig.Record(Arr).CheckpointsReached);
            Assert.Equal(0, rig.Record(Dep).CheckpointsReached);
        }

        [Fact]
        public void test_delay_checkpoint_early_finish_is_zero_lateness_never_negative()
        {
            // Landed planned 1000, actual 900: lateness 0, not −100 (§14.4).
            // OnStand planned 1000, actual 1100: late 100, delta 100 over
            // W = [900, 1100) with no interval: residue 100.
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Milestone(900, Arr, FlightMilestone.Landed, 1000);
            s.Milestone(1100, Arr, FlightMilestone.OnStand, 1000);
            var rig = new DelayRig(s);
            rig.RunThrough(900);
            FlightDelay r = rig.Record(Arr);
            Assert.Equal(0UL, r.TotalTicks);
            Assert.Equal(1, r.CheckpointsReached);
            Assert.Equal(900UL, r.LastCheckpointActual);
            Assert.Empty(rig.Leaves(Arr));
            rig.RunThrough(1100);
            Assert.Equal(new List<string> { Unexplained(2, Arr, 1, 100, 1100) }, LeafTexts(rig, Arr));
            r = rig.Record(Arr);
            Assert.True(r.Finalised);
            Assert.Equal(1100UL, r.FinalisedAt);
            Assert.Equal(1100UL, r.LastCheckpointActual);
            Assert.Equal(2, r.CheckpointsReached);
        }

        [Fact]
        public void test_delay_checkpoint_out_of_order_throws_with_tick()
        {
            // A departure's Pushback before its OnStand (§14.6).
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.Pushback, 1000);
            new DelayRig(s).AssertThrowsAt(1000, "Pushback before OnStand");
        }

        [Fact]
        public void test_delay_checkpoint_twice_throws_with_tick()
        {
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Milestone(1000, Arr, FlightMilestone.Landed, 1000);
            s.Milestone(1010, Arr, FlightMilestone.Landed, 1010);
            new DelayRig(s).AssertThrowsAt(1010, "Landed twice");
        }

        [Fact]
        public void test_delay_checkpoint_after_terminal_throws_with_tick()
        {
            // The terminal checkpoint again, after finalisation: "twice".
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Milestone(1000, Arr, FlightMilestone.Landed, 1000);
            s.Milestone(1100, Arr, FlightMilestone.OnStand, 1100);
            s.Milestone(1200, Arr, FlightMilestone.OnStand, 1200);
            new DelayRig(s).AssertThrowsAt(1200, "OnStand after finalisation");
        }

        [Fact]
        public void test_delay_checkpoint_with_unscheduled_planned_tick_throws_with_tick()
        {
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Milestone(1000, Arr, FlightMilestone.Landed, DConst.TickUnscheduled);
            new DelayRig(s).AssertThrowsAt(1000, "PlannedTick == TICK_UNSCHEDULED");
        }

        [Fact]
        public void test_delay_checkpoint_for_unknown_flight_throws_with_tick()
        {
            // §14.4 (Q-108). OnStand, a checkpoint of either kind, for a flight
            // never published: throw, and no record is created.
            var never = new Script();
            never.Plan(0, Arr, MovementKind.Arrival);
            never.Milestone(1000, 99, FlightMilestone.OnStand, 1000);
            var a = new DelayRig(never);
            a.AssertThrowsAt(1000, "checkpoint for unpublished flight 99");

            // The same for a pruned flight: arrival 21, finalised at 200 on day 0,
            // is pruned at 28800; its OnStand again at 28900 throws.
            var pruned = new Script();
            pruned.Plan(0, 21, MovementKind.Arrival);
            pruned.Milestone(100, 21, FlightMilestone.Landed, 100);
            pruned.Milestone(200, 21, FlightMilestone.OnStand, 200);
            pruned.Milestone(28900, 21, FlightMilestone.OnStand, 28900);
            var b = new DelayRig(pruned);
            b.RunTo(28900);
            Assert.False(b.Delay.TryGetFlightDelay(new FlightId(21), out _), "flight 21 should have been pruned at 28800");
            b.AssertThrowsAt(28900, "checkpoint for pruned flight 21");

            // A non-checkpoint milestone for an unknown flight is ignored, with
            // no lookup and no state change.
            var ignored = new Script();
            ignored.Plan(0, Arr, MovementKind.Arrival);
            ignored.Milestone(1000, 99, FlightMilestone.DoorsOpen, 1000);
            ignored.Milestone(1010, 99, FlightMilestone.BoardingComplete, DConst.TickUnscheduled);
            var c = new DelayRig(ignored);
            c.RunTo(1);
            ulong hash = c.Delay.ComputeStateHash();
            c.RunThrough(1100);
            Assert.Equal(hash, c.Delay.ComputeStateHash());
            Assert.False(c.Delay.TryGetFlightDelay(new FlightId(99), out _));
            Assert.Equal(new List<FlightId> { new FlightId(Arr) }, new List<FlightId>(c.Delay.RetainedFlights()));
        }

        [Fact]
        public void test_delay_checkpoint_window_starts_at_publication_then_at_last_checkpoint()
        {
            // §14.6 step 1 and §14.3: LastCheckpointActual is the tick
            // FlightPlanPublished was handled (500 here) until the first
            // checkpoint, and the first window starts there.
            // Landed planned 600, actual 800: delta 200 over [500, 800); the
            // runway hold [600, 700) owns 100; residue 100.
            // OnStand planned 860, actual 1000: late 140, delta −60 (recovery:
            // the residue, the later leaf, loses 60 → 40).
            var s = new Script();
            s.Plan(500, Arr, MovementKind.Arrival);
            Step rw = s.RunwayHeld(600, Arr, 1, 2);
            s.RunwayReleased(700, Arr, 1);
            s.Milestone(800, Arr, FlightMilestone.Landed, 600);
            s.Milestone(1000, Arr, FlightMilestone.OnStand, 860);
            var rig = new DelayRig(s);
            rig.RunThrough(500);
            Assert.Equal(500UL, rig.Record(Arr).LastCheckpointActual);
            rig.RunThrough(800);
            Assert.Equal(
                new List<string>
                {
                    Leaf(2, Arr, 1, DelayCategory.RunwayCongestion, 100, true, 0, rw.Ref, DelaySource.RunwayHold, 1, 2, 800),
                    Unexplained(3, Arr, 1, 100, 800),
                },
                LeafTexts(rig, Arr));
            rig.RunThrough(1000);
            Assert.Equal(
                new List<string>
                {
                    Leaf(2, Arr, 1, DelayCategory.RunwayCongestion, 100, true, 0, rw.Ref, DelaySource.RunwayHold, 1, 2, 800),
                    Unexplained(3, Arr, 1, 40, 800),
                },
                LeafTexts(rig, Arr));
            Assert.Equal(140UL, rig.Record(Arr).TotalTicks);
        }
    }
}
