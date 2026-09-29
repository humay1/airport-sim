using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.13's binding requirements on the Phase 0/1 airside fixture,
    /// asserted on the layout the suite uses. The runway-hold requirement is
    /// asserted by the headless day (AirsideHeadlessDayTests), since it is a
    /// property of a run against tests/fixtures/schedule/phase0-200.csv.
    /// </summary>
    public sealed class FixtureLayoutTests
    {
        [Fact]
        public void test_fixture_layout_meets_spec_requirements()
        {
            AirsideLayout layout = FixtureLayout.Layout();

            // Exactly one RunwayDef.
            Assert.Single(layout.Runways);
            ushort threshold = layout.Runways[0].ThresholdNode.Value;

            // Four stands, reached from the threshold through at least one junction.
            Assert.Equal(4, layout.Stands.Count);
            Assert.Contains(layout.Nodes, n => n.Kind == TaxiNodeKind.Junction);
            foreach (StandDef s in layout.Stands)
            {
                Assert.NotEqual(ulong.MaxValue, Routes.LeastTicks(layout, threshold, s.Node.Value));
                Assert.NotEqual(ulong.MaxValue, Routes.LeastTicks(layout, s.Node.Value, threshold));
            }

            // At least one edge shared by more than one threshold-to-stand route.
            Dictionary<ushort, int> use = Routes.EdgeUse(layout, threshold);
            Assert.Contains(use, kv => kv.Value > 1);

            // At least one stand excludes an aircraft type the schedule fixture uses.
            SortedSet<string> types = ScheduleFixture.AircraftTypes();
            Assert.Contains(
                layout.Stands,
                s => types.Any(t => !AirsideContent.Fits(t, s.MaxAircraftSizeCategory.Value)));

            // The declared capacity is tighter than the 06:00 bank's 3-minute spacing.
            Assert.True(AirConst.MinSeparation(layout.Runways[0].DeclaredCapacityPerHour) > 3UL * AirConst.TicksPerMinute);
        }

        [Fact]
        public void test_fixture_layout_route_ticks_match_least_traversal_ticks()
        {
            // The suite's RouteTicks table must equal the least-TraversalTicks
            // route of 12 §12.4, both ways, or the planned-tick oracle is wrong.
            AirsideLayout layout = FixtureLayout.Layout();
            for (ushort s = FixtureLayout.S1; s <= FixtureLayout.S4; s++)
            {
                Assert.Equal(FixtureLayout.RouteTicks(s), Routes.LeastTicks(layout, FixtureLayout.Threshold, FixtureLayout.StandNode(s)));
                Assert.Equal(FixtureLayout.RouteTicks(s), Routes.LeastTicks(layout, FixtureLayout.StandNode(s), FixtureLayout.Threshold));
            }
        }
    }
}
