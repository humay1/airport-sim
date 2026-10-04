using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>06 rule 1 and 14 §14.6: the leaves partition the total exactly, in integer ticks.</summary>
    public sealed class SumOfLeavesTests
    {
        [Fact]
        [Trait("Category", "Slow")]
        public void test_sum_of_leaves_equals_total()
        {
            // 06 "Tests the Test Author must write": over 1000 randomly generated
            // flight days (14 §14.14's seeded generator, one continuous run so
            // that retention, pruning and unfinalised flights carry across
            // days), after every handler: the touched flight's leaf ticks sum to
            // its TotalTicks, which equals its FlightTotal's ticks. At the end of
            // every day, every retained flight. Slow by 07 L11a rule (a): 1000
            // sim-days is far above 144 000 ticks.
            const int Days = 1000;
            long nonZero = 0;
            var run = new GeneratedRun(
                0x0024_5000_0001UL,
                Generator.SyntheticDay,
                model: false,
                perEvent: (d, flight, tick, context) =>
                {
                    Assert.True(d.TryGetFlightDelay(new FlightId(flight), out FlightDelay r), context);
                    Assert.True(d.TryGetNode(r.Root, out DelayNode root), "no root: " + context);
                    ulong sum = 0UL;
                    foreach (DelayEventId id in d.LeavesOf(new FlightId(flight)))
                    {
                        Assert.True(d.TryGetNode(id, out DelayNode n), "leaf missing: " + context);
                        sum += n.Ticks;
                    }

                    Assert.True(sum == r.TotalTicks && root.Ticks == r.TotalTicks, "sum of leaves " + sum + ", total " + r.TotalTicks + ", root " + root.Ticks + ": " + context + "\n" + Show.Tree(d, flight));
                    if (r.TotalTicks > 0UL)
                    {
                        nonZero++;
                    }
                });
            run.RunDays(Days - 1, day => Invariants.CheckAll(run.Rig.Delay, run.Context(day)));
            Assert.True(run.EventsChecked > 100000L && nonZero > 10000L, "the generated days did too little: " + run.EventsChecked + " events, " + nonZero + " late states");
        }
    }
}
