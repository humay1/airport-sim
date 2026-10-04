using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>06 rule 5 and 14 §14.7 "Depth".</summary>
    public sealed class DepthTests
    {
        [Fact]
        public void test_depth_capped()
        {
            // ChainDepth (derived: 1 with no leaves, else max over leaves of 2 or
            // 2 + ChainDepth(LinkedFlight)) never exceeds MAX_ATTRIBUTION_DEPTH,
            // after every handler, over eight generated days. At Phase 0/1 an
            // arrival's depth is at most 2 and a departure's at most 4 (§14.7):
            // both bounds are asserted, and depth 4 must actually occur.
            int deepest = 0;
            var run = new GeneratedRun(
                0x0024_DE97UL,
                Generator.SyntheticDay,
                model: false,
                perEvent: (d, flight, tick, context) =>
                {
                    Assert.True(d.TryGetFlightDelay(new FlightId(flight), out FlightDelay r), context);
                    int depth = Invariants.ChainDepth(d, flight, 0);
                    Assert.True(depth <= DConst.MaxAttributionDepth, "ChainDepth " + depth + ": " + context);
                    Assert.True(depth <= (r.Kind == MovementKind.Arrival ? 2 : 4), "Phase 1 depth bound broken (" + depth + "): " + context);
                    if (depth > deepest)
                    {
                        deepest = depth;
                    }
                });
            run.RunDays(7);
            Assert.Equal(4, deepest);
        }
    }
}
