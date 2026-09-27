using System;
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
        public void test_promotion_allocation_set_promoted_allocates_nothing_after_warm_up()
        {
            var rig = new PromoRig(PromoPlan.Standard(), record: false, recordCheckpoints: false);
            rig.Host.Step(6000);
            Assert.True(rig.TotalPopulation() > 0);
            rig.SetAll(true);
            rig.SetAll(false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                rig.SetAll(i % 2 == 0);
            }

            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(bytes == 0L, "SetPromoted allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over 900 calls");
        }
    }
}
