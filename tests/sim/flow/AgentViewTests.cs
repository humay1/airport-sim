using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// AgentsAt (09 §9.7 "Promotion rules", Q-033): on a promoted node it
    /// returns exactly one view per passenger, so its count equals
    /// Population(node). It is sorted by (Cohort, Index), and Index runs from
    /// 0 to Count - 1 in each cohort. Each view is of that node (§9.1:
    /// PassengerRef = (CohortId, indexWithinCohort)).
    /// </summary>
    public sealed class AgentViewTests
    {
        /// <summary>
        /// The views §9.7 requires on a promoted node: for each cohort in
        /// CohortsAt order (ascending CohortId, §9.3), Index 0 .. Count - 1.
        /// </summary>
        private static List<(ulong Cohort, int Index)> Expected(Rig rig, uint node)
        {
            var expected = new List<(ulong, int)>();
            foreach (PassengerCohort c in rig.CohortsOn(node))
            {
                for (int i = 0; i < c.Count; i++)
                {
                    expected.Add((c.Id.Value, i));
                }
            }

            return expected;
        }

        /// <summary>Copies the list, which is valid only until the next AgentsAt or Tick (§9.7).</summary>
        private static List<AgentView> Views(Rig rig, uint node)
        {
            return new List<AgentView>(rig.Flow.AgentsAt(new NodeId(node)));
        }

        private static void AssertViews(Rig rig, uint node, string where)
        {
            List<(ulong Cohort, int Index)> expected = Expected(rig, node);
            List<AgentView> views = Views(rig, node);
            Assert.True(
                rig.Pop(node) == views.Count,
                where + ": AgentsAt(" + node + ") has " + views.Count + " views, Population is " + rig.Pop(node));
            for (int i = 0; i < views.Count; i++)
            {
                AgentView v = views[i];
                Assert.True(
                    expected[i] == (v.Ref.Cohort.Value, v.Ref.Index),
                    where + ": AgentsAt(" + node + ")[" + i + "] is (" + v.Ref.Cohort.Value + ", " + v.Ref.Index + "), expected (" + expected[i].Cohort + ", " + expected[i].Index + ")");
                Assert.True(v.Node == new NodeId(node), where + ": view " + i + " on node " + v.Node.Value + ", listed at " + node);
                Assert.True(v.ProgressAlongEdge >= Fx.Zero && v.ProgressAlongEdge <= Fx.One, where + ": view " + i + " progress outside 0..1");
            }
        }

        [Fact]
        public void test_agents_at_promoted_node_one_view_per_passenger_indexed_within_each_cohort()
        {
            // Three keys on one Source, so three cohorts of 3, 5 and 2.
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One)));
            CohortId a = rig.InjectNow(1, 3, 1);
            CohortId b = rig.InjectNow(1, 5, 2);
            CohortId c = rig.InjectNow(1, 2, 3);
            Assert.True(a.Value < b.Value && b.Value < c.Value);
            rig.Flow.SetPromoted(new NodeId(1), true);

            List<AgentView> views = Views(rig, 1);
            Assert.Equal(10, rig.Pop(1));
            Assert.Equal(10, views.Count);
            var expected = new List<(ulong, int)>
            {
                (a.Value, 0), (a.Value, 1), (a.Value, 2),
                (b.Value, 0), (b.Value, 1), (b.Value, 2), (b.Value, 3), (b.Value, 4),
                (c.Value, 0), (c.Value, 1),
            };
            var actual = new List<(ulong, int)>();
            foreach (AgentView v in views)
            {
                actual.Add((v.Ref.Cohort.Value, v.Ref.Index));
                Assert.Equal(new NodeId(1), v.Node);
            }

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void test_agents_at_single_cohort_indexes_run_zero_to_count_minus_one()
        {
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One)));
            CohortId only = rig.InjectNow(1, 7, 1);
            rig.Flow.SetPromoted(new NodeId(1), true);

            List<AgentView> views = Views(rig, 1);
            Assert.Equal(7, views.Count);
            for (int i = 0; i < 7; i++)
            {
                Assert.Equal(only, views[i].Ref.Cohort);
                Assert.Equal(i, views[i].Ref.Index);
            }
        }

        [Fact]
        public void test_agents_at_count_tracks_population_after_service_splits_and_demotion_empties()
        {
            // 60 pax/min on one lane serves 6 a tick (6 s ticks), so a cohort
            // of 40 on the queue splits: the remainder keeps its id (§9.3) and
            // its views must re-index from 0 over the smaller Count.
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60))));
            rig.InjectNow(1, 40, 1);
            rig.InjectNow(1, 9, 2);
            for (uint n = 1; n <= 4; n++)
            {
                rig.Flow.SetPromoted(new NodeId(n), true);
            }

            bool sawSplitRemainder = false;
            for (int t = 0; t < 8; t++)
            {
                rig.Step(1);
                for (uint n = 1; n <= 4; n++)
                {
                    AssertViews(rig, n, "tick " + t);
                }

                foreach (PassengerCohort c in rig.CohortsOn(2))
                {
                    sawSplitRemainder |= c.Count > 1 && c.Count < 40 && c.Key.Flight.Value == 1;
                }
            }

            Assert.True(sawSplitRemainder, "the queue never held a partly served cohort");

            rig.Flow.SetPromoted(new NodeId(3), false);
            Assert.True(rig.Pop(3) > 0);
            Assert.Empty(rig.Flow.AgentsAt(new NodeId(3)));
        }

        [Fact]
        public void test_agents_at_every_promoted_node_over_a_day_matches_cohorts()
        {
            // Property over the Phase 0 fixture day: after every tick, on every
            // promoted node, one view per passenger in (Cohort, Index) order.
            var rig = Rig.Fixture(DayScenario.Content());
            for (uint n = 1; n <= 9; n++)
            {
                rig.Flow.SetPromoted(new NodeId(n), true);
            }

            int multiPassengerCohorts = 0;
            new DayScenario().Run(rig, 0xA6E7_0001UL, 1200, afterTick: t =>
            {
                for (uint n = 1; n <= 9; n++)
                {
                    AssertViews(rig, n, "tick " + t);
                    foreach (PassengerCohort c in rig.CohortsOn(n))
                    {
                        if (c.Count > 1)
                        {
                            multiPassengerCohorts++;
                        }
                    }
                }
            });

            Assert.True(multiPassengerCohorts > 0, "no cohort with Count > 1 was ever checked");
        }
    }
}
