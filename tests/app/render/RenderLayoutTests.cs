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
            return sb.ToString();
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
