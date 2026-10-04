using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.6 step 3: the rotation arrival must exist and be finalised when a late departure reaches OnStand.</summary>
    public sealed class DelayDepartureTests
    {
        [Fact]
        public void test_delay_departure_on_stand_with_unfinalised_inbound_throws_with_tick()
        {
            // Arrival 11 has landed but is not on stand; its rotation 12 reaches
            // OnStand 100 late.
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival, 12);
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Milestone(900, 11, FlightMilestone.Landed, 900);
            s.Milestone(1100, 12, FlightMilestone.OnStand, 1000);
            new DelayRig(s).AssertThrowsAt(1100, "departure OnStand before its inbound finalised");
        }

        [Fact]
        public void test_delay_departure_on_stand_with_unknown_inbound_throws_with_tick()
        {
            // Rotation 11 was never published.
            var s = new Script();
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Milestone(1100, 12, FlightMilestone.OnStand, 1000);
            new DelayRig(s).AssertThrowsAt(1100, "departure OnStand with no inbound record");
        }
    }
}
