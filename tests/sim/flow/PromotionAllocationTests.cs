using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-010 budget clause and 09 §9.10: promotion bookkeeping allocates
    /// nothing on the hot path. Measured as whole-host ticks that are not
    /// checkpoints (a checkpoint allocates its SystemHashes array, 08 §8.9),
    /// after a warm-up day. The probes around sim.flow allocate nothing.
    /// </summary>
    public sealed class PromotionAllocationTests
    {
        private static long AllocatedOverDay(PromoRig rig, ulong fromTick)
        {
            long total = 0;
            while (rig.Host.CurrentTick < fromTick + PromoConst.TicksPerDay)
            {
                bool checkpoint = rig.Host.CurrentTick % 600UL == 0UL;
                long before = GC.GetAllocatedBytesForCurrentThread();
                rig.Host.Step(1);
                long delta = GC.GetAllocatedBytesForCurrentThread() - before;
                if (!checkpoint)
                {
                    total += delta;
                }
            }

            return total;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_promotion_allocation_tick_with_every_node_promoted_allocates_nothing()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false, recordCheckpoints: false);
            rig.SetAll(true);
            rig.Host.Step((uint)PromoConst.TicksPerDay);
            long bytes = AllocatedOverDay(rig, PromoConst.TicksPerDay);
            Assert.True(rig.Absorber.Absorbed > 0, "the measured day must move passengers to the gate and board them");
            Assert.True(bytes == 0L, "promoted ticks allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1");
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_promotion_allocation_toggling_inside_ticks_allocates_nothing()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false, recordCheckpoints: false);
            rig.AfterFlow.Hook = (in TickContext ctx, IFlowSystem flow) =>
            {
                var node = new NodeId(PromoConst.AllNodes[(int)(ctx.Tick % (ulong)PromoConst.AllNodes.Length)]);
                flow.SetPromoted(node, (ctx.Tick / 50UL) % 2UL == 0UL);
            };
            rig.Host.Step((uint)PromoConst.TicksPerDay);
            long bytes = AllocatedOverDay(rig, PromoConst.TicksPerDay);
            Assert.True(rig.Absorber.Absorbed > 0, "the measured day must move passengers to the gate and board them");
            Assert.True(bytes == 0L, "ticks with in-tick promotion toggles allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1");
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_promotion_allocation_set_promoted_never_allocates()
        {
            // Q-033: never, so the very first calls are measured too.
            var rig = new PromoRig(PromoPlan.Standard(), record: false, recordCheckpoints: false);
            rig.Host.Step(6000);
            Assert.True(rig.TotalPopulation() > 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                rig.SetAll(i % 2 == 0);
                rig.SetAll(i % 2 == 0);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0L, "SetPromoted allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over 1800 calls");
            rig.SetAll(true);
            Assert.Equal(rig.Flow.Population(new NodeId(PromoConst.Gate)), rig.Flow.AgentsAt(new NodeId(PromoConst.Gate)).Count);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_promotion_allocation_agents_at_allocates_nothing_after_warm_up()
        {
            // Q-033: AgentsAt's buffer grows only when a node's population exceeds
            // every earlier one. Warm up one day reading every node each tick; on
            // day 2, count allocation only on reads at or below the node's earlier peak.
            var rig = new PromoRig(PromoPlan.Standard(), record: false, recordCheckpoints: false);
            rig.SetAll(true);
            var peak = new int[PromoConst.AllNodes.Length];
            for (ulong t = 0; t < PromoConst.TicksPerDay; t++)
            {
                rig.Host.Step(1);
                for (int k = 0; k < PromoConst.AllNodes.Length; k++)
                {
                    var node = new NodeId(PromoConst.AllNodes[k]);
                    peak[k] = Math.Max(peak[k], rig.Flow.AgentsAt(node).Count);
                }
            }

            long bytes = 0;
            long reads = 0;
            long viewsRead = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t++)
            {
                rig.Host.Step(1);
                for (int k = 0; k < PromoConst.AllNodes.Length; k++)
                {
                    var node = new NodeId(PromoConst.AllNodes[k]);
                    int pop = rig.Flow.Population(node);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    IReadOnlyList<AgentView> views = rig.Flow.AgentsAt(node);
                    long delta = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (pop <= peak[k])
                    {
                        bytes += delta;
                        reads++;
                        viewsRead += views.Count;
                    }

                    peak[k] = Math.Max(peak[k], pop);
                }
            }

            Assert.True(reads > 0 && viewsRead > 0, "day 2 must read populated promoted nodes");
            Assert.True(bytes == 0L, "AgentsAt allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over " + reads + " warm reads");
        }
    }
}
