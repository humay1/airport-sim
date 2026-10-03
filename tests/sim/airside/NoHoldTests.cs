using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8: "sim.flow not registered, or BoardingHoldMaxMinutes == 0: no
    /// hold, and neither event is emitted"; the doors close at the
    /// doors-close point exactly as without the hold. §12.12a: flow null
    /// means no Inject/Absorb calls and no hold.
    /// </summary>
    public sealed class NoHoldTests
    {
        private static void AssertClosedAtPointWithoutHold(HostRig rig)
        {
            ulong h = DepartureTests.DoorsClosePoint(rig);
            Assert.Equal(h, rig.Rec.Milestone(DepartureTests.Rd, FlightMilestone.DoorsClosed).Milestone.ActualTick);
            Assert.Equal(h, rig.Rec.Milestone(DepartureTests.Rd, FlightMilestone.Pushback).Milestone.ActualTick);
            Assert.Empty(rig.Rec.Of<DepartureHeldForPassengers>());
            Assert.Empty(rig.Rec.Of<DepartureHeldForPassengersReleased>());
        }

        [Fact]
        public void test_no_hold_when_flow_absent_or_hold_max_zero()
        {
            // sim.flow absent: nobody to wait for.
            HostRig absent = DepartureTests.Rig(flow: null, holdMinutes: 10U);
            absent.RunTo(AirConst.TicksPerDay);
            AssertClosedAtPointWithoutHold(absent);

            // sim.flow present with passengers outstanding throughout, hold max 0.
            var flow = ScriptedFlow.For(DepartureTests.Rd, ulong.MaxValue, 7, 77U);
            HostRig zero = DepartureTests.Rig(flow, holdMinutes: 0U);
            ulong trackedTicks = 0UL;
            zero.StepEach(AirConst.TicksPerDay, t =>
            {
                if (zero.Airside.TryGetTrack(new FlightId(DepartureTests.Rd), out AircraftTrack tr))
                {
                    Assert.Equal(AirConst.TickUnscheduled, tr.PassengerHoldSince);
                    trackedTicks++;
                }
            });
            Assert.True(trackedTicks > 0UL, "the departure was never tracked");
            AssertClosedAtPointWithoutHold(zero);

            ulong h = DepartureTests.DoorsClosePoint(zero);
            var absorbs = flow.AbsorbsOf(DepartureTests.Rd);
            Assert.Single(absorbs);
            Assert.Equal(h, absorbs[0].Tick);
        }
    }
}
