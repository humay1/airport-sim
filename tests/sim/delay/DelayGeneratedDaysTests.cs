using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 14 §14.4–§14.9 as a whole, on the seeded generator's streams (§14.14):
    /// the tree, record and publications match the reference model in
    /// DelayModel.cs after every event, and every invariant holds.
    /// </summary>
    public sealed class DelayGeneratedDaysTests
    {
        private static void RunAndCompare(ulong seed, int lastDay)
        {
            var run = new GeneratedRun(
                seed,
                Generator.SyntheticDay,
                model: true,
                perEvent: (d, flight, tick, context) => Invariants.CheckFlight(d, flight, context));
            var coverage = new HashSet<DelaySource>();
            run.RunDays(lastDay, day =>
            {
                run.Model!.PruneThrough(run.Rig.Host.CurrentTick - 1UL);
                Assert.Equal(run.Model.Snapshot(), Show.Snapshot(run.Rig.Delay));
                Invariants.CheckAll(run.Rig.Delay, run.Context(day));
                foreach (DelayNode n in Invariants.AllNodes(run.Rig.Delay))
                {
                    coverage.Add(n.Explanation.Source);
                }
            });

            var published = new List<string>();
            foreach ((EventEnvelope env, DelayEvent evt) in run.Rig.R.Delays)
            {
                Assert.Equal(DConst.DelayPos, env.Source.Value);
                published.Add(DelayModel.PublishedText(env.Tick, env.Cause.Id, evt.Node));
            }

            Assert.Equal(run.Model!.Published, published);

            // The streams reached every DelaySource (§14.14: leaf coverage per
            // source is the synthetic streams' job).
            foreach (DelaySource src in new[]
            {
                DelaySource.FlightTotal, DelaySource.InboundAircraft, DelaySource.RunwayHold, DelaySource.TaxiwayHold,
                DelaySource.StandUnavailable, DelaySource.TurnaroundJobWait, DelaySource.Unexplained, DelaySource.PassengerHold,
            })
            {
                Assert.True(coverage.Contains(src), "no " + src + " node was ever retained at a day end; the generator does not cover it");
            }

            Assert.True(run.Model.Published.Count > 300, "too few publications: " + run.Model.Published.Count);
        }

        [Fact]
        public void test_delay_generated_days_match_reference_model()
        {
            RunAndCompare(0x0024_9E9EUL, 7);
        }

        [Fact]
        public void test_delay_generated_days_match_reference_model_second_seed()
        {
            RunAndCompare(0x0024_5EC0_4D00UL, 7);
        }
    }
}
