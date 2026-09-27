using System;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// IFlowGraphLoader (09 §9.11 "File format" and "Failures", Q-032): the
    /// strict JSON subset over sim.world's nodes; every failure a
    /// FormatException starting "sourceName: ", naming the line for syntax or
    /// shape and the node id for validation.
    /// </summary>
    public sealed class FlowGraphTests
    {
        private const string Source = "bad.flow.json";

        // World: 11 Source -> 12 Queue -> 13 Gate -> 14 Sink, plus 15 Hall -> 13.
        private static IWorldSystem World(bool sinkHasExit = false, bool gateHasSink = true)
        {
            var g = new TestGraph()
                .Node(11, "source").Queue(12, 2, 1, FlowKit.Lane).Node(13, "gate").Node(14, "sink").Node(15, "hall")
                .Edge(11, 12).Edge(12, 13).Edge(15, 13);
            if (gateHasSink)
            {
                g.Edge(13, 14);
            }
            else
            {
                g.Edge(13, 15);
            }

            if (sinkHasExit)
            {
                g.Edge(14, 15);
            }

            ISimHostBuilder b = FlowKit.Builder(Graphs.Content(Graphs.Lane(Fx.One)));
            return g.World(b);
        }

        private const string Q12 = "{\"id\": 12, \"kind\": \"queue\", \"server_count\": 2, \"servers_open\": 1, \"queue_profile\": \"queue_test_lane\"}";

        private static string Doc(params string[] nodes)
        {
            return "{\"schema_version\": 1, \"nodes\": [" + string.Join(", ", nodes) + "]}";
        }

        private static string N(uint id, string kind)
        {
            return "{\"id\": " + id + ", \"kind\": \"" + kind + "\"}";
        }

        private static string Valid()
        {
            return Doc(N(11, "source"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall"));
        }

        private static FlowGraph Load(string json, IWorldSystem? world = null)
        {
            return FlowFactory.CreateGraphLoader().Load(Fixtures.Utf8(json), Source, world ?? World());
        }

        private static FormatException Rejects(string json, IWorldSystem? world = null)
        {
            FormatException ex = Assert.Throws<FormatException>(() => Load(json, world));
            Assert.StartsWith(Source + ": ", ex.Message, StringComparison.Ordinal);
            return ex;
        }

        private static void RejectsNaming(string json, string id, IWorldSystem? world = null)
        {
            FormatException ex = Rejects(json, world);
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_flow_graph_loads_the_phase0_fixture()
        {
            IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.One),
                FlowKit.Queue(Landside.SecurityProfile, Fx.One, 50, Fx.FromInt(10), Fx.FromInt(2)),
            });
            var rig = Rig.Fixture(content);
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(Landside.SecurityA), out LaneState a));
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(Landside.SecurityB), out LaneState b));
            Assert.Equal(3, a.ServerCount);
            Assert.Equal(1, a.ServersOpen);
            Assert.Equal(3, b.ServerCount);
            Assert.False(rig.Flow.TryGetLaneState(new NodeId(Landside.LandsideCorridor), out _));
        }

        [Fact]
        public void test_flow_graph_accepts_any_node_order_and_key_order()
        {
            string json = "{\"nodes\": [" + N(15, "hall") + ", " + N(14, "sink") + ", "
                + "{\"queue_profile\": \"queue_test_lane\", \"servers_open\": 1, \"kind\": \"queue\", \"server_count\": 2, \"id\": 12}, "
                + N(13, "gate") + ", {\"kind\": \"source\", \"id\": 11}], \"schema_version\": 1}";
            Load(json);
            Load(Valid());
        }

        [Fact]
        public void test_flow_graph_rejects_world_node_without_definition()
        {
            RejectsNaming(Doc(N(11, "source"), Q12, N(13, "gate"), N(14, "sink")), "15");
        }

        [Fact]
        public void test_flow_graph_rejects_definition_of_unknown_node()
        {
            RejectsNaming(Doc(N(11, "source"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall"), N(4099, "hall")), "4099");
        }

        [Fact]
        public void test_flow_graph_rejects_duplicate_definition()
        {
            RejectsNaming(Doc(N(11, "source"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall"), N(15, "corridor")), "15");
        }

        [Fact]
        public void test_flow_graph_rejects_source_that_reaches_no_gate()
        {
            // Declaring 13 a hall leaves the graph with no Gate for source 11.
            RejectsNaming(Doc(N(11, "source"), Q12, N(13, "hall"), N(14, "sink"), N(15, "hall")), "11");
        }

        [Fact]
        public void test_flow_graph_rejects_gate_without_sink_successor()
        {
            RejectsNaming(Valid(), "13", World(gateHasSink: false));
        }

        [Fact]
        public void test_flow_graph_rejects_sink_with_outbound_edge()
        {
            RejectsNaming(Valid(), "14", World(sinkHasExit: true));
        }

        [Fact]
        public void test_flow_graph_rejects_servers_open_above_server_count()
        {
            string q = "{\"id\": 12, \"kind\": \"queue\", \"server_count\": 2, \"servers_open\": 3, \"queue_profile\": \"queue_test_lane\"}";
            FormatException ex = Rejects(Doc(N(11, "source"), q, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Assert.Contains("12", ex.Message, StringComparison.Ordinal);
            Assert.Contains("servers_open", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_flow_graph_rejects_bad_queue_fields()
        {
            string Q(string fields) => "{\"id\": 12, \"kind\": \"queue\", " + fields + "}";
            string[] bad =
            {
                Q("\"server_count\": 0, \"servers_open\": 0, \"queue_profile\": \"queue_test_lane\""),
                Q("\"server_count\": 2, \"servers_open\": -1, \"queue_profile\": \"queue_test_lane\""),
                Q("\"server_count\": 2, \"queue_profile\": \"queue_test_lane\""),
                Q("\"server_count\": 2, \"servers_open\": 1"),
                Q("\"server_count\": 2, \"servers_open\": 1, \"queue_profile\": 5"),
                Q("\"server_count\": 2, \"servers_open\": 1, \"queue_profile\": \"queue_test_lane\", \"capacity\": 4"),
                Q("\"server_count\": 2.0, \"servers_open\": 1, \"queue_profile\": \"queue_test_lane\""),
            };
            foreach (string q in bad)
            {
                Rejects(Doc(N(11, "source"), q, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            }
        }

        [Fact]
        public void test_flow_graph_rejects_bad_shapes_and_kinds()
        {
            Rejects(Doc(N(11, "fountain"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Rejects(Doc(N(11, "Source"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Rejects(Doc("{\"id\": 11, \"kind\": \"source\", \"server_count\": 1}", Q12, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Rejects(Doc("{\"id\": 11}", Q12, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Rejects(Doc("{\"id\": 0, \"kind\": \"source\"}", N(11, "source"), Q12, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            Rejects("{\"schema_version\": 2, \"nodes\": [" + N(11, "source") + ", " + Q12 + ", " + N(13, "gate") + ", " + N(14, "sink") + ", " + N(15, "hall") + "]}");
            Rejects("{\"nodes\": [" + N(11, "source") + "]}");
            Rejects("{\"schema_version\": 1, \"nodes\": [], \"extra\": 1}");
            Rejects("[]");
            Rejects(string.Empty);
        }

        [Fact]
        public void test_flow_graph_syntax_failure_names_line()
        {
            string json = "{\n  \"schema_version\": 1,\n  \"nodes\": [\n    {\"id\": 11, \"kind\": \"source\"}\n    {\"id\": 13, \"kind\": \"gate\"}\n  ]\n}\n";
            FormatException ex = Rejects(json);
            Assert.Contains("line 5", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_flow_graph_null_arguments_throw_argument_null()
        {
            IFlowGraphLoader loader = FlowFactory.CreateGraphLoader();
            byte[] bytes = Fixtures.Utf8(Valid());
            IWorldSystem world = World();
            Assert.Throws<ArgumentNullException>(() => loader.Load(bytes, null!, world));
            Assert.Throws<ArgumentNullException>(() => loader.Load(bytes, Source, null!));
        }
    }
}
