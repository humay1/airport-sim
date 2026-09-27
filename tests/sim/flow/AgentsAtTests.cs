using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.1 and §9.7: AgentsAt is empty unless the node is promoted. On a
    /// promoted node it shows the node's passengers as agents whose
    /// PassengerRef = (CohortId, indexWithinCohort) is derived, never drawn.
    /// Each view names its node and has ProgressAlongEdge in [0, 1].
    /// </summary>
    public sealed class AgentsAtTests
    {
        private static PromoRig RunTo(ulong tick, bool promoteAll = false)
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            if (promoteAll)
            {
                rig.SetAll(true);
            }

            rig.Host.Step((uint)tick);
            return rig;
        }

        [Fact]
        public void test_agents_at_is_empty_for_non_promoted_nodes()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            int populatedChecks = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t += 300UL)
            {
                rig.Host.Step(300);
                foreach (uint n in PromoConst.AllNodes)
                {
                    var node = new NodeId(n);
                    Assert.Empty(rig.Flow.AgentsAt(node));
                    populatedChecks += rig.Flow.Population(node) > 0 ? 1 : 0;
                }
            }

            Assert.True(populatedChecks > 20, "non-promoted nodes must be checked while populated");
        }

        [Fact]
        public void test_agents_at_shows_every_passenger_once_by_cohort_index()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            rig.SetAll(true);
            int nonEmpty = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t += 97UL)
            {
                rig.Host.Step(97);
                foreach (uint n in PromoConst.AllNodes)
                {
                    var node = new NodeId(n);
                    IReadOnlyList<AgentView> views = rig.Flow.AgentsAt(node);
                    SortedSet<(ulong Cohort, int Index)> expected = PromoAgents.Expected(rig.Flow, node);
                    SortedSet<(ulong Cohort, int Index)> actual = PromoAgents.Refs(views);
                    Assert.True(expected.SetEquals(actual), "node " + n + " after tick " + (rig.Host.CurrentTick - 1UL) + ": agents do not match its cohorts' (id, index) pairs");
                    Assert.Equal(rig.Flow.Population(node), views.Count);
                    nonEmpty += views.Count > 0 ? 1 : 0;
                }
            }

            Assert.True(nonEmpty > 20);
        }

        [Fact]
        public void test_agents_at_views_name_their_node_and_progress_within_unit_interval()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            rig.SetAll(true);
            long seen = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t += 61UL)
            {
                rig.Host.Step(61);
                foreach (uint n in PromoConst.AllNodes)
                {
                    foreach (AgentView v in rig.Flow.AgentsAt(new NodeId(n)))
                    {
                        seen++;
                        Assert.Equal(n, v.Node.Value);
                        Assert.True(v.ProgressAlongEdge >= Fx.Zero && v.ProgressAlongEdge <= Fx.One, "progress " + v.ProgressAlongEdge.Raw + " outside [0, 1]");
                        Assert.True(rig.Flow.TryGetCohort(v.Ref.Cohort, out PassengerCohort c));
                        Assert.Equal(n, c.Node.Value);
                        Assert.InRange(v.Ref.Index, 0, c.Count - 1);
                    }
                }
            }

            Assert.True(seen > 1000);
        }

        [Fact]
        public void test_agents_at_refs_do_not_depend_on_presentation_draws()
        {
            // Run A reads agents on every tick; run B only at the sample ticks.
            // A PassengerRef is derived (§9.1 item 4), so the refs agree even
            // though the presentation stream was consumed differently.
            var a = new PromoRig(PromoPlan.Standard(), record: false);
            var b = new PromoRig(PromoPlan.Standard(), record: false);
            a.SetAll(true);
            b.SetAll(true);
            int compared = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t++)
            {
                a.Host.Step(1);
                b.Host.Step(1);
                bool sample = t % 500UL == 499UL;
                foreach (uint n in PromoConst.AllNodes)
                {
                    var node = new NodeId(n);
                    IReadOnlyList<AgentView> av = a.Flow.AgentsAt(node);
                    if (sample)
                    {
                        IReadOnlyList<AgentView> bv = b.Flow.AgentsAt(node);
                        Assert.True(PromoAgents.Refs(av).SetEquals(PromoAgents.Refs(bv)), "refs differ at node " + n + " tick " + t);
                        compared += av.Count;
                    }
                }
            }

            Assert.True(compared > 0);
        }

        [Fact]
        public void test_agents_at_refs_are_stable_across_repeated_reads()
        {
            PromoRig rig = RunTo(6000, promoteAll: true);
            foreach (uint n in PromoConst.AllNodes)
            {
                var node = new NodeId(n);
                SortedSet<(ulong Cohort, int Index)> first = PromoAgents.Refs(rig.Flow.AgentsAt(node));
                for (int i = 0; i < 5; i++)
                {
                    Assert.True(first.SetEquals(PromoAgents.Refs(rig.Flow.AgentsAt(node))));
                }
            }

            Assert.True(rig.TotalPopulation() > 0);
        }

        [Fact]
        public void test_agents_at_is_empty_after_demotion()
        {
            PromoRig rig = RunTo(6000, promoteAll: true);
            int shown = 0;
            foreach (uint n in PromoConst.AllNodes)
            {
                shown += rig.Flow.AgentsAt(new NodeId(n)).Count;
            }

            Assert.True(shown > 0);
            rig.SetAll(false);
            foreach (uint n in PromoConst.AllNodes)
            {
                Assert.Empty(rig.Flow.AgentsAt(new NodeId(n)));
            }

            rig.Host.Step(100);
            foreach (uint n in PromoConst.AllNodes)
            {
                Assert.Empty(rig.Flow.AgentsAt(new NodeId(n)));
            }
        }

        [Fact]
        public void test_agents_at_shows_agents_as_soon_as_promoted()
        {
            // Promotion needs no tick to take effect: agents are a view of current cohorts.
            PromoRig rig = RunTo(6000);
            foreach (uint n in PromoConst.AllNodes)
            {
                var node = new NodeId(n);
                rig.Flow.SetPromoted(node, true);
                Assert.True(PromoAgents.Expected(rig.Flow, node).SetEquals(PromoAgents.Refs(rig.Flow.AgentsAt(node))));
            }

            Assert.True(rig.TotalPopulation() > 0);
        }

        [Fact]
        public void test_agents_at_same_seed_same_views()
        {
            // Determinism of the view itself: same seed and same calls give the same views, progress included.
            var a = new PromoRig(PromoPlan.Standard(), record: false);
            var b = new PromoRig(PromoPlan.Standard(), record: false);
            a.SetAll(true);
            b.SetAll(true);
            int compared = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t += 113UL)
            {
                a.Host.Step(113);
                b.Host.Step(113);
                foreach (uint n in PromoConst.AllNodes)
                {
                    string av = PromoAgents.Describe(a.Flow.AgentsAt(new NodeId(n)));
                    string bv = PromoAgents.Describe(b.Flow.AgentsAt(new NodeId(n)));
                    Assert.Equal(av, bv);
                    compared += av.Length;
                }
            }

            Assert.True(compared > 0);
        }
    }
}
