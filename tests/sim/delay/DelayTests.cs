using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 14 §14.4–§14.9 on hand-built event streams whose trees can be worked
    /// out by hand. Every expected tick count below is derived in the comment
    /// beside it from §14.6's steps. Nothing else is registered: sim.delay is a
    /// pure function of its input events (§14.14).
    /// </summary>
    public sealed class DelayTests
    {
        private const ulong Arr = 11UL;
        private const ulong Dep = 12UL;

        [Fact]
        public void test_delay_concurrent_intervals_first_blocker_wins()
        {
            // Departure, on time at OnStand (1000). Window W = [1000, 1500).
            //   Catering [1100, 1300) and Fuel [1100, 1400) open on the same tick;
            //   Catering's opener is published first, so its OpenerId is lower.
            //   Passenger hold [1350, 1450).
            // Pushback planned 1150, actual 1500: late 350, delta 350.
            // Ownership by lowest OpenerId: Catering 1100-1300 = 200; Fuel only
            // 1300-1400 = 100 (1100-1300 is Catering's); hold only 1400-1450 = 50.
            // 200 + 100 + 50 = 350 = delta, so no cap and no residue.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            Step cat = s.JobBlocked(1100, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            Step fuel = s.JobBlocked(1100, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobUnblocked(1300, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            Step hold = s.Hold(1350, Dep, 5, 42);
            s.JobUnblocked(1400, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.HoldReleased(1450, Dep);
            s.Milestone(1500, Dep, FlightMilestone.Pushback, 1150);
            var rig = new DelayRig(s);
            rig.RunThrough(1500);

            Assert.Equal(350UL, rig.Record(Dep).TotalTicks);
            Assert.Equal(
                new List<string>
                {
                    Job(2, Dep, 1, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 200, cat, 1500),
                    Job(3, Dep, 1, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 100, fuel, 1500),
                    Leaf(4, Dep, 1, DelayCategory.PassengerLate, 50, true, 0, hold.Ref, DelaySource.PassengerHold, 42, 5, 1500),
                },
                LeafTexts(rig, Dep));
            Invariants.CheckAll(rig.Delay, "after Pushback");
        }

        [Fact]
        public void test_delay_blocking_beyond_gap_is_capped_in_opener_order()
        {
            // OnStand on time at 1000; W = [1000, 1500).
            //   Fuel [1100, 1300): owns 200.
            //   Catering [1300, 1500) (opened after Fuel's close on the same tick;
            //   its close at 1500 is dispatched after Pushback, so it is open at the
            //   checkpoint and clipped to 1500): owns 200.
            //   CabinClean [1320, 1340), inside Catering's ticks: owns 0.
            // Pushback planned 1250, actual 1500: delta 250 < 400 owned.
            // Cap in ascending OpenerId: Fuel min(200, 250) = 200, Catering
            // min(200, 50) = 50, CabinClean nothing (no leaf: created on first
            // positive allocation). No residue.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            Step fuel = s.JobBlocked(1100, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobUnblocked(1300, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            Step cat = s.JobBlocked(1300, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobBlocked(1320, Dep, JobKind.CabinClean, ResourceKind.Vehicle, DelayCategory.Cleaning, 1);
            s.JobUnblocked(1340, Dep, JobKind.CabinClean, ResourceKind.Vehicle, DelayCategory.Cleaning, 1);
            s.Milestone(1500, Dep, FlightMilestone.Pushback, 1250);
            s.JobUnblocked(1500, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(1580, Dep, FlightMilestone.Airborne, 1330);
            var rig = new DelayRig(s);
            rig.RunThrough(1500);

            var expected = new List<string>
            {
                Job(2, Dep, 1, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 200, fuel, 1500),
                Job(3, Dep, 1, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 50, cat, 1500),
            };
            Assert.Equal(expected, LeafTexts(rig, Dep));
            Assert.Equal(250UL, rig.Record(Dep).TotalTicks);

            // Airborne planned 1330, actual 1580: late 250, delta 0, no change.
            rig.RunThrough(1580);
            Assert.Equal(expected, LeafTexts(rig, Dep));
            Assert.True(rig.Record(Dep).Finalised);
        }

        [Fact]
        public void test_delay_uncovered_gap_is_single_propagated_leaf()
        {
            // Arrival published at 0. Landed planned 1000, actual 1100: late 100,
            // W = [0, 1100) holds no interval, so all 100 is residue.
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.Milestone(1100, Arr, FlightMilestone.Landed, 1000);
            Step taxi = s.TaxiHeld(1150, Arr, 3, 77);
            s.TaxiReleased(1200, Arr, 3);
            s.Milestone(1500, Arr, FlightMilestone.OnStand, 1300);
            var rig = new DelayRig(s);

            rig.RunThrough(1100);
            Assert.Equal(new List<string> { Unexplained(2, Arr, 1, 100, 1100) }, LeafTexts(rig, Arr));

            // OnStand planned 1300, actual 1500: late 200, delta 100.
            // W = [1100, 1500): taxi hold 1150-1200 = 50; residue 50 goes to the
            // same Unexplained leaf (at most one per flight), now 150.
            rig.RunThrough(1500);
            Assert.Equal(
                new List<string>
                {
                    Unexplained(2, Arr, 1, 150, 1100),
                    Leaf(3, Arr, 1, DelayCategory.TaxiCongestion, 50, true, 0, taxi.Ref, DelaySource.TaxiwayHold, 3, 77, 1500),
                },
                LeafTexts(rig, Arr));
            Assert.Equal(200UL, rig.Record(Arr).TotalTicks);
        }

        [Fact]
        public void test_delay_recovery_trims_latest_leaf_first()
        {
            // OnStand on time at 1000. Fuel [1100, 1200) = 100, Catering
            // [1250, 1300) = 50. Pushback planned 1300, actual 1500: late 200,
            // delta 200: Fuel 100 (id 2), Catering 50 (id 3), residue 50 to a new
            // Unexplained leaf (id 4), created last in the handler.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            Step fuel = s.JobBlocked(1100, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobUnblocked(1200, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            Step cat = s.JobBlocked(1250, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobUnblocked(1300, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(1500, Dep, FlightMilestone.Pushback, 1300);
            s.Milestone(1580, Dep, FlightMilestone.Airborne, 1500);
            s.Plan(1600, 13, MovementKind.Arrival);
            var rig = new DelayRig(s);

            rig.RunThrough(1500);
            Assert.Equal(
                new List<string>
                {
                    Job(2, Dep, 1, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 100, fuel, 1500),
                    Job(3, Dep, 1, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 50, cat, 1500),
                    Unexplained(4, Dep, 1, 50, 1500),
                },
                LeafTexts(rig, Dep));

            // Airborne planned 1500, actual 1580: late 80, delta −120. Descending
            // DelayEventId: Unexplained 50 → 0 (removed), Catering 50 → 0
            // (removed), Fuel 100 → 80.
            rig.RunThrough(1580);
            Assert.Equal(
                new List<string> { Job(2, Dep, 1, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 80, fuel, 1500) },
                LeafTexts(rig, Dep));
            FlightDelay r = rig.Record(Dep);
            Assert.Equal(80UL, r.TotalTicks);
            Assert.Equal(80UL, rig.Root(Dep).Ticks);
            Assert.False(rig.Delay.TryGetNode(new DelayEventId(3), out _));
            Assert.False(rig.Delay.TryGetNode(new DelayEventId(4), out _));

            // A removed leaf's id is never reused: the next node created is 5.
            rig.RunThrough(1600);
            Assert.Equal(5UL, rig.Record(13).Root.Value);
        }

        [Fact]
        public void test_delay_late_inbound_capped_at_inbound_total()
        {
            // Three rotation pairs, published at tick 0 in this order (roots 1-6).
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival, 12);
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Plan(0, 21, MovementKind.Arrival, 22);
            s.Plan(0, 22, MovementKind.Departure, 21);
            s.Plan(0, 31, MovementKind.Arrival, 32);
            s.Plan(0, 32, MovementKind.Departure, 31);

            // Pair 1. Arrival 11: runway hold [900, 1050) = 150 against Landed's
            // delta 100 (planned 1000, actual 1100): capped to 100 (id 7). OnStand
            // planned 1200, actual 1350: late 150, delta 50, no interval: residue
            // (id 8). Total 150.
            Step rw = s.RunwayHeld(900, 11, 2, 3);
            s.RunwayReleased(1050, 11, 2);
            s.Milestone(1100, 11, FlightMilestone.Landed, 1000);
            s.Milestone(1350, 11, FlightMilestone.OnStand, 1200);

            // Departure 12: OnStand planned 1400, actual 1600: delta 200 >
            // inbound total 150, so late_inbound is 150 (id 9) and the remaining
            // 50 is Unexplained (id 10).
            s.Milestone(1600, 12, FlightMilestone.OnStand, 1400);

            // Pair 2. Arrival 21 is 300 late at OnStand (id 11). Departure 22's
            // delta 100 < 300: late_inbound 100 (id 12), nothing unexplained.
            s.Milestone(2300, 21, FlightMilestone.Landed, 2000);
            s.Milestone(2400, 21, FlightMilestone.OnStand, 2100);
            s.Milestone(2600, 22, FlightMilestone.OnStand, 2500);

            // Pair 3. Arrival 31 on time: inbound = min(70, 0) = 0, so no
            // InboundAircraft leaf; departure 32's 70 is Unexplained (id 13).
            s.Milestone(3000, 31, FlightMilestone.Landed, 3000);
            s.Milestone(3100, 31, FlightMilestone.OnStand, 3100);
            s.Milestone(3270, 32, FlightMilestone.OnStand, 3200);
            var rig = new DelayRig(s);
            rig.RunThrough(3300);

            Assert.Equal(
                new List<string>
                {
                    Leaf(7, 11, 1, DelayCategory.RunwayCongestion, 100, true, 0, rw.Ref, DelaySource.RunwayHold, 2, 3, 1100),
                    Unexplained(8, 11, 1, 50, 1350),
                },
                LeafTexts(rig, 11));
            Assert.Equal(150UL, rig.Record(11).TotalTicks);
            Assert.Equal(
                new List<string>
                {
                    Leaf(9, 12, 2, DelayCategory.LateInbound, 150, false, 11, EventRef.None, DelaySource.InboundAircraft, 11, 0, 1600),
                    Unexplained(10, 12, 2, 50, 1600),
                },
                LeafTexts(rig, 12));
            Assert.Equal(
                new List<string> { Leaf(12, 22, 4, DelayCategory.LateInbound, 100, false, 21, EventRef.None, DelaySource.InboundAircraft, 21, 0, 2600) },
                LeafTexts(rig, 22));
            Assert.Equal(new List<string> { Unexplained(13, 32, 6, 70, 3270) }, LeafTexts(rig, 32));
            Invariants.CheckAll(rig.Delay, "end");
        }

        [Fact]
        public void test_delay_inbound_rule_without_rotation_is_unexplained()
        {
            // A rotation-less departure, 70 late at OnStand: all of delta to
            // Unexplained (14 §14.6 step 3).
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1070, Dep, FlightMilestone.OnStand, 1000);
            var rig = new DelayRig(s);
            rig.RunThrough(1070);
            Assert.Equal(new List<string> { Unexplained(2, Dep, 1, 70, 1070) }, LeafTexts(rig, Dep));
        }

        [Fact]
        public void test_delay_job_dependency_wait_is_ignored()
        {
            // 14 §14.5: waitingOn == JobDependency is ignored, open and close
            // alike: a close with no open, a second open on the same key, and an
            // open left open at Airborne all pass without effect.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            s.JobBlocked(1050, Dep, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.JobUnblocked(1060, Dep, JobKind.Catering, ResourceKind.JobDependency, DelayCategory.Catering);
            s.JobBlocked(1070, Dep, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.JobUnblocked(1400, Dep, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.Milestone(1450, Dep, FlightMilestone.Pushback, 1300);
            s.JobBlocked(1460, Dep, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.Milestone(1530, Dep, FlightMilestone.Airborne, 1380);
            var rig = new DelayRig(s);

            rig.RunTo(1001);
            ulong before = rig.Delay.ComputeStateHash();
            rig.RunTo(1450);
            Assert.Equal(before, rig.Delay.ComputeStateHash());

            // Pushback planned 1300, actual 1450: 150 late with no blocking
            // interval: all Unexplained, no TurnaroundJobWait leaf.
            rig.RunThrough(1530);
            Assert.Equal(new List<string> { Unexplained(2, Dep, 1, 150, 1450) }, LeafTexts(rig, Dep));
            Assert.True(rig.Record(Dep).Finalised);
        }

        [Fact]
        public void test_delay_passenger_hold_is_passenger_late_leaf_naming_held_at_node()
        {
            // D6, 14 §14.5/§14.9: OnStand on time at 1000, hold [1200, 1350) with
            // 7 outstanding at node 42. Pushback planned 1250, actual 1400: late
            // 150 = the hold's 150 ticks. Explanation A = heldAt, B = outstanding
            // at the opening.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            Step hold = s.Hold(1200, Dep, 7, 42);
            s.HoldReleased(1350, Dep);
            s.Milestone(1400, Dep, FlightMilestone.Pushback, 1250);
            var rig = new DelayRig(s);
            rig.RunThrough(1400);
            Assert.Equal(
                new List<string> { Leaf(2, Dep, 1, DelayCategory.PassengerLate, 150, true, 0, hold.Ref, DelaySource.PassengerHold, 42, 7, 1400) },
                LeafTexts(rig, Dep));
        }

        [Fact]
        public void test_delay_ids_monotone_and_references_point_backwards()
        {
            // 14 §14.7. Ids start at 1 and follow dispatch order, not FlightId
            // order: flight 30 is published before flight 20.
            var s = new Script();
            s.Plan(0, 30, MovementKind.Departure);
            s.Plan(0, 20, MovementKind.Departure);

            // Same tick, 20's checkpoint dispatched first: its leaf gets the lower id.
            s.Milestone(500, 20, FlightMilestone.OnStand, 450);
            s.Milestone(500, 30, FlightMilestone.OnStand, 400);

            // Roots 41, 42 and 50 are created at tick 600 (ids 5, 6, 7).
            // One handler, InboundAircraft before Unexplained: arrival 41 is 50
            // late (leaf 8 at Landed); departure 42 is 80 late at OnStand:
            // inbound 50 (id 9), then residue 30 (id 10).
            s.Plan(600, 41, MovementKind.Arrival, 42);
            s.Plan(600, 42, MovementKind.Departure, 41);
            s.Milestone(1050, 41, FlightMilestone.Landed, 1000);
            s.Milestone(1100, 41, FlightMilestone.OnStand, 1050);
            s.Milestone(1300, 42, FlightMilestone.OnStand, 1220);

            // One handler, intervals in ascending OpenerId, then Unexplained:
            // rotation-less 50 on time at OnStand 2000; Fuel [2010, 2050) 40, a
            // hold [2030, 2100) owning 2050-2100 = 50; Pushback 20 late beyond:
            // planned 1990, actual 2100: delta 110 → Fuel (11), hold (12),
            // residue 20 (13).
            s.Plan(600, 50, MovementKind.Departure);
            s.Milestone(2000, 50, FlightMilestone.OnStand, 2000);
            s.JobBlocked(2010, 50, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.Hold(2030, 50, 2, 9);
            s.JobUnblocked(2050, 50, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.HoldReleased(2100, 50);
            s.Milestone(2100, 50, FlightMilestone.Pushback, 1990);
            var rig = new DelayRig(s);
            rig.RunThrough(2100);

            Assert.Equal(1UL, rig.Record(30).Root.Value);
            Assert.Equal(2UL, rig.Record(20).Root.Value);
            Assert.Equal(3UL, rig.Leaf(20, DelaySource.Unexplained).Id.Value);
            Assert.Equal(4UL, rig.Leaf(30, DelaySource.Unexplained).Id.Value);
            Assert.Equal(5UL, rig.Record(41).Root.Value);
            Assert.Equal(6UL, rig.Record(42).Root.Value);
            Assert.Equal(7UL, rig.Record(50).Root.Value);
            Assert.Equal(8UL, rig.Leaf(41, DelaySource.Unexplained).Id.Value);
            Assert.Equal(9UL, rig.Leaf(42, DelaySource.InboundAircraft).Id.Value);
            Assert.Equal(50UL, rig.Leaf(42, DelaySource.InboundAircraft).Ticks);
            Assert.Equal(10UL, rig.Leaf(42, DelaySource.Unexplained).Id.Value);
            Assert.Equal(11UL, rig.Leaf(50, DelaySource.TurnaroundJobWait).Id.Value);
            Assert.Equal(12UL, rig.Leaf(50, DelaySource.PassengerHold).Id.Value);
            Assert.Equal(13UL, rig.Leaf(50, DelaySource.Unexplained).Id.Value);
            Assert.Equal(40UL, rig.Leaf(50, DelaySource.TurnaroundJobWait).Ticks);
            Assert.Equal(50UL, rig.Leaf(50, DelaySource.PassengerHold).Ticks);
            Assert.Equal(20UL, rig.Leaf(50, DelaySource.Unexplained).Ticks);

            // Over generated days: every node that appears was created in the
            // handler of the event just dispatched (CreatedAt = its tick), with
            // an id above every id ever seen; Parent and the LinkedFlight's root
            // are strictly lower.
            ulong maxSeen = 0UL;
            var seen = new HashSet<ulong>();
            int created = 0;
            int linked = 0;
            var run = new GeneratedRun(
                0x0024_1D50UL,
                Generator.SyntheticDay,
                model: false,
                perEvent: (d, flight, eventTick, context) =>
                {
                    Assert.True(d.TryGetFlightDelay(new FlightId(flight), out FlightDelay r), context);
                    var ids = new List<DelayEventId> { r.Root };
                    ids.AddRange(d.LeavesOf(new FlightId(flight)));
                    var fresh = new List<DelayNode>();
                    foreach (DelayEventId id in ids)
                    {
                        Assert.True(d.TryGetNode(id, out DelayNode n), context);
                        Assert.True(n.Parent.Value < n.Id.Value, "Parent not below child: " + context);
                        if (n.LinkedFlight.Value != 0UL)
                        {
                            Assert.True(d.TryGetFlightDelay(n.LinkedFlight, out FlightDelay lr) && lr.Root.Value < n.Id.Value, "LinkedFlight's root not below the leaf: " + context);
                            linked++;
                        }

                        if (!seen.Contains(id.Value))
                        {
                            fresh.Add(n);
                        }
                    }

                    fresh.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
                    foreach (DelayNode n in fresh)
                    {
                        Assert.True(n.Id.Value > maxSeen, "new id " + n.Id.Value + " not above every earlier id " + maxSeen + ": " + context);
                        Assert.True(n.CreatedAt == eventTick, "node created outside the dispatching handler's tick: " + context);
                        maxSeen = n.Id.Value;
                        seen.Add(n.Id.Value);
                        created++;
                    }
                });
            run.RunDays(3);
            Assert.True(created > 300 && linked > 20, "generated days created too little: " + created + " nodes, " + linked + " linked leaves seen");
        }
    }
}
