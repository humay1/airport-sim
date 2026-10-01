using System;
using System.Collections.Generic;
using System.Diagnostics;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// sim.world's budget, 0.10 ms/tick at max tier (03-module-map.md, 18
    /// §18.4), measured per 07 L11. It is spent by callers' queries, not by
    /// Tick, so each measured tick is one Tick plus a caller-sized batch of
    /// queries against a max-tier graph (about 200 landside nodes, §18.3).
    /// Route precomputation is load time and is not measured.
    ///
    /// Window and arithmetic per 03 "Budget tests: window and arithmetic"
    /// (Q-044, Q-045): one window of TICKS_PER_SIM_DAY consecutive per-tick
    /// samples after an unsampled warm-up, each rounded up to whole
    /// microseconds and capped, passing iff the sum is at most B x n and the
    /// nearest-rank p99 at most 2 x B. sim.world registers no handler, so a
    /// sample is its Tick plus the queries (03 "Measured", Q-064).
    /// </summary>
    public sealed class WorldBudgetTests
    {
        private const long BudgetMicrosPerTick = 100;
        private const int MaxTierNodes = 200;
        private const int Window = (int)SimConstants.TICKS_PER_SIM_DAY;
        private const int WarmUpTicks = Window;

        private readonly ITestOutputHelper _output;

        public WorldBudgetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// 03 Q-045 step 2: a raw Stopwatch difference to whole microseconds,
        /// rounded up and capped at <paramref name="cap"/>, in long only.
        /// </summary>
        private static long RoundedUpMicros(long d, long f, long cap)
        {
            if (d > (long.MaxValue - f + 1) / 1_000_000L)
            {
                return cap;
            }

            return Math.Min((d * 1_000_000L + f - 1) / f, cap);
        }

        /// <summary>
        /// A connected max-tier graph: a two-way spine through all nodes plus
        /// random extra one-way links, every length in [1, 50] metres.
        /// </summary>
        private static WalkGraph MaxTier(SplitMix64 rng)
        {
            var b = new GraphBuilder();
            for (uint i = 1; i <= MaxTierNodes; i++)
            {
                b.Node(i, (uint)rng.Range(1, 50));
            }

            var seen = new HashSet<(uint, uint)>();
            uint edge = 1;
            for (uint i = 1; i < MaxTierNodes; i++)
            {
                seen.Add((i, i + 1));
                b.Edge(edge++, i, i + 1);
                seen.Add((i + 1, i));
                b.Edge(edge++, i + 1, i);
            }

            for (int k = 0; k < 2 * MaxTierNodes; k++)
            {
                uint from = (uint)rng.Range(1, MaxTierNodes);
                uint to = (uint)rng.Range(1, MaxTierNodes);
                if (from != to && seen.Add((from, to)))
                {
                    b.Edge(edge++, from, to);
                }
            }

            return b.Build(rng.Next());
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_world_budget_tick_and_queries_at_max_tier_within_point_one_ms()
        {
            var rng = new SplitMix64(0xB0D6E7);
            WalkGraph g = MaxTier(rng);
            IWorldSystem world = WorldKit.Create(g);

            // Per tick: 64 CanReach, 64 CanReachVia and 16 full PathVia reads,
            // drawn up front so the measured loop does only world work.
            const int Queries = 64;
            const int Paths = 16;
            var from = new NodeId[Queries];
            var to = new NodeId[Queries];
            var via = new EdgeId[Queries];
            for (int i = 0; i < Queries; i++)
            {
                from[i] = g.Nodes[rng.Range(0, g.Nodes.Count - 1)].Id;
                to[i] = g.Nodes[rng.Range(0, g.Nodes.Count - 1)].Id;
                via[i] = g.Edges[rng.Range(0, g.Edges.Count - 1)].Id;
            }

            var rngService = new CountingRandom();
            var events = new CountingPublisher();
            TickContext ctx = WorldKit.Context(1, rngService, events);
            long[] d = new long[Window];
            long sink = 0;

            // Pass 0 is the unsampled warm-up (JIT tiering, caches); pass 1
            // is the one asserted window, one sample per tick.
            for (int pass = 0; pass < 2; pass++)
            {
                int ticks = pass == 0 ? WarmUpTicks : Window;
                for (int t = 0; t < ticks; t++)
                {
                    long start = Stopwatch.GetTimestamp();
                    world.Tick(ctx);
                    for (int i = 0; i < Queries; i++)
                    {
                        int q = (i + t) % Queries;
                        if (world.CanReach(from[q], to[q]))
                        {
                            sink++;
                        }

                        if (world.CanReachVia(via[q], to[q]))
                        {
                            sink++;
                        }
                    }

                    for (int i = 0; i < Paths; i++)
                    {
                        int q = (i + t) % Queries;
                        IReadOnlyList<NodeId> path = world.PathVia(via[q], to[q]);
                        for (int p = 0; p < path.Count; p++)
                        {
                            sink += path[p].Value;
                        }
                    }

                    long end = Stopwatch.GetTimestamp();
                    if (pass == 1)
                    {
                        d[t] = end - start;
                    }
                }
            }

            const long B = BudgetMicrosPerTick;
            const long n = Window;
            const long C = B * n + 1;
            long f = Stopwatch.Frequency;
            long[] u = new long[Window];
            long sum = 0;
            for (int t = 0; t < Window; t++)
            {
                u[t] = RoundedUpMicros(d[t], f, C);
                sum += u[t];
            }

            Array.Sort(u);
            long p99 = u[(99 * n + 99) / 100 - 1];
            long reportedMean = (sum + n - 1) / n;
            string report = "sim.world tick + queries over " + n + " ticks: mean " + reportedMean + " us (limit " + B
                + "), p99 " + p99 + " us (limit " + (2 * B) + "), max " + u[Window - 1] + " us; Stopwatch.Frequency " + f;
            _output.WriteLine(report);

            Assert.True(sum <= B * n, "mean over budget. " + report);
            Assert.True(p99 <= 2 * B, "p99 over twice the budget. " + report);
            Assert.True(sink > 0);
            Assert.Equal(0, rngService.Touches);
        }
    }
}
