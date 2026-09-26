using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>FlightMilestone, 10 §10.4, relocated to sim.core (Q-018).</summary>
    public sealed class FlightMilestoneTests
    {
        [Fact]
        public void test_flight_milestone_enum_matches_spec_thirteen_values_ordinal_order()
        {
            Assert.Empty(PayloadShapes.EnumViolations(typeof(FlightMilestone), PayloadShapes.FlightMilestoneNames));
            Assert.Equal(13, Enum.GetValues(typeof(FlightMilestone)).Length);
            Assert.Equal(0, (int)FlightMilestone.PlanPublished);
            Assert.Equal(12, (int)FlightMilestone.Airborne);
        }
    }
}
