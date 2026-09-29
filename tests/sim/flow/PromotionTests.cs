using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 01 "Hierarchical simulation" rule 4, 02 rule 8 and 09 §9.1/§9.7:
    /// promotion and demotion change no simulation outcome. Each test runs
    /// the same congested day headless and with promotion, and compares
    /// every checkpoint, every cohort at every checkpoint, every flow event
    /// and the final hashes.
    /// </summary>
    public sealed class PromotionTests
    {
        private const ulong Day = PromoConst.TicksPerDay;

        private static PromoTrace Headless(uint stepSize = 600)
        {
            return PromoTrace.Run(new PromoRig(PromoPlan.Standard()), Day, stepSize);
        }

        [Fact]
        public void test_promotion_is_outcome_neutral()
        {
            // 01's named test and 02's determinism_promotion: the camera parked on the gate all day.
            PromoTrace headless = Headless();
            var rig = new PromoRig(PromoPlan.Standard());
            rig.Flow.SetPromoted(new NodeId(PromoConst.Gate), true);
            PromoTrace parked = PromoTrace.Run(rig, Day, 600, _ => rig.Flow.AgentsAt(new NodeId(PromoConst.Gate)));
            PromoTrace.AssertSame(headless, parked);
        }

        [Fact]
        public void test_promotion_of_every_node_all_day_is_outcome_neutral()
        {
            PromoTrace headless = Headless();
            var rig = new PromoRig(PromoPlan.Standard());
            rig.SetAll(true);
            PromoTrace promoted = PromoTrace.Run(rig, Day, 600);
            PromoTrace.AssertSame(headless, promoted);
        }

        [Fact]
        public void test_promotion_with_agents_read_every_tick_is_outcome_neutral()
        {
            // Reading agents may draw from flow.presentation; that stream is outside the hash (§9.1 item 2).
            PromoTrace headless = Headless(1);
            var rig = new PromoRig(PromoPlan.Standard());
            rig.SetAll(true);
            long views = 0;
            PromoTrace promoted = PromoTrace.Run(rig, Day, 1, _ =>
            {
                foreach (uint n in PromoConst.AllNodes)
                {
                    views += rig.Flow.AgentsAt(new NodeId(n)).Count;
                }
            });
            Assert.True(views > 0, "promoted nodes must show agents");
            PromoTrace.AssertSame(headless, promoted);
        }

        [Fact]
        public void test_promotion_toggled_between_steps_is_outcome_neutral()
        {
            PromoTrace headless = Headless(7);
            var rig = new PromoRig(PromoPlan.Standard());
            const ulong seed = 0x0010_7066UL;
            var rng = new PromoSplitMix(seed);
            PromoTrace toggled = PromoTrace.Run(rig, Day, 7, _ =>
            {
                for (int k = 0; k < 3; k++)
                {
                    var node = new NodeId(PromoConst.AllNodes[rng.Below(PromoConst.AllNodes.Length)]);
                    rig.Flow.SetPromoted(node, rng.Below(2) == 0);
                    rig.Flow.AgentsAt(node);
                }
            });
            PromoTrace.AssertSame(headless, toggled);
        }

        [Fact]
        public void test_promotion_toggled_inside_ticks_is_outcome_neutral()
        {
            // "SetPromoted may be called at any tick" (09 §9.7): here from a system running after sim.flow.
            PromoTrace headless = Headless();
            var rig = new PromoRig(PromoPlan.Standard());
            rig.AfterFlow.Hook = (in TickContext ctx, IFlowSystem flow) =>
            {
                var node = new NodeId(PromoConst.AllNodes[(int)(ctx.Tick % (ulong)PromoConst.AllNodes.Length)]);
                flow.SetPromoted(node, (ctx.Tick / 50UL) % 2UL == 0UL);
            };
            PromoTrace toggled = PromoTrace.Run(rig, Day, 600);
            PromoTrace.AssertSame(headless, toggled);
        }

        [Fact]
        public void test_promotion_then_demotion_leaves_nothing_to_fold_back()
        {
            // Promote everything for the morning peak, demote for the rest of the day (§9.1 item 3).
            PromoTrace headless = Headless();
            var rig = new PromoRig(PromoPlan.Standard());
            PromoTrace cycled = PromoTrace.Run(rig, Day, 600, t =>
            {
                if (t == 3000UL)
                {
                    rig.SetAll(true);
                }

                if (t == 9000UL)
                {
                    rig.SetAll(false);
                }
            });
            PromoTrace.AssertSame(headless, cycled);
        }

        [Fact]
        public void test_promotion_set_promoted_changes_no_hashed_state()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false);
            int checkedPoints = 0;
            for (ulong t = 0; t < Day; t += 450UL)
            {
                rig.Host.Step(450);
                ulong world = rig.Host.WorldStateHash();
                ulong flow = rig.Flow.ComputeStateHash();
                string cohorts = rig.DescribeCohorts();
                rig.SetAll(true);
                foreach (uint n in PromoConst.AllNodes)
                {
                    rig.Flow.AgentsAt(new NodeId(n));
                }

                Assert.Equal(flow, rig.Flow.ComputeStateHash());
                Assert.Equal(world, rig.Host.WorldStateHash());
                Assert.Equal(cohorts, rig.DescribeCohorts());
                rig.SetAll(false);
                Assert.Equal(flow, rig.Flow.ComputeStateHash());
                Assert.Equal(world, rig.Host.WorldStateHash());
                if (rig.TotalPopulation() > 0)
                {
                    checkedPoints++;
                }
            }

            Assert.True(checkedPoints > 5, "the checks must happen while passengers are on the graph");
        }

        [Fact]
        public void test_promotion_is_idempotent_and_emits_no_events()
        {
            var rig = new PromoRig(PromoPlan.Standard());
            rig.Host.Step(5000);
            int before = rig.Events!.Lines.Count;
            ulong hash = rig.Flow.ComputeStateHash();
            Assert.True(rig.TotalPopulation() > 0);
            for (int i = 0; i < 3; i++)
            {
                rig.SetAll(true);
                rig.SetAll(true);
                rig.SetAll(false);
                rig.SetAll(false);
            }

            Assert.Equal(before, rig.Events.Lines.Count);
            Assert.Equal(hash, rig.Flow.ComputeStateHash());
            var headless = new PromoRig(PromoPlan.Standard());
            headless.Host.Step(5001);
            rig.Host.Step(1);
            Assert.Equal(headless.Events!.Lines, rig.Events.Lines);
            Assert.Equal(headless.Host.WorldStateHash(), rig.Host.WorldStateHash());
        }

        [Fact]
        public void test_promotion_is_outcome_neutral_over_two_days()
        {
            PromoTrace headless = PromoTrace.Run(new PromoRig(PromoPlan.Standard()), 2UL * Day, 600);
            var rig = new PromoRig(PromoPlan.Standard());
            rig.Flow.SetPromoted(new NodeId(PromoConst.Gate), true);
            rig.Flow.SetPromoted(new NodeId(5), true);
            PromoTrace promoted = PromoTrace.Run(rig, 2UL * Day, 600, _ =>
            {
                rig.Flow.AgentsAt(new NodeId(PromoConst.Gate));
                rig.Flow.AgentsAt(new NodeId(5));
            });
            Assert.Equal(48, headless.Checkpoints.Count);
            PromoTrace.AssertSame(headless, promoted);
        }

        [Fact]
        public void test_promotion_does_not_change_head_count_on_any_node()
        {
            // Conservation across promotion: at every tick, every node's population
            // matches the headless run's, and the injected total is accounted for.
            var headless = new PromoRig(PromoPlan.Standard(), record: false);
            var promoted = new PromoRig(PromoPlan.Standard(), record: false);
            promoted.SetAll(true);
            var mismatches = new List<string>();
            for (ulong t = 0; t < Day; t++)
            {
                headless.Host.Step(1);
                promoted.Host.Step(1);
                foreach (uint n in PromoConst.AllNodes)
                {
                    var node = new NodeId(n);
                    int a = headless.Flow.Population(node);
                    int b = promoted.Flow.Population(node);
                    if (a != b && mismatches.Count < 10)
                    {
                        mismatches.Add("t=" + t + " n=" + n + " headless " + a + " promoted " + b);
                    }
                }
            }

            Assert.Empty(mismatches);
            Assert.True(headless.Injector.Injected > 0);
            Assert.Equal(headless.Injector.Injected, promoted.Injector.Injected);
            Assert.Equal(headless.Absorber.Absorbed, promoted.Absorber.Absorbed);
            Assert.True(promoted.Absorber.Absorbed > 0, "the scenario must board passengers");
        }
    }
}
