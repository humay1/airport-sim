using System.Diagnostics;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// sim.flow's budget, 2.5 ms/tick at max tier (03-module-map.md, 09 §9.10),
    /// measured per 07 L11; no allocation in the update path (§9.10, 07); and a
    /// ceiling on live cohorts, because "a passing time with an unbounded cohort
    /// count only means the fixture was short" (§9.10).
    /// </summary>
    public sealed class FlowBudgetTests
    {
        private const long BudgetMicrosPerTick = 2500;
        private const int Branches = 40;
        private const int FlightTicks = 150;
        private const int LiveFlights = 7;

        private static long ElapsedMicros(long start, long end)
        {
            return (end - start) * 1_000_000L / Stopwatch.Frequency;
        }

        /// <summary>
        /// About 200 landside nodes (18 §18.3's max tier): 40 branches of
        /// source -> hall -> corridor -> queue -> corridor, joining one long
        /// corridor to the gate and the sink.
        /// </summary>
        private static TestGraph MaxTier(out uint[] sources, out uint sink)
        {
            var g = new TestGraph();
            sources = new uint[Branches];
            uint join = 1000;
            uint gate = 1001;
            sink = 1002;
            for (uint b = 0; b < Branches; b++)
            {
                uint s = 10 + b * 5;
                sources[b] = s;
                g.Node(s, "source").Node(s + 1, "hall", 20).Node(s + 2, "corridor", 60)
                 .Queue(s + 3, 2, 2, FlowKit.Lane, 10).Node(s + 4, "corridor", 60);
                g.Edge(s, s + 1).Edge(s + 1, s + 2).Edge(s + 2, s + 3).Edge(s + 3, s + 4).Edge(s + 4, join);
            }

            g.Node(join, "corridor", 120).Node(gate, "gate").Node(sink, "sink").Edge(join, gate).Edge(gate, sink);
            return g;
        }

        private static IContentIndex Content()
        {
            return ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.FromRatio(13, 10)),
                FlowKit.Queue(FlowKit.Lane, Fx.FromInt(4), 1000000, Fx.FromInt(30), Fx.FromInt(5)),
            });
        }

        /// <summary>
        /// Wires a steady load: three cohorts a tick from SplitMix64-chosen
        /// sources, one flight per FlightTicks, each flight absorbed
        /// LiveFlights - 1 flights later. The probe allocates nothing per tick.
        /// </summary>
        private static Rig Loaded(ulong seed)
        {
            var rig = Rig.Create(MaxTier(out uint[] sources, out uint sink), Content(), 1, recordEvents: false);
            var rng = new SplitMix64(seed);
            var sinkNode = new NodeId(sink);
            rig.Inject = (in TickContext ctx) =>
            {
                ulong flight = 1 + ctx.Tick / FlightTicks;
                for (int k = 0; k < 3; k++)
                {
                    rig.Flow.Inject(FlowKit.Key(flight), rng.Range(4, 16), new NodeId(sources[rng.Range(0, Branches - 1)]));
                }

                if (ctx.Tick % FlightTicks == 0 && flight > LiveFlights - 1)
                {
                    rig.Flow.Absorb(sinkNode, new FlightId(flight - (LiveFlights - 1)));
                }
            };
            return rig;
        }

        private static int LiveCohorts(Rig rig)
        {
            int live = 0;
            var nodes = rig.World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                live += rig.Flow.CohortsAt(nodes[i]).Count;
            }

            return live;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_flow_budget_max_tier_within_two_and_a_half_ms_and_bounded_cohorts()
        {
            Rig rig = Loaded(0xB0D6_E7F1UL);
            int nodes = rig.World.Nodes().Count;
            Assert.InRange(nodes, 180, 220);

            // Warm up for two sim hours, then measure one.
            rig.Step((uint)(2 * SimConstants.TICKS_PER_SIM_HOUR));
            const int Measured = (int)SimConstants.TICKS_PER_SIM_HOUR;
            long start = Stopwatch.GetTimestamp();
            rig.Step((uint)Measured);
            long end = Stopwatch.GetTimestamp();
            long perTick = ElapsedMicros(start, end) / Measured;
            Assert.True(perTick <= BudgetMicrosPerTick, "sim.flow max tier took " + perTick + " us/tick, budget " + BudgetMicrosPerTick);

            // Merge is mandatory (§9.3): with no blocking (capacities are huge),
            // there is at most one cohort per (node, live key).
            int live = LiveCohorts(rig);
            Assert.True(live > 0);
            Assert.True(live <= nodes * LiveFlights, live + " live cohorts exceed " + nodes + " nodes x " + LiveFlights + " live keys");
            Assert.True(FlowKit.TotalPopulation(rig.Flow, rig.World) > 1000, "the load never built up");
        }

        [Fact]
        public void test_flow_budget_update_path_allocates_nothing()
        {
            // Steady state after warm-up; ticks 1201..1799 contain no checkpoint
            // (08 §8.9 allocates a fresh SystemHashes array at checkpoints only).
            Rig rig = Loaded(0xA110_C007UL);
            rig.Step(1201);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            rig.Step(599);
            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(1800UL, rig.Host.CurrentTick);
            Assert.Equal(0L, after - before);
        }
    }
}
