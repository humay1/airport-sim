using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
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
                if (b.Stand.HasValue)
                {
                    sb.Append(string.Format(CultureInfo.InvariantCulture, "@S{0}", b.Stand.Value.Value));
                }
            }

            // Q-132: a null Walkways would throw here, as it should.
            foreach (LayoutWalkway w in l.Walkways)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, " W{0}({1},{2},{3},{4},{5})", w.Node.Value, w.X0, w.Y0, w.X1, w.Y1, w.Width));
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

        private static string KindText(AreaKind kind)
        {
            switch (kind)
            {
                case AreaKind.Apron: return "apron";
                case AreaKind.Terminal: return "terminal";
                case AreaKind.Pier: return "pier";
                default: return "control_tower";
            }
        }

        private static List<(uint, string, int, int, int, int)> Phase1Areas()
        {
            var rows = new List<(uint, string, int, int, int, int)>();
            foreach (LayoutArea a in Phase1RenderLayout.Areas())
            {
                rows.Add((a.Id, KindText(a.Kind), a.MinX, a.MinY, a.MaxX, a.MaxY));
            }

            return rows;
        }

        private static List<(uint, int, int, int, int, int, int?)> Phase1Bridges()
        {
            var rows = new List<(uint, int, int, int, int, int, int?)>();
            foreach (LayoutBridge b in Phase1RenderLayout.Bridges())
            {
                rows.Add((b.Id, b.X0, b.Y0, b.X1, b.Y1, b.Width, b.Stand.HasValue ? b.Stand.Value.Value : (int?)null));
            }

            return rows;
        }

        private static List<(uint, int, int, int, int, int)> Phase1Walkways()
        {
            var rows = new List<(uint, int, int, int, int, int)>();
            foreach (LayoutWalkway w in Phase1RenderLayout.Walkways())
            {
                rows.Add((w.Node.Value, w.X0, w.Y0, w.X1, w.Y1, w.Width));
            }

            return rows;
        }

        /// <summary>
        /// A version 3 file (15 §15.21, Q-132): V2's text with the Phase 1
        /// areas (unless given), then these bridges, each with "stand" when
        /// its stand is not null, then, unless withWalkways is false, these
        /// walkways. One object per line.
        /// </summary>
        private static string V3(
            IEnumerable<(uint Id, int X0, int Y0, int X1, int Y1, int Width, int? Stand)> bridges,
            IEnumerable<(uint Node, int X0, int Y0, int X1, int Y1, int Width)> walkways,
            int version = 3,
            bool withWalkways = true,
            IEnumerable<(uint Id, string Kind, int MinX, int MinY, int MaxX, int MaxY)>? areas = null,
            int standSize = 40)
        {
            string v2 = V2(areas ?? Phase1Areas(), new List<(uint, int, int, int, int, int)>(), version, standSize);
            const string Tail = "\n  \"bridges\": [\n  ]\n}\n";
            Assert.EndsWith(Tail, v2, StringComparison.Ordinal);
            var sb = new StringBuilder(v2.Substring(0, v2.Length - Tail.Length));
            sb.Append("\n  \"bridges\": [");
            string sep = "\n";
            foreach ((uint id, int x0, int y0, int x1, int y1, int w, int? stand) in bridges)
            {
                string standKey = stand.HasValue ? string.Format(CultureInfo.InvariantCulture, ", \"stand\": {0}", stand.Value) : string.Empty;
                sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"id\": {0}, \"x0\": {1}, \"y0\": {2}, \"x1\": {3}, \"y1\": {4}, \"width\": {5}{6} }}", id, x0, y0, x1, y1, w, standKey));
                sep = ",\n";
            }

            sb.Append("\n  ]");
            if (withWalkways)
            {
                sb.Append(",\n  \"walkways\": [");
                sep = "\n";
                foreach ((uint node, int x0, int y0, int x1, int y1, int w) in walkways)
                {
                    sb.Append(sep).Append(string.Format(CultureInfo.InvariantCulture, "    {{ \"node\": {0}, \"x0\": {1}, \"y0\": {2}, \"x1\": {3}, \"y1\": {4}, \"width\": {5} }}", node, x0, y0, x1, y1, w));
                    sep = ",\n";
                }

                sb.Append("\n  ]");
            }

            sb.Append("\n}\n");
            return sb.ToString();
        }

        /// <summary>The 1-based line of the first occurrence of the marker.</summary>
        private static int LineOf(string text, string marker)
        {
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(at >= 0, "the test text has no " + marker);
            int line = 1;
            for (int i = 0; i < at; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
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

            // The fixture is version 3 (Q-132, 15 §15.23), and loads its areas and bridges in id order.
            byte[] fixture = Repo.Read("tests", "fixtures", "render", SourceName);
            Assert.Contains("\"schema_version\": 3", Encoding.UTF8.GetString(fixture), StringComparison.Ordinal);
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

        [Fact]
        public void test_render_layout_version_3_loads_walkways_and_bridge_stands()
        {
            IRenderLayoutLoader loader = RenderFactory.CreateLayoutLoader();
            AirsideLayout airside = Phase1Sim.AirsideLayout();
            byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

            // The fixture, with and without the airside layout: walkways on
            // corridors 4 and 7, bridge k serving stand k (15 §15.23).
            byte[] fixture = Repo.Read("tests", "fixtures", "render", SourceName);
            foreach (AirsideLayout? withAirside in new[] { (AirsideLayout?)airside, null })
            {
                RenderLayout l = loader.Load(fixture, SourceName, withAirside);
                Assert.Equal(new[] { 4U, 7U }, l.Walkways.Select(w => w.Node.Value).ToArray());
                Assert.Equal(" W4(300,230,380,230,40) W7(600,230,680,230,40)", Show(l).Substring(Show(l).IndexOf(" W4", StringComparison.Ordinal)));
                for (int i = 0; i < 4; i++)
                {
                    Assert.Equal((uint)(i + 1), l.Bridges[i].Id);
                    Assert.True(l.Bridges[i].Stand.HasValue && l.Bridges[i].Stand!.Value.Value == i + 1, "bridge " + (i + 1) + " stand " + l.Bridges[i].Stand);
                }

                Assert.Equal(Show(Phase1RenderLayout.Build()), Show(l));
            }

            // Lists in reverse order come back ascending: walkways by NodeId, bridges by id.
            var reversedBridges = Phase1Bridges();
            reversedBridges.Reverse();
            var reversedWalkways = Phase1Walkways();
            reversedWalkways.Reverse();
            RenderLayout reversed = loader.Load(Bytes(V3(reversedBridges, reversedWalkways)), SourceName, airside);
            Assert.Equal(Show(Phase1RenderLayout.Build()), Show(reversed));

            // Empty lists are valid; a version 3 bridge always names a stand.
            RenderLayout bare = loader.Load(Bytes(V3(new List<(uint, int, int, int, int, int, int?)>(), new List<(uint, int, int, int, int, int)>())), SourceName, airside);
            Assert.Empty(bare.Bridges);
            Assert.NotNull(bare.Walkways);
            Assert.Empty(bare.Walkways);

            // Version 1 and 2 files load with empty walkways and null stands.
            RenderLayout v1 = loader.Load(Phase1Json(), SourceName, airside);
            Assert.NotNull(v1.Walkways);
            Assert.Empty(v1.Walkways);
            var v2Bridges = new List<(uint, int, int, int, int, int)> { (2U, 452, -180, 446, -163, 3), (1U, 308, -180, 306, -160, 3) };
            RenderLayout v2 = loader.Load(Bytes(V2(Phase1Areas(), v2Bridges)), SourceName, airside);
            Assert.NotNull(v2.Walkways);
            Assert.Empty(v2.Walkways);
            Assert.Equal(2, v2.Bridges.Count);
            foreach (LayoutBridge b in v2.Bridges)
            {
                Assert.False(b.Stand.HasValue, "a version 2 bridge has stand " + b.Stand);
            }

            // The kept constructors (07 L10, 15 §15.21): LayoutBridge's six fields
            // set Stand to null; RenderLayout's seven and nine set Walkways to empty.
            Assert.False(new LayoutBridge(5U, 0, 0, 1, 1, 2).Stand.HasValue);
            var full = new LayoutBridge(5U, 0, 0, 1, 1, 2, new StandId(9));
            Assert.Equal((ushort)9, full.Stand!.Value.Value);
            var walkway = new LayoutWalkway(new NodeId(7), 1, 2, 3, 4, 5);
            Assert.Equal("7 1 2 3 4 5", string.Join(" ", walkway.Node.Value, walkway.X0, walkway.Y0, walkway.X1, walkway.Y1, walkway.Width));
            var seven = new RenderLayout(v1.TaxiNodes, v1.Runways, v1.FlowNodes, 40, 30, 2, 15);
            var nine = new RenderLayout(v1.TaxiNodes, v1.Runways, v1.FlowNodes, 40, 30, 2, 15, Phase1RenderLayout.Areas(), Phase1RenderLayout.Bridges());
            var ten = new RenderLayout(v1.TaxiNodes, v1.Runways, v1.FlowNodes, 40, 30, 2, 15, Phase1RenderLayout.Areas(), Phase1RenderLayout.Bridges(), Phase1RenderLayout.Walkways());
            Assert.NotNull(seven.Walkways);
            Assert.Empty(seven.Walkways);
            Assert.NotNull(nine.Walkways);
            Assert.Empty(nine.Walkways);
            Assert.Equal(2, ten.Walkways.Count);
            Assert.Equal(4, ten.Bridges.Count);

            // Parse failures (15 §15.4, §15.21): keys that do not belong to the
            // version, a missing key, and a value outside its C# type.
            var bridges = Phase1Bridges();
            var walkways = Phase1Walkways();
            var noStands = new List<(uint, int, int, int, int, int, int?)> { (1U, 308, -180, 306, -160, 3, null) };
            AssertParseFailure(V3(bridges, walkways, withWalkways: false), "version 3 without walkways");
            AssertParseFailure(V3(noStands, walkways), "a version 3 bridge without a stand");
            AssertParseFailure(V3(noStands, walkways, version: 2), "version 2 with walkways");
            AssertParseFailure(V3(bridges, walkways, version: 2, withWalkways: false), "a version 2 bridge with a stand");
            AssertParseFailure(V3(noStands, walkways, version: 1), "version 1 with walkways");
            AssertParseFailure(V3(bridges, walkways, version: 4), "version 4");
            var oneWalkway = new List<(uint, int, int, int, int, int)> { (4U, 300, 230, 380, 230, 41) };
            AssertParseFailure(V3(bridges, oneWalkway).Replace("\"width\": 41 }", "\"width\": 41, \"lane\": 1 }", StringComparison.Ordinal), "an unknown walkway key");
            AssertParseFailure(V3(bridges, oneWalkway).Replace(", \"width\": 41 }", " }", StringComparison.Ordinal), "a walkway without width");
            AssertParseFailure(V3(bridges, oneWalkway).Replace("\"node\": 4, \"x0\"", "\"node\": 4, \"node\": 4, \"x0\"", StringComparison.Ordinal), "a duplicate walkway key");
            AssertParseFailure(V3(bridges, walkways).Replace("\"stand\": 2", "\"stand\": \"2\"", StringComparison.Ordinal), "a stand that is not an integer");
            string valid = V3(bridges, walkways);
            foreach ((string from, string marker, string what) in new[]
            {
                ("\"stand\": 3 }", "\"stand\": 70000", "a stand above uint16"),
                ("\"stand\": 3 }", "\"stand\": -1", "a negative stand"),
                ("{ \"node\": 7, \"x0\": 600", "\"node\": 4294967296", "a walkway node above uint32"),
                ("\"x1\": 680, \"y1\": 230, \"width\": 40 }", "\"width\": 2147483648", "a walkway width above int32"),
            })
            {
                Assert.True(valid.IndexOf(from, StringComparison.Ordinal) >= 0 && valid.IndexOf(from, StringComparison.Ordinal) == valid.LastIndexOf(from, StringComparison.Ordinal), "the test text holds exactly one " + from);
                string to = from.StartsWith("\"stand\"", StringComparison.Ordinal) ? marker + " }"
                    : from.StartsWith("{ \"node\"", StringComparison.Ordinal) ? "{ " + marker + ", \"x0\": 600"
                    : "\"x1\": 680, \"y1\": 230, " + marker + " }";
                string text = valid.Replace(from, to, StringComparison.Ordinal);
                FormatException ex = RejectsText(text);
                Assert.True(ex.Message.StartsWith(SourceName + ": ", StringComparison.Ordinal), what + ": " + ex.Message);
                string line = "line " + LineOf(text, marker).ToString(CultureInfo.InvariantCulture);
                Assert.True(Regex.IsMatch(ex.Message, line + "(?![0-9])"), what + ": message does not contain " + line + ": " + ex.Message);
            }

            // Check 4's last step, only with airside: every bridge's stand is a
            // StandDef (stands 1..4 here); the lowest bridge id that is not is named.
            var badStands = new List<(uint, int, int, int, int, int, int?)> { (9U, 770, -180, 761, -158, 3, 77), (3U, 608, -180, 606, -160, 3, 88), (5U, 308, -180, 306, -160, 3, 1) };
            AssertNames(Rejects(Bytes(V3(badStands, walkways)), airside), "3");
            Assert.Equal(3, loader.Load(Bytes(V3(badStands, walkways)), SourceName, null).Bridges.Count);

            // Check 4's earlier steps come first: a missing taxi position (node 13) is named.
            string noNode13 = V3(badStands, walkways).Replace("    { \"node\": 13, \"x\": 600, \"y\": -150 },\n", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("\"node\": 13,", noNode13, StringComparison.Ordinal);
            AssertNames(Rejects(Bytes(noNode13), airside), "13");

            // Check 4 comes before check 5 (a duplicate area id 6).
            var dupAreas = new List<(uint, string, int, int, int, int)> { (6U, "apron", 0, 0, 10, 10), (6U, "pier", 0, 0, 20, 20) };
            AssertNames(Rejects(Bytes(V3(badStands, walkways, areas: dupAreas)), airside), "3");
            AssertNames(Rejects(Bytes(V3(badStands, walkways, areas: dupAreas)), null), "6");

            // Check 5's last step, always: no two bridges name the same stand; the
            // higher bridge id of the lowest pair is named. Earlier steps of check 5 come first.
            (List<(uint, int, int, int, int, int, int?)> Rows, string Id, string What)[] sharing =
            {
                (new List<(uint, int, int, int, int, int, int?)> { (9U, 0, 0, 10, 0, 3, 2), (4U, 0, 0, 0, 10, 3, 2), (1U, 0, 0, 5, 5, 3, 1) }, "9", "bridges 4 and 9 share stand 2"),
                (new List<(uint, int, int, int, int, int, int?)> { (8U, 0, 0, 10, 0, 3, 2), (3U, 0, 0, 10, 0, 3, 2), (5U, 0, 0, 0, 10, 3, 1), (2U, 0, 0, 0, 10, 3, 1) }, "5", "pairs (2, 5) and (3, 8)"),
                (new List<(uint, int, int, int, int, int, int?)> { (6U, 0, 0, 10, 0, 3, 3), (1U, 0, 0, 0, 10, 3, 3), (4U, 0, 0, 5, 5, 3, 3) }, "4", "bridges 1, 4 and 6 share stand 3"),
                (new List<(uint, int, int, int, int, int, int?)> { (2U, 0, 0, 10, 0, 3, 1), (5U, 0, 0, 0, 10, 3, 1), (11U, 0, 0, 0, 10, 0, 4) }, "11", "a bridge of width 0 comes before a shared stand"),
            };
            foreach ((List<(uint, int, int, int, int, int, int?)> rows, string id, string what) in sharing)
            {
                foreach (AirsideLayout? withAirside in new[] { (AirsideLayout?)null, airside })
                {
                    FormatException ex = Assert.Throws<FormatException>(() => loader.Load(Bytes(V3(rows, walkways)), SourceName, withAirside));
                    Assert.True(ex.Message.StartsWith(SourceName + ": ", StringComparison.Ordinal), what + ": " + ex.Message);
                    Assert.True(Regex.IsMatch(ex.Message, "(?<![0-9])" + id + "(?![0-9])"), what + ": message does not name id " + id + ": " + ex.Message);
                }
            }

            // Check 6, always: each fault names the walkway's node, the lowest
            // failing one within the check. Ends on the box's edges are inside.
            var edges = new List<(uint, int, int, int, int, int)> { (7U, 600, 200, 680, 260, 40), (4U, 380, 260, 300, 200, 1) };
            Assert.Equal(2, loader.Load(Bytes(V3(bridges, edges)), SourceName, airside).Walkways.Count);
            (List<(uint, int, int, int, int, int)> Rows, string Id, string What)[] walkwayFaults =
            {
                (new List<(uint, int, int, int, int, int)> { (7U, 600, 230, 680, 230, 40), (7U, 610, 230, 670, 230, 10) }, "7", "a duplicate walkway node"),
                (new List<(uint, int, int, int, int, int)> { (4U, 300, 230, 380, 230, 40), (42U, 300, 230, 380, 230, 40) }, "42", "a walkway whose node has no FlowNodeBox"),
                (new List<(uint, int, int, int, int, int)> { (7U, 600, 230, 680, 230, 0) }, "7", "a walkway of width 0"),
                (new List<(uint, int, int, int, int, int)> { (4U, 300, 230, 380, 230, -3) }, "4", "a walkway of negative width"),
                (new List<(uint, int, int, int, int, int)> { (7U, 640, 230, 640, 230, 40) }, "7", "a walkway from a point to itself"),
                (new List<(uint, int, int, int, int, int)> { (7U, 600, 230, 681, 230, 40) }, "7", "a walkway end right of its box"),
                (new List<(uint, int, int, int, int, int)> { (4U, 340, 199, 340, 260, 40) }, "4", "a walkway end below its box"),
                (new List<(uint, int, int, int, int, int)> { (7U, 600, 230, 680, 230, 0), (4U, 299, 230, 380, 230, 40) }, "4", "the lowest failing walkway node"),
            };
            foreach ((List<(uint, int, int, int, int, int)> rows, string id, string what) in walkwayFaults)
            {
                foreach (AirsideLayout? withAirside in new[] { (AirsideLayout?)null, airside })
                {
                    FormatException ex = Assert.Throws<FormatException>(() => loader.Load(Bytes(V3(bridges, rows)), SourceName, withAirside));
                    Assert.True(ex.Message.StartsWith(SourceName + ": ", StringComparison.Ordinal), what + ": " + ex.Message);
                    Assert.True(Regex.IsMatch(ex.Message, "(?<![0-9])" + id + "(?![0-9])"), what + ": message does not name node " + id + ": " + ex.Message);
                }
            }

            // Check 6 comes after checks 1, 4 and 5.
            var orphan = new List<(uint, int, int, int, int, int)> { (42U, 300, 230, 380, 230, 40) };
            var narrow = new List<(uint, int, int, int, int, int, int?)> { (13U, 0, 0, 10, 0, 0, 1) };
            AssertNames(Rejects(Bytes(V3(narrow, orphan)), null), "13");
            AssertNames(Rejects(Bytes(V3(badStands, orphan)), airside), "3");
            Assert.Contains("stand_size", RejectsText(V3(bridges, orphan, standSize: 0)).Message, StringComparison.Ordinal);
            AssertNames(Rejects(Bytes(V3(bridges, orphan)), null), "42");
        }

        [Fact]
        public void test_render_playtest_layout_extends_the_phase1_layout()
        {
            const string PlaytestName = "playtest-layout.json";

            // 12 §12.13's fixture with 19 §19.2c's exit lines, added in code and
            // parsed by sim.airside's loader: runway 1 leaves at node 4.
            AirsideLayout airside = PlaytestAirside.Layout();
            Assert.Equal(4, airside.Runways[0].ExitNode.Value);
            Assert.Equal(1, airside.Runways[0].ThresholdNode.Value);
            Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6, 11, 12, 13, 14 }, airside.Nodes.Select(n => n.Id.Value).ToArray());

            // It loads against that layout, and is phase1-layout.json plus the
            // three taxi-node positions (15 §15.23).
            byte[] playtest = Repo.Read("tests", "fixtures", "render", PlaytestName);
            IRenderLayoutLoader loader = RenderFactory.CreateLayoutLoader();
            RenderLayout loaded = loader.Load(playtest, PlaytestName, airside);
            Assert.Equal(Show(Phase1RenderLayout.Playtest()), Show(loaded));
            Assert.Equal(10, loaded.TaxiNodes.Count);
            Assert.Equal(" T4(-1850,0) T5(-1850,-90) T6(0,-90) ", Show(loaded).Substring(Show(loaded).IndexOf(" T4(", StringComparison.Ordinal), " T4(-1850,0) T5(-1850,-90) T6(0,-90) ".Length));

            // And no other change: its text without those three entries is the fixture's.
            string playtestText = Encoding.UTF8.GetString(playtest);
            string phase1Text = Encoding.UTF8.GetString(Repo.Read("tests", "fixtures", "render", SourceName));
            string[] added =
            {
                "    { \"node\": 4, \"x\": -1850, \"y\": 0 },\n",
                "    { \"node\": 5, \"x\": -1850, \"y\": -90 },\n",
                "    { \"node\": 6, \"x\": 0, \"y\": -90 },\n",
            };
            string stripped = playtestText;
            foreach (string line in added)
            {
                Assert.Contains(line, stripped, StringComparison.Ordinal);
                stripped = stripped.Replace(line, string.Empty, StringComparison.Ordinal);
            }

            Assert.Equal(phase1Text, stripped);

            // Neither file loads against the other's airside layout (15 §15.4 check 4).
            AssertNames(Assert.Throws<FormatException>(() => loader.Load(Repo.Read("tests", "fixtures", "render", SourceName), SourceName, airside)), "4");
            FormatException wrong = Assert.Throws<FormatException>(() => loader.Load(playtest, PlaytestName, Phase1Sim.AirsideLayout()));
            Assert.StartsWith(PlaytestName + ": ", wrong.Message, StringComparison.Ordinal);
            Assert.True(Regex.IsMatch(wrong.Message, "(?<![0-9])4(?![0-9])"), "message does not name taxi node 4: " + wrong.Message);
        }
    }
}
