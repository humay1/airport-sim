using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 14 §14.8 "Retention and pruning": at the first tick of each sim-day a
    /// rotation pair, or a single rotation-less flight, is pruned when every
    /// member is finalised with FinalisedAt &lt; (DayIndex − 1) × TICKS_PER_SIM_DAY
    /// (DELAY_RETENTION_DAYS = 2).
    /// </summary>
    public sealed class DelayRotationTests
    {
        private static List<ulong> Retained(DelayRig rig)
        {
            return new List<FlightId>(rig.Delay.RetainedFlights()).ConvertAll(f => f.Value);
        }

        [Fact]
        public void test_delay_rotation_pair_pruned_together_after_retention()
        {
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival, 12);
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Plan(0, 21, MovementKind.Arrival);
            s.Plan(0, 31, MovementKind.Arrival);
            s.Plan(0, 41, MovementKind.Departure);
            s.Plan(0, 51, MovementKind.Arrival, 52);
            s.Plan(0, 52, MovementKind.Departure, 51);

            // 11: finalised at 1000 (day 0), 50 late, so it has a leaf.
            s.Milestone(950, 11, FlightMilestone.Landed, 900);
            s.Milestone(1000, 11, FlightMilestone.OnStand, 950);

            // 12, its rotation: finalised at 15000 (day 1).
            s.Milestone(1100, 12, FlightMilestone.OnStand, 1100);
            s.Milestone(14000, 12, FlightMilestone.Pushback, 14000);
            s.Milestone(15000, 12, FlightMilestone.Airborne, 15000);

            // 21: rotation-less, finalised at 14399, the last tick of day 0.
            s.Milestone(14000, 21, FlightMilestone.Landed, 14000);
            s.Milestone(14399, 21, FlightMilestone.OnStand, 14399);

            // 31: rotation-less, finalised at 14400, the first tick of day 1.
            s.Milestone(14300, 31, FlightMilestone.Landed, 14300);
            s.Milestone(14400, 31, FlightMilestone.OnStand, 14400);

            // 41: rotation-less, never finalised.
            s.Milestone(500, 41, FlightMilestone.OnStand, 500);

            // 51 finalised on day 0, its rotation 52 never: never pruned.
            s.Milestone(550, 51, FlightMilestone.Landed, 550);
            s.Milestone(600, 51, FlightMilestone.OnStand, 600);
            s.Milestone(700, 52, FlightMilestone.OnStand, 700);
            var rig = new DelayRig(s);

            rig.RunTo(28800);
            var all = new List<ulong> { 11, 12, 21, 31, 41, 51, 52 };
            Assert.Equal(all, Retained(rig));
            DelayEventId leaf11 = rig.Leaf(11, DelaySource.Unexplained).Id;
            DelayEventId root11 = rig.Record(11).Root;
            DelayEventId root12 = rig.Record(12).Root;
            DelayEventId root21 = rig.Record(21).Root;
            DelayEventId root31 = rig.Record(31).Root;

            // Day 2 starts at 28800: threshold 14400. 21 (14399) goes. 11 (1000)
            // would, but its rotation 12 (15000) stays, so both stay. 31 (14400)
            // is not strictly before the threshold.
            rig.RunThrough(28800);
            Assert.Equal(new List<ulong> { 11, 12, 31, 41, 51, 52 }, Retained(rig));
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(21), out _));
            Assert.False(rig.Delay.TryGetNode(root21, out _));
            Assert.Empty(rig.Delay.LeavesOf(new FlightId(21)));
            Assert.True(rig.Delay.TryGetNode(leaf11, out _));

            // Day 3 starts at 43200: threshold 28800. The pair 11/12 goes
            // together, and 31. 41 and the pair 51/52 are unfinalised.
            rig.RunTo(43200);
            Assert.Equal(new List<ulong> { 11, 12, 31, 41, 51, 52 }, Retained(rig));
            rig.RunThrough(43200);
            Assert.Equal(new List<ulong> { 41, 51, 52 }, Retained(rig));
            foreach (DelayEventId gone in new[] { root11, leaf11, root12, root31 })
            {
                Assert.False(rig.Delay.TryGetNode(gone, out _), "node " + gone.Value + " survived pruning");
            }

            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(11), out _));
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(12), out _));
            Assert.Empty(rig.Delay.LeavesOf(new FlightId(11)));
            Invariants.CheckAll(rig.Delay, "after pruning at 43200");
        }
    }
}
