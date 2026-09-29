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
    /// Every measurement goes through Allocation.Start()/Since() (a forced
    /// blocking collection before the baseline read, T-037's pattern), never
    /// a raw GC.GetAllocatedBytesForCurrentThread pair, and every window is
    /// kept short: a forced collection only bounds T-037's own flake over a
    /// short window, so a window spanning the whole day (or even most of it)
    /// can reintroduce that same flake instead of avoiding it.
    /// AllocatedOverDay batches the 599 non-checkpoint ticks between two
    /// checkpoint boundaries into one window (24 short windows/day) instead
    /// of one window per tick. The AgentsAt warm-read test samples every
    /// SampleEvery-th tick (288/day here) and, on a sampled tick, opens one
    /// short window per node lazily, closing and reopening it around a read
    /// of a node above its own peak (Q-033, allowed to allocate there), so
    /// exclusion within a sampled tick stays exactly per node as on main;
    /// unsampled ticks are read unmeasured, purely to keep the simulation
    /// and each node's peak advancing identically to main.
    /// </summary>
    public sealed class PromotionAllocationTests
    {
        private static long AllocatedOverDay(PromoRig rig, ulong fromTick)
        {
            // Same exclusion as a per-tick GC.GetAllocatedBytesForCurrentThread
            // pair would give (a tick is excluded when the tick it starts from
            // is itself a checkpoint boundary, 08 §8.9), but batched into one
            // forced collection per 600-tick period instead of one per tick:
            // fromTick is itself always a checkpoint boundary (a multiple of
            // 600), so each period is exactly one unmeasured tick off that
            // boundary, then the 599 following ticks measured as a single
            // window, landing back on the next boundary.
            long total = 0;
            ulong to = fromTick + PromoConst.TicksPerDay;
            while (rig.Host.CurrentTick < to)
            {
                rig.Host.Step(1);
                long before = Allocation.Start();
                rig.Host.Step(599);
                total += Allocation.Since(before);
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
            long before = Allocation.Start();
            for (int i = 0; i < 100; i++)
            {
                rig.SetAll(i % 2 == 0);
                rig.SetAll(i % 2 == 0);
            }

            long bytes = Allocation.Since(before);
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

            // Exclusion stays exactly per node (09 §9.7: only the growing
            // node may allocate). Keeping one window open across every
            // consecutive warm read — closing and reopening it only around a
            // read of a node above its own peak — was tried and rejected: on
            // this fixture no node ever exceeds its day-1 peak during day 2,
            // so that produced one multi-second window over all 129600
            // reads, and re-exposed T-037's own flake (33392 spurious bytes,
            // consistent with more than one allocation-context retirement
            // over so long a window). A forced collection bounds the flake
            // only over a short window (T-037's own reasoning), so instead
            // every SampleEvery-th tick is sampled: on a sampled tick, every
            // node is read exactly as on main (one window per node, opened
            // lazily and closed around an above-peak read, so exclusion is
            // still exactly per node within that tick); every other tick is
            // read with no window at all, unmeasured, purely to keep the
            // simulation and peak[] advancing identically to main. Sampling
            // trades tick coverage for a bounded, short-window forced-GC
            // count: PromoConst.TicksPerDay / SampleEvery = 14400 / 50 = 288
            // ticks sampled per day, so up to 288 * AllNodes.Length = 2592
            // per-node reads measured (fewer if any land on an above-peak
            // node), against 129600 before sampling.
            const ulong SampleEvery = 50UL;
            long bytes = 0;
            long reads = 0;
            long viewsRead = 0;
            long windows = 0;
            for (ulong t = 0; t < PromoConst.TicksPerDay; t++)
            {
                rig.Host.Step(1);
                if (t % SampleEvery != 0UL)
                {
                    for (int k = 0; k < PromoConst.AllNodes.Length; k++)
                    {
                        var node = new NodeId(PromoConst.AllNodes[k]);
                        int unsampledPop = rig.Flow.Population(node);
                        rig.Flow.AgentsAt(node);
                        peak[k] = Math.Max(peak[k], unsampledPop);
                    }

                    continue;
                }

                long windowStart = 0;
                bool windowOpen = false;
                for (int k = 0; k < PromoConst.AllNodes.Length; k++)
                {
                    var node = new NodeId(PromoConst.AllNodes[k]);
                    int pop = rig.Flow.Population(node);
                    if (pop > peak[k])
                    {
                        if (windowOpen)
                        {
                            bytes += Allocation.Since(windowStart);
                            windowOpen = false;
                        }

                        rig.Flow.AgentsAt(node);
                    }
                    else
                    {
                        if (!windowOpen)
                        {
                            windowStart = Allocation.Start();
                            windowOpen = true;
                            windows++;
                        }

                        reads++;
                        viewsRead += rig.Flow.AgentsAt(node).Count;
                    }

                    peak[k] = Math.Max(peak[k], pop);
                }

                if (windowOpen)
                {
                    bytes += Allocation.Since(windowStart);
                }
            }

            Assert.True(reads > 0 && viewsRead > 0, "day 2 must read populated promoted nodes");
            Assert.True(windows > 0, "day 2 must open at least one measurement window");
            Assert.True(bytes == 0L, "AgentsAt allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over " + reads + " sampled warm reads across " + windows + " windows");
        }
    }
}
