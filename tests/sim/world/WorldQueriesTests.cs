using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// 18 §18.3: every query is read-only and allocates nothing, since the lists
    /// are views into load-time tables. 08 §8.5: Tick must not allocate.
    /// Lists are read through the indexer, never foreach, so the test itself
    /// allocates no enumerator.
    /// </summary>
    public sealed class WorldQueriesTests
    {
        private static long Sweep(IWorldSystem world)
        {
            long sum = 0;
            IReadOnlyList<NodeId> nodes = world.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId n = nodes[i];
                sum += world.LengthMetres(n);
                IReadOnlyList<EdgeId> outs = world.OutEdges(n);
                for (int k = 0; k < outs.Count; k++)
                {
                    sum += world.EdgeTo(outs[k]).Value;
                    for (int j = 0; j < nodes.Count; j++)
                    {
                        if (world.CanReachVia(outs[k], nodes[j]))
                        {
                            sum++;
                        }

                        IReadOnlyList<NodeId> path = world.PathVia(outs[k], nodes[j]);
                        for (int p = 0; p < path.Count; p++)
                        {
                            sum += path[p].Value;
                        }
                    }
                }

                for (int j = 0; j < nodes.Count; j++)
                {
                    if (world.CanReach(n, nodes[j]))
                    {
                        sum++;
                    }
                }
            }

            return sum;
        }

        [Fact]
        public void test_world_queries_allocate_nothing()
        {
            var rng = new SplitMix64(0xA110C);
            var worlds = new List<IWorldSystem> { WorldKit.Create(Phase0Landside.Load()) };
            for (int i = 0; i < 10; i++)
            {
                worlds.Add(WorldKit.Create(WorldKit.RandomGraph(rng, 7, 5, 40)));
            }

            foreach (IWorldSystem world in worlds)
            {
                long warm = Sweep(world);
                long before = GC.GetAllocatedBytesForCurrentThread();
                long again = Sweep(world);
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.Equal(warm, again);
                Assert.Equal(0L, after - before);
            }
        }

        [Fact]
        public void test_world_queries_tick_allocates_nothing()
        {
            IWorldSystem world = WorldKit.Create(Phase0Landside.Load());
            var rng = new CountingRandom();
            var events = new CountingPublisher();
            TickContext ctx = WorldKit.Context(1, rng, events);
            world.Tick(ctx);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                world.Tick(ctx);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0L, after - before);
            Assert.Equal(0, rng.Touches);
        }
    }
}
