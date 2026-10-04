using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.5: a closing event with no open interval of its key, for a flight that is not finalised.</summary>
    public sealed class DelayUnmatchedTests
    {
        private const ulong Dep = 12UL;

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void test_delay_unmatched_close_event_throws_with_tick(int variant)
        {
            // Variants 0-4: each family's close with nothing open. 5: a runway
            // hold on runway 1 "closed" on runway 2. 6: a Fuel wait "closed" as
            // Catering. Pairing is by key (§14.5), so 5 and 6 are unmatched too.
            var s = new Script();
            s.Plan(0, Dep, MovementKind.Departure);
            s.Milestone(1000, Dep, FlightMilestone.OnStand, 1000);
            if (variant <= 4)
            {
                DelayIntervalTests.Close(s, variant, 1100, Dep);
            }
            else if (variant == 5)
            {
                s.RunwayHeld(1050, Dep, 1, 1);
                s.RunwayReleased(1100, Dep, 2);
            }
            else
            {
                s.JobBlocked(1050, Dep, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
                s.JobUnblocked(1100, Dep, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            }

            new DelayRig(s).AssertThrowsAt(1100, "unmatched close, variant " + variant);
        }
    }
}
