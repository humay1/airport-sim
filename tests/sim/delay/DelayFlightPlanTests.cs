using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.12 FlightPlanPublished, §14.3 FlightDelay, §14.10 queries.</summary>
    public sealed class DelayFlightPlanTests
    {
        [Fact]
        public void test_delay_flight_plan_published_creates_record_and_zero_root()
        {
            // Published in the order 12, 11, 21 at tick 250: roots 1, 2, 3 in
            // dispatch order (§14.7), queried back in ascending FlightId.
            var s = new Script();
            s.Plan(250, 12, MovementKind.Departure, 11);
            s.Plan(250, 11, MovementKind.Arrival, 12);
            s.Plan(250, 21, MovementKind.Arrival);
            var rig = new DelayRig(s);

            rig.RunTo(250);
            Assert.Empty(rig.Delay.RetainedFlights());
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(12), out _));
            rig.RunThrough(250);

            Assert.Equal(new List<FlightId> { new FlightId(11), new FlightId(12), new FlightId(21) }, new List<FlightId>(rig.Delay.RetainedFlights()));
            Assert.Equal(
                Show.RecordLine(12, MovementKind.Departure, true, 11, 1, 0, Fx.Zero, 0, 250, false, DConst.TickUnscheduled, 0, null),
                Show.Record(rig.Record(12)));
            Assert.Equal(
                Show.RecordLine(11, MovementKind.Arrival, true, 12, 2, 0, Fx.Zero, 0, 250, false, DConst.TickUnscheduled, 0, null),
                Show.Record(rig.Record(11)));
            Assert.Equal(
                Show.RecordLine(21, MovementKind.Arrival, false, 21, 3, 0, Fx.Zero, 0, 250, false, DConst.TickUnscheduled, 0, null),
                Show.Record(rig.Record(21)));
            Assert.Equal(Root(1, 12, 0, 250), Show.Node(rig.Node(new DelayEventId(1))));
            Assert.Equal(Root(2, 11, 0, 250), Show.Node(rig.Node(new DelayEventId(2))));
            Assert.Equal(Root(3, 21, 0, 250), Show.Node(rig.Node(new DelayEventId(3))));
            Assert.Empty(rig.Delay.LeavesOf(new FlightId(12)));

            // Unknown flight and unallocated ids (DELAY_EVENT_ID_NONE included).
            Assert.False(rig.Delay.TryGetFlightDelay(new FlightId(99), out _));
            Assert.Empty(rig.Delay.LeavesOf(new FlightId(99)));
            Assert.False(rig.Delay.TryGetNode(new DelayEventId(0), out _));
            Assert.False(rig.Delay.TryGetNode(new DelayEventId(4), out _));
        }

        [Fact]
        public void test_delay_flight_plan_published_twice_throws_with_tick()
        {
            var s = new Script();
            s.Plan(0, 12, MovementKind.Departure);
            s.Plan(300, 12, MovementKind.Departure);
            new DelayRig(s).AssertThrowsAt(300, "second FlightPlanPublished for flight 12");
        }
    }
}
