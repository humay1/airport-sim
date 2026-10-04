using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.3 explanation table and §14.5 blocking intervals.</summary>
    public sealed class DelayIntervalTests
    {
        private const ulong Arr = 11UL;
        private const ulong Dep = 12UL;

        /// <summary>One opening event of the given family for flight f at tick t (0 Runway, 1 Taxiway, 2 Stand, 3 Turnaround, 4 Passenger hold).</summary>
        internal static Step Open(Script s, int family, ulong t, ulong f)
        {
            switch (family)
            {
                case 0: return s.RunwayHeld(t, f, 1, 1);
                case 1: return s.TaxiHeld(t, f, 1, 5);
                case 2: return s.StandUnavailable(t, f, null, null);
                case 3: return s.JobBlocked(t, f, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
                default: return s.Hold(t, f, 3, 9);
            }
        }

        /// <summary>The closing event of the same family and key.</summary>
        internal static Step Close(Script s, int family, ulong t, ulong f)
        {
            switch (family)
            {
                case 0: return s.RunwayReleased(t, f, 1);
                case 1: return s.TaxiReleased(t, f, 1);
                case 2: return s.StandAssigned(t, f, null);
                case 3: return s.JobUnblocked(t, f, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
                default: return s.HoldReleased(t, f);
            }
        }

        [Fact]
        public void test_delay_interval_families_carry_category_source_and_explanation()
        {
            // 14 §14.3's explanation table and §14.5's categories, every family.
            // Arrival 11: runway hold rw 2, queue position 3, [500, 600). Landed
            // planned 600, actual 700: delta 100, runway owns 100.
            // OnStand planned 1000, actual 1400: delta 300 over W = [700, 1400):
            // taxi edge 5 by flight 77 [1100, 1150) = 50, taxi edge 6 with no
            // blocker [1150, 1170) = 20, stand null [1200, 1260) = 60, stand 4
            // occupied by 88 [1260, 1300) = 40 (A = StandId + 1); residue 130.
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            Step rw = s.RunwayHeld(500, Arr, 2, 3);
            s.RunwayReleased(600, Arr, 2);
            s.Milestone(700, Arr, FlightMilestone.Landed, 600);
            Step t5 = s.TaxiHeld(1100, Arr, 5, 77);
            s.TaxiReleased(1150, Arr, 5);
            Step t6 = s.TaxiHeld(1150, Arr, 6, null);
            s.TaxiReleased(1170, Arr, 6);
            Step sn = s.StandUnavailable(1200, Arr, null, null);
            s.StandAssigned(1260, Arr, null);
            Step s4 = s.StandUnavailable(1260, Arr, 4, 88);
            s.StandAssigned(1300, Arr, 4);
            s.Milestone(1400, Arr, FlightMilestone.OnStand, 1000);

            // Departure 12, rotation-less, on time at OnStand 2000. Fuel waits on
            // a vehicle with category Loading, PushbackPrep on crew with category
            // GroundHandling: the category is the event's own field, never mapped
            // from JobKind. Pushback planned 2000, actual 2100: delta 100 =
            // Fuel [2010, 2060) 50 + PushbackPrep [2060, 2110) clipped to 2100: 40,
            // residue 10.
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(2000, Dep, FlightMilestone.OnStand, 2000);
            Step fuel = s.JobBlocked(2010, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Loading, 3);
            s.JobUnblocked(2060, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Loading, 3);
            Step prep = s.JobBlocked(2060, Dep, JobKind.PushbackPrep, ResourceKind.Crew, DelayCategory.GroundHandling);
            s.Milestone(2100, Dep, FlightMilestone.Pushback, 2000);
            s.JobUnblocked(2110, Dep, JobKind.PushbackPrep, ResourceKind.Crew, DelayCategory.GroundHandling);
            var rig = new DelayRig(s);
            rig.RunThrough(2200);

            // Roots: 11 → 1, 12 → 2. Arrival leaves 3 (Landed), then 4-8 at OnStand
            // in ascending OpenerId, Unexplained last.
            Assert.Equal(
                new List<string>
                {
                    Leaf(3, Arr, 1, DelayCategory.RunwayCongestion, 100, true, 0, rw.Ref, DelaySource.RunwayHold, 2, 3, 700),
                    Leaf(4, Arr, 1, DelayCategory.TaxiCongestion, 50, true, 0, t5.Ref, DelaySource.TaxiwayHold, 5, 77, 1400),
                    Leaf(5, Arr, 1, DelayCategory.TaxiCongestion, 20, true, 0, t6.Ref, DelaySource.TaxiwayHold, 6, 0, 1400),
                    Leaf(6, Arr, 1, DelayCategory.StandUnavailable, 60, true, 0, sn.Ref, DelaySource.StandUnavailable, 0, 0, 1400),
                    Leaf(7, Arr, 1, DelayCategory.StandUnavailable, 40, true, 0, s4.Ref, DelaySource.StandUnavailable, 5, 88, 1400),
                    Unexplained(8, Arr, 1, 130, 1400),
                },
                LeafTexts(rig, Arr));
            Assert.Equal(
                new List<string>
                {
                    Job(9, Dep, 2, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Loading, 50, fuel, 2100),
                    Job(10, Dep, 2, JobKind.PushbackPrep, ResourceKind.Crew, DelayCategory.GroundHandling, 40, prep, 2100),
                    Unexplained(11, Dep, 2, 10, 2100),
                },
                LeafTexts(rig, Dep));
        }

        [Fact]
        public void test_delay_interval_open_across_checkpoint_accumulates_in_one_leaf()
        {
            // A stand wait [900, 1100) spans Landed. Landed planned 950, actual
            // 1000: delta 50, the wait owns 100 of [0, 1000): capped to 50. It is
            // still retained (closed after Landed). OnStand planned 1050, actual
            // 1200: delta 100 over [1000, 1200): the wait owns 1000-1100 = 100,
            // added to the same leaf (one leaf per OpenerId): 150.
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            Step wait = s.StandUnavailable(900, Arr, null, 55);
            s.Milestone(1000, Arr, FlightMilestone.Landed, 950);
            s.StandAssigned(1100, Arr, null);
            s.Milestone(1200, Arr, FlightMilestone.OnStand, 1050);
            var rig = new DelayRig(s);
            rig.RunThrough(1200);
            Assert.Equal(
                new List<string> { Leaf(2, Arr, 1, DelayCategory.StandUnavailable, 150, true, 0, wait.Ref, DelaySource.StandUnavailable, 0, 55, 1000) },
                LeafTexts(rig, Arr));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void test_delay_interval_duplicate_open_key_throws_with_tick(int family)
        {
            // 14 §14.5: two open intervals with the same key are an emitter bug.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            Open(s, family, 1100, Dep);
            Open(s, family, 1150, Dep);
            new DelayRig(s).AssertThrowsAt(1150, "second open of family " + family);
        }

        [Fact]
        public void test_delay_interval_same_family_other_key_is_a_second_interval()
        {
            // Keys, not families: runway 1 and runway 2 open together, and one
            // flight's Fuel and Catering waits open together, without a throw.
            // OnStand on time at 1000; Fuel [1100, 1200), Catering [1150, 1250):
            // Pushback planned 1150, actual 1300: delta 150 = Fuel 100 + Catering
            // 50 (1200-1250).
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            s.JobBlocked(1100, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobBlocked(1150, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobUnblocked(1200, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobUnblocked(1250, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(1300, Dep, FlightMilestone.Pushback, 1150);
            s.RunwayHeld(1310, Dep, 1, 1);
            s.RunwayHeld(1320, Dep, 2, 1);
            s.RunwayReleased(1330, Dep, 1);
            s.RunwayReleased(1340, Dep, 2);
            s.Milestone(1400, Dep, FlightMilestone.Airborne, 1230);
            var rig = new DelayRig(s);
            rig.RunThrough(1400);
            List<DelayNode> jobs = rig.Leaves(Dep).FindAll(n => n.Explanation.Source == DelaySource.TurnaroundJobWait);
            Assert.Equal(2, jobs.Count);
            Assert.Equal(100UL, jobs[0].Ticks);
            Assert.Equal(50UL, jobs[1].Ticks);

            // Airborne planned 1230, actual 1400: late 170, delta 20 over
            // [1300, 1400): runway 1 [1310, 1330) owns 20, runway 2 owns
            // 1330-1340 = 10 but the cap leaves it nothing.
            List<DelayNode> runway = rig.Leaves(Dep).FindAll(n => n.Explanation.Source == DelaySource.RunwayHold);
            Assert.Single(runway);
            Assert.Equal(20UL, runway[0].Ticks);
            Assert.Equal(1UL, runway[0].Explanation.A);
            Assert.True(rig.Record(Dep).Finalised);
        }

        [Fact]
        public void test_delay_interval_for_unknown_flight_throws_with_tick()
        {
            // 14 §14.5: an interval for a flight with no FlightPlanPublished.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.RunwayHeld(1100, 99, 1, 1);
            new DelayRig(s).AssertThrowsAt(1100, "interval for unknown flight 99");
        }

        [Fact]
        public void test_delay_interval_for_pruned_flight_throws_with_tick()
        {
            // 14 §14.5 "or already pruned". Rotation-less arrival 21 finalised at
            // 200 (day 0) is pruned at the first tick of day 2 (28800, §14.8).
            var s = new Script();
            s.Plan(0, 21, MovementKind.Arrival);
            s.Milestone(100, 21, FlightMilestone.Landed, 100);
            s.Milestone(200, 21, FlightMilestone.OnStand, 200);
            s.TaxiHeld(28900, 21, 1, null);
            var rig = new DelayRig(s);
            rig.RunTo(28900);
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(21), out _), "flight 21 should have been pruned at 28800");
            rig.AssertThrowsAt(28900, "interval for pruned flight 21");
        }

        [Fact]
        public void test_delay_interval_on_finalised_flight_is_ignored_with_its_pair()
        {
            // 14 §14.5: after finalisation, opens and closes are ignored, as is a
            // close whose open was discarded at finalisation, and a close with no
            // open at all. Nothing changes: not the tree, not the hashed state.
            // Arrival: stand wait opened at 950 (Landed 960 on time, so it gets
            // nothing), still open at the terminal OnStand (1000, on time).
            var s = new Script();
            s.Plan(0, Arr, MovementKind.Arrival);
            s.StandUnavailable(950, Arr, null, null);
            s.Milestone(960, Arr, FlightMilestone.Landed, 960);
            s.Milestone(1000, Arr, FlightMilestone.OnStand, 1000);
            s.StandAssigned(1050, Arr, null);
            s.JobBlocked(1100, Arr, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4);
            s.JobBlocked(1110, Arr, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4);
            s.JobUnblocked(1200, Arr, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4);
            s.TaxiReleased(1300, Arr, 5);
            s.Hold(1310, Arr, 2, 3);
            var rig = new DelayRig(s);
            rig.RunThrough(1000);
            Assert.True(rig.Record(Arr).Finalised);
            string tree = Show.Tree(rig.Delay, Arr);
            ulong hash = rig.Delay.ComputeStateHash();

            rig.RunThrough(1400);
            Assert.Equal(tree, Show.Tree(rig.Delay, Arr));
            Assert.Equal(hash, rig.Delay.ComputeStateHash());
            Assert.Empty(rig.Leaves(Arr));
        }
    }
}
