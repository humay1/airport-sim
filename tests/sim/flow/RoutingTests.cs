using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.6 corridors and routing, exact per §9.12 (Q-032): delay lines,
    /// one node per tick, the least-cost rule over IWorldSystem's routes, and
    /// the fixture's two alternative security queues (18 §18.6).
    /// </summary>
    public sealed class RoutingTests
    {
        private static IContentIndex FixtureContent(Fx securityRate, int capacity = 100000)
        {
            return ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.One),
                FlowKit.Queue(Landside.SecurityProfile, securityRate, capacity, Fx.FromInt(1000000), Fx.Zero),
            });
        }

        private static bool FlightOn(Rig rig, uint node, ulong flight)
        {
            foreach (PassengerCohort c in rig.CohortsOn(node))
            {
                if (c.Key.Flight.Value == flight)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void test_routing_moves_at_most_one_node_per_tick()
        {
            // Ascending ids, so a same-tick second hop would be possible if the
            // rule allowed it (Q-032: it does not).
            var g = new TestGraph()
                .Node(1, "source").Node(2, "hall").Node(3, "hall").Node(4, "gate").Node(5, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4).Edge(4, 5);
            var rig = Rig.Create(g, Graphs.Content());
            rig.InjectNow(1, 7, 1);
            var seen = new List<uint>();
            for (int i = 0; i < 12; i++)
            {
                rig.Step(1);
                for (uint n = 1; n <= 5; n++)
                {
                    if (rig.Pop(n) == 7 && (seen.Count == 0 || seen[seen.Count - 1] != n))
                    {
                        seen.Add(n);
                    }
                }
            }

            // Every node visited in order, each for at least one tick.
            Assert.Equal(new uint[] { 1, 2, 3, 4 }, seen.ToArray());
            Assert.Equal(7, rig.Pop(4));
        }

        [Fact]
        public void test_routing_corridor_is_a_delay_line_of_traversal_ticks()
        {
            // traversalTicks = max(1, Ceil(Div(len, speed x 6))). At 1.5 m/s a tick
            // is 9 m: 100 m is Div 11.11... -> Ceil 12; 90 m is exactly 10; 0 m is 1.
            foreach ((uint length, ulong expected) in new (uint, ulong)[] { (100, 12), (90, 10), (0, 1), (5, 1) })
            {
                var walker = new ContentId("pax_brisk");
                var g = new TestGraph()
                    .Node(1, "source").Node(2, "corridor", length).Node(3, "gate").Node(4, "sink")
                    .Edge(1, 2).Edge(2, 3).Edge(3, 4);
                IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[] { FlowKit.Pax(walker, Fx.FromRatio(3, 2)) });
                var rig = Rig.Create(g, content);
                rig.InjectNow(1, 3, 1, walker);
                int guard = 0;
                while (rig.Pop(2) == 0 && guard++ < 5)
                {
                    rig.Step(1);
                }

                List<PassengerCohort> onCorridor = rig.CohortsOn(2);
                Assert.Single(onCorridor);
                ulong entered = onCorridor[0].EnteredNodeAt;
                Assert.Equal(rig.Host.CurrentTick - 1, entered);
                Assert.Equal(entered + expected, onCorridor[0].DueAt);

                while (rig.Pop(3) == 0 && guard++ < 40)
                {
                    rig.Step(1);
                }

                // Released on its DueAt tick, into the gate.
                Assert.Equal(entered + expected, rig.Host.CurrentTick - 1);
                Assert.Single(rig.Events!.Arrived);
                Assert.Equal(entered + expected, rig.Events.Arrived[0].Tick);
                Assert.Equal(3, rig.Events.Arrived[0].E.Count);
                Assert.Equal(1UL, rig.Events.Arrived[0].E.Flight.Value);
            }
        }

        [Fact]
        public void test_routing_non_corridor_due_at_is_entered_node_at()
        {
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One)));
            rig.InjectNow(1, 2, 1);
            rig.Step(3);
            List<PassengerCohort> q = rig.CohortsOn(2);
            Assert.Single(q);
            Assert.Equal(q[0].EnteredNodeAt, q[0].DueAt);
        }

        [Fact]
        public void test_routing_two_security_queues_chosen_by_predicted_wait_without_randomness()
        {
            // Fixture: corridor 4 forks to SecurityA (5, edge 4) and SecurityB
            // (6, edge 5); both routes to the gate are identical in length. The
            // first flight meets equal costs and takes the ascending EdgeId, A.
            // While A still holds passengers its predicted wait adds to its cost,
            // so the next flight is sent to B.
            var rig = Rig.Fixture(FixtureContent(Fx.One));
            rig.InjectNow(Landside.Kerb, 40, 1);
            int guard = 0;
            while (rig.Pop(Landside.SecurityA) == 0 && rig.Pop(Landside.SecurityB) == 0 && guard++ < 100)
            {
                rig.Step(1);
            }

            Assert.Equal(40, rig.Pop(Landside.SecurityA));
            Assert.Equal(0, rig.Pop(Landside.SecurityB));

            rig.InjectNow(Landside.RailBox, 10, 2);
            bool flight2OnA = false;
            bool flight2OnB = false;
            bool flight1OnB = false;
            for (int i = 0; i < 200; i++)
            {
                rig.Step(1);
                flight2OnA |= FlightOn(rig, Landside.SecurityA, 2);
                flight2OnB |= FlightOn(rig, Landside.SecurityB, 2);
                flight1OnB |= FlightOn(rig, Landside.SecurityB, 1);
            }

            Assert.True(flight2OnB, "flight 2 should be routed to SecurityB while SecurityA is queued");
            Assert.False(flight2OnA);
            Assert.False(flight1OnB);
        }

        [Fact]
        public void test_routing_is_identical_across_runs_and_seeds()
        {
            string Run(ulong seed)
            {
                var rig = Rig.Fixture(FixtureContent(Fx.FromRatio(5, 2)), seed);
                var log = new List<string>();
                for (int t = 0; t < 600; t++)
                {
                    if (t % 7 == 0)
                    {
                        rig.InjectNow(t % 2 == 0 ? Landside.Kerb : Landside.RailBox, 3 + t % 11, (ulong)(1 + t / 60));
                    }

                    rig.Step(1);
                    log.Add(rig.Pop(Landside.SecurityA) + "/" + rig.Pop(Landside.SecurityB) + "/" + rig.Pop(Landside.Gate));
                }

                log.AddRange(rig.Events!.Log);
                return string.Join("\n", log);
            }

            string first = Run(1);
            Assert.Equal(first, Run(1));
            Assert.Equal(first, Run(0xFEEDUL));
        }

        [Fact]
        public void test_routing_tie_breaks_by_ascending_edge_id()
        {
            // Two identical branches from hall 2: edge 9 to queue 3, edge 4 to
            // queue 5. Equal costs, one gate, so the smaller EdgeId (4) wins.
            var g = new TestGraph()
                .Node(1, "source").Node(2, "hall")
                .Queue(3, 1, 1, FlowKit.Lane).Queue(5, 1, 1, FlowKit.Lane)
                .Node(6, "gate").Node(7, "sink")
                .Edge(1, 1, 2).Edge(9, 2, 3).Edge(4, 2, 5).Edge(10, 3, 6).Edge(11, 5, 6).Edge(12, 6, 7);
            var rig = Rig.Create(g, Graphs.Content(Graphs.Lane(Fx.Zero)));
            rig.InjectNow(1, 5, 1);
            rig.Step(4);
            Assert.Equal(5, rig.Pop(5));
            Assert.Equal(0, rig.Pop(3));
        }

        [Fact]
        public void test_routing_cost_counts_every_node_on_the_path()
        {
            // Q-032: every node on PathVia, of any kind and the destination
            // included, adds its traversalTicks. Branch via edge 2 has a 60 m
            // hall (10 ticks, not a delay there, but a cost); via edge 3 a 30 m
            // corridor (5 ticks). The cohort takes edge 3 despite the larger id.
            var g = new TestGraph()
                .Node(1, "source").Node(2, "hall", 60).Node(3, "corridor", 30).Node(4, "gate").Node(5, "sink")
                .Edge(1, 1, 2).Edge(3, 1, 3).Edge(4, 2, 4).Edge(5, 3, 4).Edge(6, 4, 5);
            var rig = Rig.Create(g, Graphs.Content());
            rig.InjectNow(1, 5, 1);
            rig.Step(3);
            Assert.Equal(0, rig.Pop(2));
            Assert.Equal(5, rig.Pop(3));
        }

        [Fact]
        public void test_routing_reads_routes_from_the_world()
        {
            // 09 §9.6: sim.flow never computes a path. The fake world claims the
            // edge-4 route to the gate is [5, 7, 7, 7, 8]; flow must price that
            // answer (two extra 14-tick corridor entries), not the real topology,
            // and so send the cohort to SecurityB although edge 4 is smaller.
            var world = new FakeWorld()
                .Node(1, 0).Node(2, 0).Node(3, 30).Node(4, 120).Node(5, 10).Node(6, 10).Node(7, 80).Node(8, 20).Node(9, 0)
                .Edge(1, 1, 3).Edge(2, 2, 3).Edge(3, 3, 4).Edge(4, 4, 5).Edge(5, 4, 6).Edge(6, 5, 7).Edge(7, 6, 7).Edge(8, 7, 8).Edge(9, 8, 9)
                .OverridePath(4, 8, 5, 7, 7, 7, 8);
            var rig = Rig.Create(_ => world, Fixtures.Read(Fixtures.FlowPath), Fixtures.FlowPath, FixtureContent(Fx.One), 1, true);
            rig.InjectNow(Landside.Kerb, 6, 1);
            int guard = 0;
            while (rig.Pop(Landside.SecurityA) + rig.Pop(Landside.SecurityB) == 0 && guard++ < 100)
            {
                rig.Step(1);
            }

            Assert.Equal(6, rig.Pop(Landside.SecurityB));
            Assert.Equal(0, rig.Pop(Landside.SecurityA));
            Assert.True(world.PathViaCalls > 0);
        }
    }
}
