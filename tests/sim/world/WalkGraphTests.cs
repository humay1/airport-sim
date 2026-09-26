using System;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// IWalkGraphLoader (18 §18.2, Q-030): the strict JSON subset, the
    /// load-time validation, FixtureHash over the exact bytes, and the failure
    /// contract. Q-030: tests assert the exception type, the sourceName prefix
    /// and the id (or line), and nothing else in the message.
    /// </summary>
    public sealed class WalkGraphTests
    {
        private const string Source = "bad.json";

        private static FormatException Rejects(string json)
        {
            FormatException ex = Assert.Throws<FormatException>(() => WorldKit.Load(json, Source));
            Assert.StartsWith(Source + ": ", ex.Message, StringComparison.Ordinal);
            return ex;
        }

        private static void RejectsNaming(string json, string id)
        {
            FormatException ex = Rejects(json);
            Assert.Contains(id, ex.Message, StringComparison.Ordinal);
        }

        private static void RejectsAtLine(string json, int line)
        {
            FormatException ex = Rejects(json);
            Assert.Contains("line " + line, ex.Message, StringComparison.Ordinal);
        }

        private static string Doc(string nodes, string edges)
        {
            return "{\"schema_version\": 1, \"nodes\": [" + nodes + "], \"edges\": [" + edges + "]}";
        }

        private const string TwoNodes = "{\"id\": 4101, \"length_metres\": 5}, {\"id\": 4102, \"length_metres\": 6}";

        [Fact]
        public void test_walk_graph_loads_minimal_file()
        {
            WalkGraph g = WorldKit.Load(Doc(TwoNodes, "{\"id\": 7, \"from\": 4101, \"to\": 4102}"));
            Assert.Equal("N4101:5;N4102:6;E7:4101>4102;", WorldKit.Content(g));
        }

        [Fact]
        public void test_walk_graph_load_sorts_nodes_and_edges_ascending()
        {
            string json = Doc(
                "{\"id\": 30, \"length_metres\": 3}, {\"id\": 10, \"length_metres\": 1}, {\"id\": 20, \"length_metres\": 2}",
                "{\"id\": 9, \"from\": 10, \"to\": 20}, {\"id\": 2, \"from\": 20, \"to\": 30}, {\"id\": 5, \"from\": 30, \"to\": 10}");
            WalkGraph g = WorldKit.Load(json);
            Assert.Equal("N10:1;N20:2;N30:3;E2:20>30;E5:30>10;E9:10>20;", WorldKit.Content(g));
        }

        [Fact]
        public void test_walk_graph_accepts_empty_edges_any_key_order_and_json_whitespace()
        {
            string json = "\r\n\t{ \"edges\" :[ ],\n\"nodes\":[{\"length_metres\":0,\"id\":1}] ,\t\"schema_version\" : 1 }\r\n";
            WalkGraph g = WorldKit.Load(json);
            Assert.Equal("N1:0;", WorldKit.Content(g));
        }

        [Fact]
        public void test_walk_graph_accepts_uint32_extremes()
        {
            WalkGraph g = WorldKit.Load(Doc("{\"id\": 4294967295, \"length_metres\": 4294967295}", ""));
            Assert.Equal("N4294967295:4294967295;", WorldKit.Content(g));
        }

        [Fact]
        public void test_walk_graph_fixture_hash_is_fnv_over_exact_bytes()
        {
            string a = Doc(TwoNodes, "");
            string b = a.Replace(", ", ",  ");
            WalkGraph ga = WorldKit.Load(a);
            WalkGraph gb = WorldKit.Load(b);
            Assert.Equal(WorldKit.Fnv1a64(WorldKit.Utf8(a)), ga.FixtureHash);
            Assert.Equal(WorldKit.Fnv1a64(WorldKit.Utf8(b)), gb.FixtureHash);
            Assert.NotEqual(ga.FixtureHash, gb.FixtureHash);
            Assert.Equal(WorldKit.Content(ga), WorldKit.Content(gb));

            byte[] fixture = Phase0Landside.ReadFixture();
            Assert.Equal(WorldKit.Fnv1a64(fixture), Phase0Landside.Load().FixtureHash);
        }

        [Fact]
        public void test_walk_graph_rejects_unknown_edge_endpoint()
        {
            RejectsNaming(Doc(TwoNodes, "{\"id\": 7, \"from\": 4109, \"to\": 4102}"), "4109");
            RejectsNaming(Doc(TwoNodes, "{\"id\": 7, \"from\": 4101, \"to\": 4108}"), "4108");
        }

        [Fact]
        public void test_walk_graph_rejects_duplicate_edge()
        {
            // Same (from, to) under two different edge ids. 18 §18.2 lets the
            // message name "the offending id, or ids": the repeated edge's id
            // or the pair's node ids are both that, so either is accepted.
            FormatException ex = Rejects(
                Doc(TwoNodes, "{\"id\": 4177, \"from\": 4101, \"to\": 4102}, {\"id\": 4178, \"from\": 4101, \"to\": 4102}"));
            Assert.True(NamesEdgeOrNodes(ex.Message, "4178", "4101", "4102"), ex.Message);
        }

        private static bool NamesEdgeOrNodes(string message, string edgeId, string from, string to)
        {
            return message.Contains(edgeId, StringComparison.Ordinal)
                || (message.Contains(from, StringComparison.Ordinal) && message.Contains(to, StringComparison.Ordinal));
        }

        [Fact]
        public void test_walk_graph_rejects_duplicate_node_id()
        {
            RejectsNaming(Doc(TwoNodes + ", {\"id\": 4102, \"length_metres\": 9}", ""), "4102");
        }

        [Fact]
        public void test_walk_graph_rejects_duplicate_edge_id()
        {
            RejectsNaming(
                Doc(TwoNodes, "{\"id\": 4177, \"from\": 4101, \"to\": 4102}, {\"id\": 4177, \"from\": 4102, \"to\": 4101}"),
                "4177");
        }

        [Fact]
        public void test_walk_graph_rejects_self_loop()
        {
            // As for duplicates, the edge id or the looped node id is accepted.
            FormatException ex = Rejects(Doc(TwoNodes, "{\"id\": 4170, \"from\": 4102, \"to\": 4102}"));
            Assert.True(NamesEdgeOrNodes(ex.Message, "4170", "4102", "4102"), ex.Message);
        }

        [Fact]
        public void test_walk_graph_rejects_id_zero()
        {
            Rejects(Doc("{\"id\": 0, \"length_metres\": 5}", ""));
            Rejects(Doc(TwoNodes, "{\"id\": 0, \"from\": 4101, \"to\": 4102}"));
        }

        [Fact]
        public void test_walk_graph_rejects_out_of_range_integers()
        {
            Rejects(Doc("{\"id\": 4294967296, \"length_metres\": 5}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": 4294967296}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": -1}", ""));
            Rejects(Doc("{\"id\": -3, \"length_metres\": 1}", ""));
        }

        [Fact]
        public void test_walk_graph_rejects_non_integer_values()
        {
            Rejects(Doc("{\"id\": 1, \"length_metres\": 2.5}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": 1e2}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": 01}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": -0}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": null}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": true}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": \"5\"}", ""));
        }

        [Fact]
        public void test_walk_graph_rejects_schema_version_other_than_one()
        {
            Rejects("{\"schema_version\": 2, \"nodes\": [" + TwoNodes + "], \"edges\": []}");
            Rejects("{\"schema_version\": 0, \"nodes\": [" + TwoNodes + "], \"edges\": []}");
        }

        [Fact]
        public void test_walk_graph_rejects_missing_unknown_and_duplicate_keys()
        {
            Rejects("{\"nodes\": [" + TwoNodes + "], \"edges\": []}");
            Rejects("{\"schema_version\": 1, \"edges\": []}");
            Rejects("{\"schema_version\": 1, \"nodes\": [" + TwoNodes + "]}");
            Rejects("{\"schema_version\": 1, \"nodes\": [" + TwoNodes + "], \"edges\": [], \"extra\": 1}");
            Rejects("{\"schema_version\": 1, \"schema_version\": 1, \"nodes\": [" + TwoNodes + "], \"edges\": []}");
            Rejects(Doc("{\"id\": 1}", ""));
            Rejects(Doc("{\"id\": 1, \"length_metres\": 1, \"kind\": 2}", ""));
            Rejects(Doc("{\"id\": 1, \"id\": 2, \"length_metres\": 1}", ""));
            Rejects(Doc(TwoNodes, "{\"id\": 7, \"from\": 4101}"));
            Rejects(Doc(TwoNodes, "{\"id\": 7, \"from\": 4101, \"to\": 4102, \"length_metres\": 1}"));
        }

        [Fact]
        public void test_walk_graph_rejects_empty_nodes()
        {
            Rejects(Doc("", ""));
        }

        [Fact]
        public void test_walk_graph_rejects_wrong_shapes()
        {
            Rejects("[]");
            Rejects("{\"schema_version\": 1, \"nodes\": {}, \"edges\": []}");
            Rejects("{\"schema_version\": 1, \"nodes\": [1], \"edges\": []}");
            Rejects("{\"schema_version\": 1, \"nodes\": [" + TwoNodes + "], \"edges\": {}}");
        }

        [Fact]
        public void test_walk_graph_syntax_failure_names_line()
        {
            // Missing comma between the two node objects, on line 4.
            RejectsAtLine("{\n  \"schema_version\": 1,\n  \"nodes\": [\n    {\"id\": 1, \"length_metres\": 1} {\"id\": 2, \"length_metres\": 1}\n  ],\n  \"edges\": []\n}\n", 4);

            // Unterminated string on line 2.
            RejectsAtLine("{\n  \"schema_version: 1\n}\n", 2);

            // A fraction on line 5.
            RejectsAtLine("{\n  \"schema_version\": 1,\n  \"edges\": [],\n  \"nodes\": [\n    {\"id\": 1, \"length_metres\": 1.0}\n  ]\n}\n", 5);
        }

        [Fact]
        public void test_walk_graph_rejects_malformed_documents()
        {
            Rejects(string.Empty);
            Rejects("   ");
            Rejects(Doc(TwoNodes, "") + " {}");
            Rejects(Doc(TwoNodes, "").TrimEnd('}'));
            Rejects("{\"schema_version\": 1, \"nodes\": [" + TwoNodes + ",], \"edges\": []}");
        }

        [Fact]
        public void test_walk_graph_rejects_byte_order_mark()
        {
            byte[] body = WorldKit.Utf8(Doc(TwoNodes, ""));
            var withBom = new byte[body.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Array.Copy(body, 0, withBom, 3, body.Length);
            IWalkGraphLoader loader = WorldFactory.CreateGraphLoader();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Load(withBom, Source));
            Assert.StartsWith(Source + ": ", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_walk_graph_null_source_name_throws_argument_null()
        {
            IWalkGraphLoader loader = WorldFactory.CreateGraphLoader();
            byte[] body = WorldKit.Utf8(Doc(TwoNodes, ""));
            Assert.Throws<ArgumentNullException>(() => loader.Load(body, null!));
        }
    }
}
