using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// Interface conformance for IFlowSystem (09 §9.7, §9.7b, §9.12) and its
    /// construction (§9.11), including the programmer-error throws of Inject,
    /// Absorb and the node queries.
    /// </summary>
    public sealed class FlowSystemTests
    {
        private static Rig Line()
        {
            return Rig.Create(Graphs.Line(3, 2, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(2))));
        }

        [Fact]
        public void test_flow_system_factory_creates_loader_and_system()
        {
            Assert.NotNull(FlowFactory.CreateGraphLoader());
            var rig = Line();
            Assert.NotNull(rig.Flow);
            Assert.IsAssignableFrom<ISimSystem>(rig.Flow);
        }

        [Fact]
        public void test_flow_system_id_is_registry_position_four_and_name_is_module()
        {
            var rig = Line();
            Assert.Equal((ushort)4, rig.Flow.Id.Value);
            Assert.Equal("sim.flow", rig.Flow.Name);
        }

        [Fact]
        public void test_flow_system_inject_creates_a_cohort_at_the_source()
        {
            var rig = Line();
            CohortKey key = FlowKit.Key(42, bags: true);
            CohortId id = rig.Flow.Inject(key, 17, new NodeId(1));
            Assert.True(rig.Flow.TryGetCohort(id, out PassengerCohort c));
            Assert.Equal(id, c.Id);
            Assert.Equal(key, c.Key);
            Assert.Equal(new NodeId(1), c.Node);
            Assert.Equal(17, c.Count);
            Assert.Equal(Fx.Zero, c.ServiceCredit);
            Assert.Equal(17, rig.Pop(1));
            Assert.Equal(17, rig.Flow.PopulationForFlight(new FlightId(42), FlowDirection.Departing));
            Assert.Equal(0, rig.Flow.PopulationForFlight(new FlightId(42), FlowDirection.Arriving));
            Assert.Equal(new[] { id }, rig.Flow.CohortsAt(new NodeId(1)));
            Assert.False(rig.Flow.TryGetCohort(new CohortId(id.Value + 1000), out _));
        }

        [Fact]
        public void test_flow_system_inject_rejects_unknown_node_non_source_and_non_positive_count()
        {
            // 09 §9.7 exact rules (Q-033): unknown or non-Source node is
            // ArgumentException; count <= 0 is ArgumentOutOfRangeException.
            // Never clamped or dropped.
            var rig = Line();
            CohortKey key = FlowKit.Key(1);
            Assert.Throws<ArgumentException>(() => rig.Flow.Inject(key, 5, new NodeId(99)));
            Assert.Throws<ArgumentException>(() => rig.Flow.Inject(key, 5, new NodeId(2)));
            Assert.Throws<ArgumentException>(() => rig.Flow.Inject(key, 5, new NodeId(3)));
            Assert.Throws<ArgumentException>(() => rig.Flow.Inject(key, 5, new NodeId(4)));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Flow.Inject(key, 0, new NodeId(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => rig.Flow.Inject(key, -3, new NodeId(1)));
            Assert.Equal(0, FlowKit.TotalPopulation(rig.Flow, rig.World));
        }

        [Fact]
        public void test_flow_system_inject_entered_node_at_is_ticks_completed()
        {
            // Q-033: EnteredNodeAt is N, the number of flow Ticks completed:
            // CurrentTick between ticks, t when called during tick t by an
            // earlier system. The cohort leaves its Source no earlier than N + 1.
            var rig = Line();
            rig.Step(5);
            CohortId between = rig.InjectNow(1, 2, 1);
            Assert.True(rig.Flow.TryGetCohort(between, out PassengerCohort c));
            Assert.Equal(5UL, c.EnteredNodeAt);
            Assert.Equal(5UL, c.DueAt);

            rig.Step(1);
            Assert.Equal(2, rig.Pop(1));
            rig.Step(1);
            Assert.Equal(0, rig.Pop(1));

            CohortId during = default;
            ulong at = 0;
            rig.Inject = (in TickContext ctx) =>
            {
                if (during.Value == 0)
                {
                    at = ctx.Tick;
                    during = rig.Flow.Inject(FlowKit.Key(2), 3, new NodeId(1));
                }
            };
            rig.Step(1);
            rig.Inject = null;
            Assert.Equal(7UL, at);
            Assert.True(rig.Flow.TryGetCohort(during, out PassengerCohort d));
            Assert.Equal(7UL, d.EnteredNodeAt);
            Assert.Equal(3, rig.Pop(1));
        }

        [Fact]
        public void test_flow_system_absorb_rejects_unknown_or_non_sink_and_ignores_unknown_flight()
        {
            // Q-033: unknown or non-Sink sink is ArgumentException; an unknown
            // flight is not an error and returns 0.
            var rig = Line();
            var thrown = new List<Exception?>();
            int unknownFlight = -1;
            rig.Inject = (in TickContext ctx) =>
            {
                foreach (uint node in new uint[] { 99, 1, 2, 3 })
                {
                    try
                    {
                        rig.Flow.Absorb(new NodeId(node), new FlightId(1));
                        thrown.Add(null);
                    }
                    catch (Exception e)
                    {
                        thrown.Add(e);
                    }
                }

                unknownFlight = rig.Flow.Absorb(new NodeId(4), new FlightId(12345));
            };
            rig.Step(1);
            Assert.Equal(4, thrown.Count);
            Assert.All(thrown, e => Assert.IsType<ArgumentException>(e));
            Assert.Equal(0, unknownFlight);
            Assert.Empty(rig.Events!.Missed);
        }

        [Fact]
        public void test_flow_system_unknown_node_queries_throw_argument_exception()
        {
            // §9.12 (Q-032): Population and PredictedWaitMinutes throw
            // ArgumentException for an unknown node.
            var rig = Line();
            Assert.Throws<ArgumentException>(() => rig.Flow.Population(new NodeId(99)));
            Assert.Throws<ArgumentException>(() => rig.Flow.PredictedWaitMinutes(new NodeId(99)));
            Assert.Throws<ArgumentException>(() => rig.Flow.Population(new NodeId(0)));
        }

        [Fact]
        public void test_flow_system_predicted_wait_is_formula_on_queue_and_zero_elsewhere()
        {
            var rig = Rig.Create(Graphs.Line(3, 2, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.Parse("2.5"))));
            rig.InjectNow(1, 11, 1);
            Assert.Equal(Fx.Zero, rig.Flow.PredictedWaitMinutes(new NodeId(1)));
            rig.Step(3);
            Assert.Equal(Graphs.Wait(rig.Pop(2), 2, Fx.Parse("2.5")), rig.Flow.PredictedWaitMinutes(new NodeId(2)));
            Assert.Equal(Fx.Zero, rig.Flow.PredictedWaitMinutes(new NodeId(3)));
            Assert.Equal(Fx.Zero, rig.Flow.PredictedWaitMinutes(new NodeId(4)));
        }

        [Fact]
        public void test_flow_system_lane_state_only_for_queue_nodes()
        {
            var rig = Line();
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(2), out LaneState lanes));
            Assert.Equal(3, lanes.ServerCount);
            Assert.Equal(2, lanes.ServersOpen);
            Assert.False(rig.Flow.TryGetLaneState(new NodeId(1), out _));
            Assert.False(rig.Flow.TryGetLaneState(new NodeId(3), out _));
            Assert.False(rig.Flow.TryGetLaneState(new NodeId(99), out _));
        }

        [Fact]
        public void test_flow_system_agents_empty_unless_promoted()
        {
            var rig = Line();
            rig.InjectNow(1, 4, 1);
            IReadOnlyList<AgentView> none = rig.Flow.AgentsAt(new NodeId(1));
            Assert.NotNull(none);
            Assert.Empty(none);
        }

        [Fact]
        public void test_flow_system_cohorts_at_empty_node_is_empty()
        {
            var rig = Line();
            IReadOnlyList<CohortId> none = rig.Flow.CohortsAt(new NodeId(3));
            Assert.NotNull(none);
            Assert.Empty(none);
        }

        [Fact]
        public void test_flow_system_create_system_resolves_queue_profile()
        {
            // §9.11 (Q-032): an unresolved queue_profile fails in CreateSystem
            // with FormatException "sim.flow: ...", naming the node and profile.
            TestGraph g = new TestGraph()
                .Node(1, "source").Queue(27, 1, 1, new ContentId("queue_missing")).Node(3, "gate").Node(4, "sink")
                .Edge(1, 27).Edge(27, 3).Edge(3, 4);
            ISimHostBuilder b = FlowKit.Builder(Graphs.Content(Graphs.Lane(Fx.One)));
            IWorldSystem world = g.World(b);
            FlowGraph graph = FlowFactory.CreateGraphLoader().Load(Fixtures.Utf8(g.FlowJson()), "g.flow.json", world);
            FormatException ex = Assert.Throws<FormatException>(() => FlowFactory.CreateSystem(b.Services, graph, world));
            Assert.StartsWith("sim.flow: ", ex.Message, StringComparison.Ordinal);
            Assert.Contains("27", ex.Message, StringComparison.Ordinal);
            Assert.Contains("queue_missing", ex.Message, StringComparison.Ordinal);
        }
    }
}
