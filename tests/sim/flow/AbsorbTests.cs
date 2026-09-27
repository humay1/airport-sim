using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// Absorb (09 §9.7): boards the flight's Departing passengers on any Gate
    /// node, returns their count, and reports everyone else of the flight as
    /// PassengersMissedFlight. A cohort removed with an open blocking episode
    /// emits FlowUnblocked first, at the same tick (§9.12, 10 §10.3 rule 2).
    /// Absorb is a downward call made during a tick, so it runs in the
    /// position-2 probe here.
    /// </summary>
    public sealed class AbsorbTests
    {
        private static int AbsorbInTick(Rig rig, ulong flight)
        {
            int boarded = -1;
            rig.Inject = (in TickContext ctx) => boarded = rig.Flow.Absorb(new NodeId(4), new FlightId(flight));
            rig.Step(1);
            rig.Inject = null;
            return boarded;
        }

        /// <summary>
        /// Total missed for a flight. 09 does not say whether one event or one per
        /// cohort is emitted, so only the total is asserted.
        /// </summary>
        private static int MissedCount(FlowEvents ev, ulong flight)
        {
            int n = 0;
            foreach (var m in ev.Missed)
            {
                Assert.Equal(flight, m.E.Flight.Value);
                n += m.E.Count;
            }

            return n;
        }

        [Fact]
        public void test_absorb_boards_gate_passengers_and_reports_the_rest_missed()
        {
            // One lane at 60 pax/min, capacity large.
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60))));
            rig.InjectNow(1, 5, 1);
            rig.InjectNow(1, 3, 2);
            rig.Step(8);
            Assert.Equal(8, rig.Pop(3));
            // One PassengersArrivedAtGate per cohort entering the gate (§9.12),
            // with that cohort's count.
            int arrived = 0;
            foreach (var a in rig.Events!.Arrived)
            {
                arrived += a.E.Count;
            }

            Assert.Equal(8, arrived);

            // Close the lane; four more of flight 1 wait in the queue.
            Assert.True(rig.Submit(2, 0, out _));
            rig.Step(1);
            rig.InjectNow(1, 4, 1);
            rig.Step(6);
            Assert.Equal(4, rig.Pop(2));
            Assert.True(rig.Flow.TryGetOutstanding(new FlightId(1), out OutstandingPassengers before));
            Assert.Equal(4, before.Count);

            int boarded = AbsorbInTick(rig, 1);
            Assert.Equal(5, boarded);
            Assert.Equal(3, rig.Pop(3));
            Assert.Equal(0, rig.Pop(2));
            Assert.False(rig.Flow.TryGetOutstanding(new FlightId(1), out _));
            Assert.Equal(4, MissedCount(rig.Events, 1));
            int missedEvents = rig.Events.Missed.Count;

            // Flight 2 is untouched and boards alone.
            Assert.Equal(3, AbsorbInTick(rig, 2));
            Assert.Equal(missedEvents, rig.Events.Missed.Count);
            Assert.Equal(0, rig.Pop(3));
        }

        [Fact]
        public void test_absorb_closes_an_open_blocking_episode_first()
        {
            // Queue closed with capacity 1: flight 1's second cohort is held at
            // the source. Absorbing flight 1 removes it: FlowUnblocked, then
            // PassengersMissedFlight, at the same tick.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60), 1)));
            rig.InjectNow(1, 1, 1);
            rig.Step(4);
            rig.InjectNow(1, 2, 1);
            rig.Step(4);
            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Blocked);

            ulong t = rig.NextTick;
            Assert.Equal(0, AbsorbInTick(rig, 1));
            Assert.Single(ev.Unblocked);
            Assert.Equal(t, ev.Unblocked[0].Tick);
            Assert.Equal(ev.Blocked[0].E.Cohort, ev.Unblocked[0].E.Cohort);
            Assert.Equal(ev.Blocked[0].E.Held, ev.Unblocked[0].E.Held);
            Assert.Equal(ev.Blocked[0].E.BlockedBy, ev.Unblocked[0].E.BlockedBy);
            Assert.Equal(3, MissedCount(ev, 1));
            Assert.All(ev.Missed, m => Assert.Equal(t, m.Tick));
            // The held cohort's Unblocked precedes its own missed report. Missed
            // events carry no cohort id, so the test can only require that some
            // missed report follows the Unblocked in the same tick.
            int unblocked = ev.Log.FindIndex(l => l.StartsWith(t + " unblocked"));
            int lastMissed = ev.Log.FindLastIndex(l => l.StartsWith(t + " missed"));
            Assert.True(unblocked >= 0 && unblocked < lastMissed, string.Join("\n", ev.Log));
            Assert.Equal(0, rig.Pop(1));
            Assert.Equal(0, rig.Pop(2));
        }

        [Fact]
        public void test_absorb_outstanding_names_the_node_holding_most()
        {
            // 09 §9.7a: outstanding = Departing passengers of the flight not on a
            // Gate; MostHeldAt = the node holding most, ties by ascending NodeId.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60), 5)));
            Assert.False(rig.Flow.TryGetOutstanding(new FlightId(1), out _));
            rig.InjectNow(1, 5, 1);
            rig.Step(4);
            rig.InjectNow(1, 3, 1);
            rig.Step(1);
            Assert.True(rig.Flow.TryGetOutstanding(new FlightId(1), out OutstandingPassengers o));
            Assert.Equal(new FlightId(1), o.Flight);
            Assert.Equal(8, o.Count);
            Assert.Equal(new NodeId(2), o.MostHeldAt);

            // Tie: 5 on the queue, 5 at the source -> the smaller NodeId, 1.
            rig.InjectNow(1, 2, 1);
            rig.Step(1);
            Assert.True(rig.Flow.TryGetOutstanding(new FlightId(1), out o));
            Assert.Equal(10, o.Count);
            Assert.Equal(new NodeId(1), o.MostHeldAt);
            Assert.False(rig.Flow.TryGetOutstanding(new FlightId(2), out _));
        }
    }
}
