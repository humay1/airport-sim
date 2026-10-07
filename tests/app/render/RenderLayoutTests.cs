using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Airside;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.4 "File format" and load-time validation (Q-094). Tests assert
    /// the exception type, the sourceName prefix and the named id, and
    /// nothing else in the message.
    /// </summary>
    public sealed class RenderLayoutTests
    {
        private const string SourceName = "phase1-layout.json";

        /// <summary>A layout file in §15.4's shape, from (id, values) rows.</summary>
        private static byte[] Json(
            IEnumerable<(int Node, int X, int Y)> taxi,
            IEnumerable<(int Runway, int X0, int Y0, int X1, int Y1, int Width)> runways,
            IEnumerable<(int Node, int MinX, int MinY, int MaxX, int MaxY, int Fill)> boxes)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema_version\": 1,\n  \"taxi_nodes\": [");
            string sep = "\n";
            foreach ((int n, int x, int y) in taxi)
            {
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"node\": {0}, \"x\": {1}, \"y\": {2} }}", n, x, y));
                sep = ",\n";
            }

            sb.Append("\n  ],\n  \"runways\": [");
            sep = "\n";
            foreach ((int r, int x0, int y0, int x1, int y1, int w) in runways)
            {
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"runway\": {0}, \"x0\": {1}, \"y0\": {2}, \"x1\": {3}, \"y1\": {4}, \"width\": {5} }}", r, x0, y0, x1, y1, w));
                sep = ",\n";
            }

            sb.Append("\n  ],\n  \"flow_nodes\": [");
            sep = "\n";
            foreach ((int n, int a, int b, int c, int d, int f) in boxes)
            {
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"node\": {0}, \"min_x\": {1}, \"min_y\": {2}, \"max_x\": {3}, \"max_y\": {4}, \"fill_capacity\": {5} }}", n, a, b, c, d, f));
                sep = ",\n";
            }

            sb.Append("\n  ],\n  \"stand_size\": 40,\n  \"aircraft_size\": 30,\n  \"agent_size\": 2,\n  \"taxiway_width\": 15\n}\n");
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        /// <summary>Phase1RenderLayout's rows, minus the skipped taxi nodes, plus extra runway geometry.</summary>
        private static byte[] Phase1Json(int[]? skipTaxi = null, int[]? extraRunways = null)
        {
            RenderLayout l = Phase1RenderLayout.Build();
            var taxi = new List<(int, int, int)>();
            foreach (TaxiNodePosition p in l.TaxiNodes)
            {
                if (skipTaxi == null || Array.IndexOf(skipTaxi, (int)p.Node.Value) < 0)
                {
                    taxi.Add((p.Node.Value, p.X, p.Y));
                }
            }

            var runways = new List<(int, int, int, int, int, int)>();
            foreach (RunwayGeometry g in l.Runways)
            {
                runways.Add((g.Runway.Value, g.X0, g.Y0, g.X1, g.Y1, g.Width));
            }

            foreach (int r in extraRunways ?? Array.Empty<int>())
            {
                runways.Add((r, -2000, 500, 0, 500, 45));
            }

            var boxes = new List<(int, int, int, int, int, int)>();
            foreach (FlowNodeBox b in l.FlowNodes)
            {
                boxes.Add(((int)b.Node.Value, b.MinX, b.MinY, b.MaxX, b.MaxY, b.FillCapacity));
            }

            return Json(taxi, runways, boxes);
        }

        private static FormatException Rejects(byte[] file, AirsideLayout? airside)
        {
            IRenderLayoutLoader loader = RenderFactory.CreateLayoutLoader();
            return Assert.Throws<FormatException>(() => loader.Load(file, SourceName, airside));
        }

        private static void AssertNames(FormatException ex, string id)
        {
            Assert.StartsWith(SourceName + ": ", ex.Message, StringComparison.Ordinal);
            Assert.True(Regex.IsMatch(ex.Message, "(?<![0-9])" + id + "(?![0-9])"), "message does not name id " + id + ": " + ex.Message);
        }

        private static string Show(in RenderLayout l)
        {
            var sb = new StringBuilder();
            foreach (TaxiNodePosition p in l.TaxiNodes)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "T{0}({1},{2}) ", p.Node.Value, p.X, p.Y));
            }

            foreach (RunwayGeometry g in l.Runways)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "R{0}({1},{2},{3},{4},{5}) ", g.Runway.Value, g.X0, g.Y0, g.X1, g.Y1, g.Width));
            }

            foreach (FlowNodeBox b in l.FlowNodes)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "F{0}({1},{2},{3},{4},{5}) ", b.Node.Value, b.MinX, b.MinY, b.MaxX, b.MaxY, b.FillCapacity));
            }

            sb.Append(string.Format(CultureInfo.InvariantCulture, "sizes {0} {1} {2} {3}", l.StandSize, l.AircraftSize, l.AgentSize, l.TaxiwayWidth));
            foreach (LayoutArea a in l.Areas)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, " A{0}:{1}({2},{3},{4},{5})", a.Id, a.Kind, a.MinX, a.MinY, a.MaxX, a.MaxY));
            }

            foreach (LayoutBridge b in l.Bridges)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, " B{0}({1},{2},{3},{4},{5})", b.Id, b.X0, b.Y0, b.X1, b.Y1, b.Width));
            }

            return sb.ToString();
        }

        /// <summary>
        /// A version 2 file (15 §15.4, Q-130): the Phase1RenderLayout rows,
        /// then the given area and bridge rows, one object per line. The
        /// schema_version is on line 2. Area rows are (id, kind, minX, minY,
        /// maxX, maxY); kind is written verbatim inside quotes.
        /// </summary>
        private static string V2(
            IEnumerable<(uint Id, string Kind, int MinX, int MinY, int MaxX, int MaxY)> areas,
            IEnumerable<(uint Id, int X0, int Y0, int X1, int Y1, int Width)> bridges,
            int version = 2,
            int standSize = 40)
        {
            string v1 = Encoding.UTF8.GetString(Phase1Json());
            Assert.StartsWith("{\n  \"schema_version\": 1,\n", v1, StringComparison.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema_version\": ").Append(version.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            string body = v1.Substring("{\n  \"schema_version\": 1,\n".Length);
            body = body.Substring(0, body.LastIndexOf('}')).TrimEnd();
            body = body.Replace("\"stand_size\": 40", "\"stand_size\": " + standSize.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            sb.Append(body).Append(",\n  \"areas\": [");
            string sep = "\n";
            foreach ((uint id, string kind, int a, int b, int c, int d) in areas)
            {
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"id\": {0}, \"kind\": \"{1}\", \"min_x\": {2}, \"min_y\": {3}, \"max_x\": {4}, \"max_y\": {5} }}", id, kind, a, b, c, d));
                sep = ",\n";
            }

            sb.Append("\n  ],\n  \"bridges\": [");
            sep = "\n";
            foreach ((uint id, int x0, int y0, int x1, int y1, int w) in bridges)
            {
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"id\": {0}, \"x0\": {1}, \"y0\": {2}, \"x1\": {3}, \"y1\": {4}, \"width\": {5} }}", id, x0, y0, x1, y1, w));
                sep = ",\n";
            }

            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }

        private static FormatException RejectsText(string file)
        {
            IRenderLayoutLoader loader = RenderFactory.CreateLayoutLoader();
            return Assert.Throws<FormatException>(() => loader.Load(new UTF8Encoding(false).GetBytes(file), SourceName, null));
        }

        /// <summary>15 §15.4: the exception type and the sourceName prefix, nothing else in the message.</summary>
        private static void AssertParseFailure(string file, string what)
        {
            FormatException ex = RejectsText(file);
            Assert.True(ex.Message.StartsWith(SourceName + ": ", StringComparison.Ordinal), what + ": " + ex.Message);
        }

        [Fact]
        public void test_render_layout_version_2_loads_scenery_and_rejects_faults()
        {
            IRenderLayoutLoader loader = RenderFactory.CreateLayoutLoader();
            AirsideLayout airside = Phase1Sim.AirsideLayout();

            // The fixture is version 2, and loads its areas and bridges in id order.
            byte[] fixture = Repo.Read("tests", "fixtures", "render", SourceName);
            Assert.Contains("\"schema_version\": 2", Encoding.UTF8.GetString(fixture), StringComparison.Ordinal);
            RenderLayout loaded = loader.Load(fixture, SourceName, airside);
            Assert.Equal(4, loaded.Areas.Count);
            Assert.Equal(4, loaded.Bridges.Count);
            Assert.Equal(Show(Phase1RenderLayout.Build()), Show(loaded));

            // Lists in reverse order come back ascending.
            var areas = new List<(uint, string, int, int, int, int)>
            {
                (40U, "control_tower", 920, 200, 940, 220),
                (7U, "pier", 280, -205, 780, -180),
                (5U, "terminal", -10, 190, 890, 275),
                (2U, "apron", 20, -180, 900, 190),
            };
            var bridges = new List<(uint, int, int, int, int, int)>
            {
                (9U, 770, -180, 761, -158, 3),
                (3U, 308, -180, 306, -160, 1),
            };
            RenderLayout v2 = loader.Load(new UTF8Encoding(false).GetBytes(V2(areas, bridges)), SourceName, airside);
            Assert.Equal(
                "A2:Apron(20,-180,900,190) A5:Terminal(-10,190,890,275) A7:Pier(280,-205,780,-180) A40:ControlTower(920,200,940,220) B3(308,-180,306,-160,1) B9(770,-180,761,-158,3)",
                Show(v2).Substring(Show(v2).IndexOf(" A2:", StringComparison.Ordinal) + 1));

            // Empty lists are valid.
            RenderLayout bare = loader.Load(new UTF8Encoding(false).GetBytes(V2(new List<(uint, string, int, int, int, int)>(), new List<(uint, int, int, int, int, int)>())), SourceName, null);
            Assert.Empty(bare.Areas);
            Assert.Empty(bare.Bridges);

            // Version 1 loads with empty lists, never null.
            RenderLayout v1 = loader.Load(Phase1Json(), SourceName, airside);
            Assert.NotNull(v1.Areas);
            Assert.NotNull(v1.Bridges);
            Assert.Empty(v1.Areas);
            Assert.Empty(v1.Bridges);

            // So does the kept seven-field constructor (07 L10, Q-130).
            RenderLayout kept = new RenderLayout(v1.TaxiNodes, v1.Runways, v1.FlowNodes, 40, 30, 2, 15);
            Assert.NotNull(kept.Areas);
            Assert.NotNull(kept.Bridges);
            Assert.Empty(kept.Areas);
            Assert.Empty(kept.Bridges);

            // Parse failures: other versions, an unknown kind, and keys that do
            // not belong to the version.
            var one = new List<(uint, string, int, int, int, int)> { (1U, "apron", 0, 0, 10, 10) };
            var none = new List<(uint, int, int, int, int, int)>();
            AssertParseFailure(V2(one, none, version: 3), "version 3");
            AssertParseFailure(V2(one, none, version: 0), "version 0");
            string hangar = V2(new List<(uint, string, int, int, int, int)> { (1U, "apron", 0, 0, 10, 10), (2U, "hangar", 0, 0, 10, 10) }, none);
            AssertParseFailure(hangar, "an unknown kind");
            string upper = V2(new List<(uint, string, int, int, int, int)> { (1U, "Apron", 0, 0, 10, 10) }, none);
            AssertParseFailure(upper, "a kind in the wrong case");
            string v1WithAreas = V2(one, none, version: 1);
            Assert.StartsWith(SourceName + ": ", RejectsText(v1WithAreas).Message, StringComparison.Ordinal);
            string v2WithoutBridges = V2(one, none).Replace(",\n  \"bridges\": [\n  ]", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("bridges", v2WithoutBridges, StringComparison.Ordinal);
            Assert.StartsWith(SourceName + ": ", RejectsText(v2WithoutBridges).Message, StringComparison.Ordinal);
            string kindAsInteger = V2(one, none).Replace("\"kind\": \"apron\"", "\"kind\": 0", StringComparison.Ordinal);
            AssertParseFailure(kindAsInteger, "a kind that is not a string");

            // Check 5, each naming the failing id; within one check, the lowest.
            (List<(uint, string, int, int, int, int)> Areas, List<(uint, int, int, int, int, int)> Bridges, string Id, string What)[] faults =
            {
                (new List<(uint, string, int, int, int, int)> { (6U, "apron", 0, 0, 10, 10), (6U, "pier", 0, 0, 20, 20) }, none, "6", "duplicate area id"),
                (new List<(uint, string, int, int, int, int)> { (9U, "apron", 10, 0, 10, 10), (4U, "terminal", 11, 0, 10, 10) }, none, "4", "MinX >= MaxX"),
                (new List<(uint, string, int, int, int, int)> { (8U, "apron", 0, 0, 10, 10), (12U, "pier", 0, 5, 10, 5) }, none, "12", "MinY = MaxY"),
                (new List<(uint, string, int, int, int, int)> { (3U, "control_tower", 0, 0, 20, 21) }, none, "3", "a control tower that is not square"),
                (new List<(uint, string, int, int, int, int)> { (13U, "terminal", 0, 0, 20, 21) }, new List<(uint, int, int, int, int, int)> { (5U, 0, 0, 10, 0, 3), (5U, 0, 0, 0, 10, 3) }, "5", "duplicate bridge id"),
                (one, new List<(uint, int, int, int, int, int)> { (31U, 0, 0, 10, 0, 0), (17U, 0, 0, 10, 0, -2) }, "17", "bridge width <= 0"),
                (one, new List<(uint, int, int, int, int, int)> { (2U, 0, 0, 10, 0, 3), (11U, 5, -7, 5, -7, 3) }, "11", "a bridge from a point to itself"),
            };
            foreach ((List<(uint, string, int, int, int, int)> a, List<(uint, int, int, int, int, int)> b, string id, string what) in faults)
            {
                foreach (AirsideLayout? withAirside in new[] { (AirsideLayout?)null, airside })
                {
                    FormatException ex = Assert.Throws<FormatException>(() => loader.Load(new UTF8Encoding(false).GetBytes(V2(a, b)), SourceName, withAirside));
                    Assert.True(ex.Message.StartsWith(SourceName + ": ", StringComparison.Ordinal), what + ": " + ex.Message);
                    Assert.True(Regex.IsMatch(ex.Message, "(?<![0-9])" + id + "(?![0-9])"), what + ": message does not name id " + id + ": " + ex.Message);
                }
            }

            // Check 1 (sizes) comes before check 5.
            FormatException first = RejectsText(V2(new List<(uint, string, int, int, int, int)> { (77U, "control_tower", 0, 0, 20, 21) }, none, standSize: 0));
            Assert.Contains("stand_size", first.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_render_layout_rejects_missing_taxi_node_position()
        {
            AirsideLayout airside = Phase1Sim.AirsideLayout();

            // Nodes 13 and 3 have no position: the lowest, 3, is named.
            AssertNames(Rejects(Phase1Json(skipTaxi: new[] { 13, 3 }), airside), "3");
            AssertNames(Rejects(Phase1Json(skipTaxi: new[] { 14 }), airside), "14");

            // Without an airside layout, check 4 is skipped and the same file loads.
            RenderLayout loaded = RenderFactory.CreateLayoutLoader().Load(Phase1Json(skipTaxi: new[] { 14 }), SourceName, null);
            Assert.Equal(6, loaded.TaxiNodes.Count);
        }

        [Fact]
        public void test_render_layout_rejects_unknown_runway_geometry()
        {
            AirsideLayout airside = Phase1Sim.AirsideLayout();

            // Geometry for runways 4242 and 77, which the airside layout lacks: the lowest, 77, is named.
            AssertNames(Rejects(Phase1Json(extraRunways: new[] { 4242, 77 }), airside), "77");
            AssertNames(Rejects(Phase1Json(extraRunways: new[] { 4242 }), airside), "4242");

            // Without an airside layout, the extra geometry is not checked against anything.
            RenderLayout loaded = RenderFactory.CreateLayoutLoader().Load(Phase1Json(extraRunways: new[] { 4242 }), SourceName, null);
            Assert.Equal(2, loaded.Runways.Count);
        }

        [Fact]
        public void test_render_layout_fixture_file_equals_built_layout()
        {
            // The §15.12 fixture, loaded against the airside fixture, is the
            // layout the integration tests build in code.
            byte[] file = Repo.Read("tests", "fixtures", "render", SourceName);
            RenderLayout loaded = RenderFactory.CreateLayoutLoader().Load(file, SourceName, Phase1Sim.AirsideLayout());
            Assert.Equal(Show(Phase1RenderLayout.Build()), Show(loaded));
        }
    }
}
