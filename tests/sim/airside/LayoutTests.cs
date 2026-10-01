using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.4 load-time validation (Q-046): checks run in a fixed order
    /// (ranges, non-empty, unique ids, references, kinds, connected), and a
    /// failure is a FormatException starting "sim.airside: " that contains
    /// the pinned field name and ids in decimal. Per §12.4, tests assert the
    /// type, the prefix, the field name and the ids, and nothing else. Ids in
    /// the broken cases appear nowhere else in the layout. Then §12.4 "File
    /// format": Parse of the §12.13 fixture, its parse failures (with
    /// "line n") and Load failures reached through Parse (without).
    /// </summary>
    public sealed class LayoutTests
    {
        private const string Bad = "bad.json";

        private static void AssertNames(string message, string field, params int[] ids)
        {
            Assert.True(
                Regex.IsMatch(message, @"(?<![A-Za-z0-9_])" + Regex.Escape(field) + @"(?![A-Za-z0-9_])"),
                "expected the message to name field " + field + ": " + message);
            foreach (int id in ids)
            {
                string s = id.ToString(CultureInfo.InvariantCulture);
                Assert.True(Regex.IsMatch(message, @"(?<![0-9])" + s + @"(?![0-9])"), "expected the message to name id " + s + ": " + message);
            }
        }

        private static void AssertRejects(LayoutBuilder b, string field, params int[] ids)
        {
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            AirsideLayout raw = b.Raw();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Load(raw));
            Assert.StartsWith("sim.airside: ", ex.Message, StringComparison.Ordinal);
            AssertNames(ex.Message, field, ids);
        }

        [Fact]
        public void test_layout_rejects_disconnected_graph()
        {
            // Check 6: stand 44 on node 44, which no edge reaches.
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(44, TaxiNodeKind.StandPosition)
                .Stand(44, 44, AirsideContent.Super, 944);
            AssertRejects(b, "nodes", 44);
        }

        [Fact]
        public void test_layout_rejects_stand_that_cannot_reach_the_threshold_back()
        {
            // Check 6 is both ways: a one-way edge into node 45 leaves no way out.
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(45, TaxiNodeKind.StandPosition)
                .Edge(45, FixtureLayout.J1, 45, 10, bidirectional: false)
                .Stand(45, 45, AirsideContent.Super, 945);
            AssertRejects(b, "nodes", 45);
        }

        [Fact]
        public void test_layout_rejects_edge_to_undeclared_node()
        {
            AssertRejects(FixtureLayout.Builder().Edge(46, FixtureLayout.J2, 77, 10), "to", 46, 77);
        }

        [Fact]
        public void test_layout_rejects_edge_from_undeclared_node()
        {
            AssertRejects(FixtureLayout.Builder().Edge(46, 76, FixtureLayout.J2, 10), "from", 46, 76);
        }

        [Fact]
        public void test_layout_rejects_stand_on_undeclared_node()
        {
            AssertRejects(FixtureLayout.Builder().Stand(47, 78, AirsideContent.Super, 947), "node", 47, 78);
        }

        [Fact]
        public void test_layout_rejects_runway_with_undeclared_threshold()
        {
            AssertRejects(FixtureLayout.Builder().Runway(48, 79, 15, 10), "threshold_node", 48, 79);
        }

        [Fact]
        public void test_layout_rejects_duplicate_ids_naming_list_and_id()
        {
            AssertRejects(
                FixtureLayout.Builder()
                    .Node(50, TaxiNodeKind.StandPosition).Node(51, TaxiNodeKind.StandPosition)
                    .Edge(50, FixtureLayout.J2, 50, 10).Edge(51, FixtureLayout.J2, 51, 10)
                    .Stand(61, 50, AirsideContent.Super, 950).Stand(61, 51, AirsideContent.Super, 951),
                "stands",
                61);
            AssertRejects(
                FixtureLayout.Builder().Node(62, TaxiNodeKind.Junction).Node(62, TaxiNodeKind.Junction).Edge(52, FixtureLayout.J2, 62, 10),
                "nodes",
                62);
            AssertRejects(
                FixtureLayout.Builder().Edge(63, FixtureLayout.J1, FixtureLayout.J2, 40).Edge(63, FixtureLayout.Threshold, FixtureLayout.J2, 50),
                "edges",
                63);
            AssertRejects(
                FixtureLayout.Builder().Node(53, TaxiNodeKind.RunwayThreshold).Edge(53, 53, FixtureLayout.J1, 30)
                    .Runway(64, FixtureLayout.Threshold, 15, 10).Runway(64, 53, 15, 10),
                "runways",
                64);
        }

        [Fact]
        public void test_layout_rejects_out_of_range_values_naming_field_and_object()
        {
            AssertRejects(FixtureLayout.Builder().Runway(65, FixtureLayout.Threshold, 15, 0), "occupancy_ticks", 65);
            AssertRejects(FixtureLayout.Builder().Runway(68, FixtureLayout.Threshold, 0, 10), "declared_capacity_per_hour", 68);
            AssertRejects(FixtureLayout.Builder().Runway(67, FixtureLayout.Threshold, 15, 10, directionDeg: 360), "active_direction_deg", 67);
            AssertRejects(FixtureLayout.Builder().Runway(67, FixtureLayout.Threshold, 15, 10, directionDeg: -1), "active_direction_deg", 67);
            AssertRejects(FixtureLayout.Builder().Edge(66, FixtureLayout.J1, FixtureLayout.J2, 0), "traversal_ticks", 66);
            AssertRejects(FixtureLayout.Builder().Stand(69, FixtureLayout.StandNode(FixtureLayout.S4), AirsideContent.Super, 0), "departure_sink_node", 69);
            AssertRejects(FixtureLayout.Builder().Node(0, TaxiNodeKind.Junction), "id", 0);
        }

        [Fact]
        public void test_layout_rejects_empty_runway_or_stand_list()
        {
            LayoutBuilder noStands = FixtureLayout.Builder();
            noStands.Stands.Clear();
            AssertRejects(noStands, "stands");

            LayoutBuilder noRunways = FixtureLayout.Builder();
            noRunways.Runways.Clear();
            AssertRejects(noRunways, "runways");
        }

        [Fact]
        public void test_layout_rejects_wrong_node_kind_for_stand_or_threshold()
        {
            AssertRejects(
                FixtureLayout.Builder().Node(71, TaxiNodeKind.Junction).Edge(71, FixtureLayout.J2, 71, 10).Stand(70, 71, AirsideContent.Super, 970),
                "node",
                70,
                71);
            AssertRejects(
                FixtureLayout.Builder().Node(73, TaxiNodeKind.Junction).Edge(73, 73, FixtureLayout.J1, 10).Runway(72, 73, 15, 10),
                "threshold_node",
                72,
                73);
        }

        [Fact]
        public void test_layout_checks_run_in_pinned_order()
        {
            // A range failure (check 1) and a duplicate (check 3): ranges win.
            AssertRejects(
                FixtureLayout.Builder().Runway(65, FixtureLayout.Threshold, 15, 0)
                    .Node(50, TaxiNodeKind.StandPosition).Edge(50, FixtureLayout.J2, 50, 10)
                    .Stand(61, 50, AirsideContent.Super, 950).Stand(61, 50, AirsideContent.Super, 951),
                "occupancy_ticks",
                65);

            // A duplicate (check 3) and an undeclared reference (check 4): the duplicate wins.
            AssertRejects(
                FixtureLayout.Builder().Edge(63, FixtureLayout.J1, FixtureLayout.J2, 40).Edge(63, FixtureLayout.J1, FixtureLayout.J2, 40)
                    .Edge(46, FixtureLayout.J2, 77, 10),
                "edges",
                63);

            // An undeclared reference (check 4) and a disconnected stand (check 6): the reference wins.
            AssertRejects(
                FixtureLayout.Builder().Edge(46, FixtureLayout.J2, 77, 10).Node(44, TaxiNodeKind.StandPosition).Stand(44, 44, AirsideContent.Super, 944),
                "to",
                46,
                77);
        }

        [Fact]
        public void test_layout_load_returns_lists_sorted_by_id()
        {
            // Declared in descending id order; Load returns ascending (12 §12.4).
            var b = new LayoutBuilder()
                .Node(14, TaxiNodeKind.StandPosition).Node(13, TaxiNodeKind.StandPosition)
                .Node(2, TaxiNodeKind.Junction).Node(1, TaxiNodeKind.RunwayThreshold)
                .Edge(9, 2, 14, 10).Edge(5, 2, 13, 10).Edge(1, 1, 2, 30)
                .Stand(4, 14, AirsideContent.Super, 904).Stand(3, 13, AirsideContent.Super, 903)
                .Node(20, TaxiNodeKind.RunwayThreshold).Edge(20, 20, 2, 30)
                .Runway(7, 20, 15, 10).Runway(1, 1, 15, 10);
            AirsideLayout loaded = b.Load();
            Assert.Equal(new[] { 1, 7 }, Ids(loaded.Runways, r => r.Id.Value));
            Assert.Equal(new[] { 1, 2, 13, 14, 20 }, Ids(loaded.Nodes, n => n.Id.Value));
            Assert.Equal(new[] { 1, 5, 9, 20 }, Ids(loaded.Edges, e => e.Id.Value));
            Assert.Equal(new[] { 3, 4 }, Ids(loaded.Stands, s => s.Id.Value));
        }

        [Fact]
        public void test_layout_load_accepts_valid_layout_and_keeps_its_content()
        {
            // Declared in reverse, so Load must sort as well as keep every field.
            LayoutBuilder b = FixtureLayout.Builder();
            b.Nodes.Reverse();
            b.Edges.Reverse();
            b.Stands.Reverse();
            AssertSameLayout(FixtureLayout.Builder().Raw(), b.Load());
        }

        [Fact]
        public void test_layout_parse_fixture_file_equals_built_layout()
        {
            AssertSameLayout(FixtureLayout.Layout(), AirsideFixture.Parse());
        }

        [Fact]
        public void test_layout_parse_ignores_key_and_array_order()
        {
            // Keys may come in any order (12 §12.4); Load sorts every list by id.
            List<string> lines = FixtureLines();
            lines[3] = "    { \"occupancy_ticks\": 10, \"declared_capacity_per_hour\": 15, \"id\": 1, \"active_direction_deg\": 270, \"threshold_node\": 1 }";
            var nodes = lines.GetRange(6, 7).ConvertAll(l => l.TrimEnd(','));
            nodes.Reverse();
            for (int i = 0; i < nodes.Count; i++)
            {
                lines[6 + i] = nodes[i] + (i < nodes.Count - 1 ? "," : string.Empty);
            }

            AirsideLayout parsed = AirsideFactory.CreateLayoutLoader().Parse(Join(lines), "reordered.json");
            AssertSameLayout(FixtureLayout.Layout(), parsed);
        }

        [Fact]
        public void test_layout_parse_rejects_unknown_key_with_line_number()
        {
            List<string> lines = FixtureLines();
            lines[7] = "    { \"id\": 2, \"kind\": \"junction\", \"colour\": \"red\" },";
            AssertParseFails(lines, 8);
        }

        [Fact]
        public void test_layout_parse_rejects_shape_syntax_and_type_range_with_line_number()
        {
            // Each case breaks one line of the fixture; each object sits on one line.
            var cases = new (int Line, string Text)[]
            {
                (2, "  \"schema_version\": 2,"),
                (4, "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10.5 }"),
                (7, "    { \"id\": true, \"kind\": \"runway_threshold\" },"),
                (9, "    { \"id\": 3, \"id\": 3, \"kind\": \"junction\" },"),
                (10, "    { \"id\": 011, \"kind\": \"stand_position\" },"),
                (11, "    { id: 12, \"kind\": \"stand_position\" },"),
                (12, "    { \"id\": 13, \"kind\": \"hangar\" },"),
                (16, "    { \"id\": 1, \"from\": 1, \"to\": 2, \"traversal_ticks\": 30 },"),
                (17, "    { \"id\": 2, \"from\": 2, \"to\": 11, \"traversal_ticks\": 20, \"bidirectional\": 1 },"),
                (24, "    { \"id\": 1, \"node\": 70000, \"max_aircraft_size_category\": \"medium\", \"departure_sink_node\": 901 },"),
                (25, "    { \"id\": 2, \"node\": 12, \"max_aircraft_size_category\": \"super\", \"departure_sink_node\": -902 },"),
            };
            foreach ((int line, string text) in cases)
            {
                List<string> lines = FixtureLines();
                lines[line - 1] = text;
                AssertParseFails(lines, line);
            }
        }

        [Fact]
        public void test_layout_parse_range_failure_has_no_line_number()
        {
            // "id": 0 and "occupancy_ticks": 0 parse as uint16/uint32; they are Load's failures.
            List<string> stand = FixtureLines();
            stand[26] = "    { \"id\": 0, \"node\": 14, \"max_aircraft_size_category\": \"super\", \"departure_sink_node\": 904 }";
            AssertLoadFailsThroughParse(stand, "id", 0);

            List<string> runway = FixtureLines();
            runway[3] = "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 0 }";
            AssertLoadFailsThroughParse(runway, "occupancy_ticks", 1);
        }

        [Fact]
        public void test_layout_parse_null_source_name_throws_argument_null()
        {
            byte[] file = AirsideFixture.Bytes();
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            Assert.Throws<ArgumentNullException>(() => loader.Parse(file, null!));
        }

        private static void AssertParseFails(List<string> lines, int line)
        {
            byte[] file = Join(lines);
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Parse(file, Bad));
            Assert.StartsWith(Bad + ": ", ex.Message, StringComparison.Ordinal);
            string n = line.ToString(CultureInfo.InvariantCulture);
            Assert.True(Regex.IsMatch(ex.Message, @"(?<![A-Za-z0-9_])line " + n + @"(?![0-9])"), "expected line " + n + ": " + ex.Message);
        }

        private static void AssertLoadFailsThroughParse(List<string> lines, string field, params int[] ids)
        {
            byte[] file = Join(lines);
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Parse(file, Bad));
            Assert.StartsWith(Bad + ": sim.airside: ", ex.Message, StringComparison.Ordinal);
            AssertNames(ex.Message, field, ids);
            Assert.False(Regex.IsMatch(ex.Message, @"(?<![A-Za-z0-9_])line [0-9]"), "a Load failure carries no line number: " + ex.Message);
        }

        /// <summary>The fixture file's lines, after checking the layout this suite's line numbers assume.</summary>
        private static List<string> FixtureLines()
        {
            var lines = new List<string>(Encoding.UTF8.GetString(AirsideFixture.Bytes()).Split('\n'));
            Assert.Equal(string.Empty, lines[lines.Count - 1]);
            lines.RemoveAt(lines.Count - 1);
            Assert.Equal(29, lines.Count);
            Assert.Contains("\"runways\"", lines[2], StringComparison.Ordinal);
            Assert.Contains("\"nodes\"", lines[5], StringComparison.Ordinal);
            Assert.Contains("\"edges\"", lines[14], StringComparison.Ordinal);
            Assert.Contains("\"stands\"", lines[22], StringComparison.Ordinal);
            return lines;
        }

        private static byte[] Join(List<string> lines)
        {
            return Csv.Utf8(string.Join("\n", lines) + "\n");
        }

        private static int[] Ids<T>(IReadOnlyList<T> items, Func<T, int> id)
        {
            var ids = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                ids[i] = id(items[i]);
            }

            return ids;
        }

        /// <summary>Same objects, field by field, ignoring list order on the expected side; the actual stands come sorted.</summary>
        internal static void AssertSameLayout(AirsideLayout expected, AirsideLayout actual)
        {
            Assert.Equal(
                Sorted(expected.Runways, r => Key(r.Id.Value, r.ThresholdNode.Value, r.ActiveDirectionDeg, r.DeclaredCapacityPerHour, r.OccupancyTicks)),
                Sorted(actual.Runways, r => Key(r.Id.Value, r.ThresholdNode.Value, r.ActiveDirectionDeg, r.DeclaredCapacityPerHour, r.OccupancyTicks)));
            Assert.Equal(Sorted(expected.Nodes, n => Key(n.Id.Value, n.Kind)), Sorted(actual.Nodes, n => Key(n.Id.Value, n.Kind)));
            Assert.Equal(
                Sorted(expected.Edges, e => Key(e.Id.Value, e.From.Value, e.To.Value, e.TraversalTicks, e.Bidirectional)),
                Sorted(actual.Edges, e => Key(e.Id.Value, e.From.Value, e.To.Value, e.TraversalTicks, e.Bidirectional)));
            Assert.Equal(
                Sorted(expected.Stands, s => Key(s.Id.Value, s.Node.Value, s.MaxAircraftSizeCategory.Value, s.DepartureSinkNode.Value)),
                Sorted(actual.Stands, s => Key(s.Id.Value, s.Node.Value, s.MaxAircraftSizeCategory.Value, s.DepartureSinkNode.Value)));
            int[] stands = Ids(actual.Stands, s => s.Id.Value);
            int[] sorted = (int[])stands.Clone();
            Array.Sort(sorted);
            Assert.Equal(sorted, stands);
        }

        private static string Key(params object[] parts)
        {
            return string.Join("/", Array.ConvertAll(parts, p => Convert.ToString(p, CultureInfo.InvariantCulture)));
        }

        private static string[] Sorted<T>(IReadOnlyList<T> items, Func<T, string> key)
        {
            var keys = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                keys[i] = key(items[i]);
            }

            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }
    }
}
