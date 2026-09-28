using System;
using System.Collections.Generic;
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

        private static FormatException RejectsBytes(byte[] bytes)
        {
            FormatException ex = Assert.Throws<FormatException>(() => FlowFactory.CreateGraphLoader().Load(bytes, Source, World()));
            Assert.StartsWith(Source + ": ", ex.Message, StringComparison.Ordinal);
            return ex;
        }

        /// <summary>A JSON \u escape for the hex digits, built at run time.</summary>
        private static string U(string hex)
        {
            return "\\" + "u" + hex;
        }

        [Fact]
        public void test_flow_graph_accepts_json_string_escapes()
        {
            // 08 §8.11 "Strings" (Q-033): the eight JSON escapes plus \uXXXX in
            // either case; keys compare after unescaping. The escaped profile id
            // must resolve to queue_test_lane in CreateSystem.
            string q = "{\"id\": 12, \"k" + U("0069") + "nd\": \"qu" + U("0065") + "ue\", \"server_count\": 2, \"servers_open\": 1, \"queue_profile\": \"queue" + U("005F") + "test" + U("005f") + "lane\"}";
            string json = Doc("{\"id\": 11, \"kind\": \"sour" + U("0063") + "e\"}", q, N(13, "gate"), N(14, "sink"), N(15, "hall"));
            ISimHostBuilder b = FlowKit.Builder(Graphs.Content(Graphs.Lane(Fx.One)));
            var g = new TestGraph()
                .Node(11, "source").Queue(12, 2, 1, FlowKit.Lane).Node(13, "gate").Node(14, "sink").Node(15, "hall")
                .Edge(11, 12).Edge(12, 13).Edge(15, 13).Edge(13, 14);
            IWorldSystem world = g.World(b);
            FlowGraph graph = FlowFactory.CreateGraphLoader().Load(Fixtures.Utf8(json), Source, world);
            IFlowSystem flow = FlowFactory.CreateSystem(b.Services, graph, world);
            Assert.True(flow.TryGetLaneState(new NodeId(12), out LaneState lanes));
            Assert.Equal(2, lanes.ServerCount);

            // Every other standard escape and raw UTF-8 outside the BMP load
            // (the profile is only resolved in CreateSystem, not in Load).
            foreach (string id in new[] { "a\\\"b", "a\\\\b", "a\\/b", "a\\bb", "a\\fb", "a\\nb", "a\\rb", "a\\tb", "\\u00e9t\\u00C9", "pax_\U0001F600" })
            {
                string queue = "{\"id\": 12, \"kind\": \"queue\", \"server_count\": 2, \"servers_open\": 1, \"queue_profile\": \"" + id + "\"}";
                Load(Doc(N(11, "source"), queue, N(13, "gate"), N(14, "sink"), N(15, "hall")));
            }
        }

        [Fact]
        public void test_flow_graph_rejects_bad_string_escapes_and_control_characters()
        {
            string Queue(string profile) => "{\"id\": 12, \"kind\": \"queue\", \"server_count\": 2, \"servers_open\": 1, \"queue_profile\": \"" + profile + "\"}";
            // Escapes as JSON text (C# "\\" is one backslash), then raw control
            // characters (C# escapes producing U+0009, U+0001, U+001F).
            foreach (string bad in new[] { "a\\xb", "a\\u12", "a\\u12G4", "a\\uD800", "a\\udfff", "a\\uDBFF", "a\\U0041", "a\\'b", "a\\", "a\tb", "a\u0001b", "a\u001fb" })
            {
                Rejects(Doc(N(11, "source"), Queue(bad), N(13, "gate"), N(14, "sink"), N(15, "hall")));
            }

            // A raw newline inside a string is a control character too; the line
            // of a syntax failure is still reported.
            FormatException ex = Rejects("{\n\"schema_version\": 1,\n\"nodes\": [{\"id\": 11, \"kind\": \"sou\nrce\"}]\n}\n");
            Assert.Contains("line ", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_flow_graph_rejects_invalid_utf8_anywhere()
        {
            byte[] valid = Fixtures.Utf8(Valid());
            int kindAt = Fixtures.Utf8(Valid().Substring(0, Valid().IndexOf("hall", StringComparison.Ordinal))).Length;
            foreach (byte[] junk in new[]
            {
                new byte[] { 0xFF },               // never valid
                new byte[] { 0xC3 },               // truncated two-byte sequence
                new byte[] { 0xC0, 0xAF },         // overlong
                new byte[] { 0xED, 0xA0, 0x80 },   // encoded surrogate
                new byte[] { 0x80 },               // lone continuation
            })
            {
                // Inside a string value.
                var inString = new List<byte>(valid);
                inString.InsertRange(kindAt, junk);
                RejectsBytes(inString.ToArray());

                // Between tokens, outside any string.
                var outside = new List<byte>(valid);
                outside.InsertRange(valid.Length - 1, junk);
                RejectsBytes(outside.ToArray());
            }
        }
    }
}
