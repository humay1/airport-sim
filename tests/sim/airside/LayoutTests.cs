using System;
using System.Globalization;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.4 load-time validation, each a hard failure naming the offending
    /// id. Load takes no source name, so by 07 "Error handling" (Q-030,
    /// Q-031) the FormatException's message starts with the module name.
    /// Ids in the broken cases are chosen to appear nowhere else in the
    /// layout, so "names the id" is a real check.
    /// </summary>
    public sealed class LayoutTests
    {
        private static void AssertRejects(LayoutBuilder b, ushort offendingId)
        {
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            AirsideLayout raw = b.Raw();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Load(raw));
            Assert.StartsWith("sim.airside: ", ex.Message, StringComparison.Ordinal);
            string id = offendingId.ToString(CultureInfo.InvariantCulture);
            Assert.True(
                Regex.IsMatch(ex.Message, @"(?<![0-9])" + id + @"(?![0-9])"),
                "expected the message to name id " + id + ": " + ex.Message);
        }

        /// <summary>The fixture layout with a fifth stand, id 44 on node 44, that no edge reaches.</summary>
        private static LayoutBuilder WithIsolatedStand()
        {
            return FixtureLayout.Builder()
                .Node(44, TaxiNodeKind.StandPosition)
                .Stand(44, 44, AirsideContent.Super, 944);
        }

        [Fact]
        public void test_layout_rejects_disconnected_graph()
        {
            AssertRejects(WithIsolatedStand(), 44);
        }

        [Fact]
        public void test_layout_rejects_stand_that_cannot_reach_the_threshold_back()
        {
            // 12 §12.4: every StandPosition and RunwayThreshold reachable from
            // every other. A one-way edge into stand 45 leaves no way back out.
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(45, TaxiNodeKind.StandPosition)
                .Edge(45, FixtureLayout.J1, 45, 10, bidirectional: false)
                .Stand(45, 45, AirsideContent.Super, 945);
            AssertRejects(b, 45);
        }

        [Fact]
        public void test_layout_rejects_edge_to_undeclared_node()
        {
            LayoutBuilder b = FixtureLayout.Builder().Edge(46, FixtureLayout.J2, 77, 10);
            AssertRejects(b, 77);
        }

        [Fact]
        public void test_layout_rejects_edge_from_undeclared_node()
        {
            LayoutBuilder b = FixtureLayout.Builder().Edge(46, 76, FixtureLayout.J2, 10);
            AssertRejects(b, 76);
        }

        [Fact]
        public void test_layout_rejects_stand_on_undeclared_node()
        {
            LayoutBuilder b = FixtureLayout.Builder().Stand(47, 78, AirsideContent.Super, 947);
            AssertRejects(b, 78);
        }

        [Fact]
        public void test_layout_rejects_runway_with_undeclared_threshold()
        {
            LayoutBuilder b = FixtureLayout.Builder().Runway(48, 79, 15, 10);
            AssertRejects(b, 79);
        }

        [Fact]
        public void test_layout_rejects_duplicate_stand_id()
        {
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(50, TaxiNodeKind.StandPosition)
                .Node(51, TaxiNodeKind.StandPosition)
                .Edge(50, FixtureLayout.J2, 50, 10)
                .Edge(51, FixtureLayout.J2, 51, 10)
                .Stand(61, 50, AirsideContent.Super, 950)
                .Stand(61, 51, AirsideContent.Super, 951);
            AssertRejects(b, 61);
        }

        [Fact]
        public void test_layout_rejects_duplicate_node_id()
        {
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(62, TaxiNodeKind.Junction)
                .Node(62, TaxiNodeKind.Junction)
                .Edge(52, FixtureLayout.J2, 62, 10);
            AssertRejects(b, 62);
        }

        [Fact]
        public void test_layout_rejects_duplicate_edge_id()
        {
            LayoutBuilder b = FixtureLayout.Builder()
                .Edge(63, FixtureLayout.J1, FixtureLayout.J2, 40)
                .Edge(63, FixtureLayout.Threshold, FixtureLayout.J2, 50);
            AssertRejects(b, 63);
        }

        [Fact]
        public void test_layout_rejects_duplicate_runway_id()
        {
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(53, TaxiNodeKind.RunwayThreshold)
                .Edge(53, 53, FixtureLayout.J1, 30)
                .Runway(64, FixtureLayout.Threshold, 15, 10)
                .Runway(64, 53, 15, 10);
            AssertRejects(b, 64);
        }

        [Fact]
        public void test_layout_load_accepts_valid_layout_and_keeps_its_content()
        {
            AirsideLayout raw = FixtureLayout.Builder().Raw();
            AirsideLayout loaded = AirsideFactory.CreateLayoutLoader().Load(raw);

            Assert.Equal(raw.Runways.Count, loaded.Runways.Count);
            RunwayDef r = loaded.Runways[0];
            Assert.Equal(FixtureLayout.Runway, r.Id.Value);
            Assert.Equal(FixtureLayout.Threshold, r.ThresholdNode.Value);
            Assert.Equal(270, r.ActiveDirectionDeg);
            Assert.Equal(FixtureLayout.CapacityPerHour, r.DeclaredCapacityPerHour);
            Assert.Equal(FixtureLayout.OccupancyTicks, r.OccupancyTicks);

            Assert.Equal(Sorted(raw.Nodes, n => n.Id.Value + "/" + n.Kind), Sorted(loaded.Nodes, n => n.Id.Value + "/" + n.Kind));
            Assert.Equal(
                Sorted(raw.Edges, e => e.Id.Value + "/" + e.From.Value + "/" + e.To.Value + "/" + e.TraversalTicks + "/" + e.Bidirectional),
                Sorted(loaded.Edges, e => e.Id.Value + "/" + e.From.Value + "/" + e.To.Value + "/" + e.TraversalTicks + "/" + e.Bidirectional));
            Assert.Equal(
                Sorted(raw.Stands, s => s.Id.Value + "/" + s.Node.Value + "/" + s.MaxAircraftSizeCategory.Value + "/" + s.DepartureSinkNode.Value),
                Sorted(loaded.Stands, s => s.Id.Value + "/" + s.Node.Value + "/" + s.MaxAircraftSizeCategory.Value + "/" + s.DepartureSinkNode.Value));
        }

        private static string[] Sorted<T>(System.Collections.Generic.IReadOnlyList<T> items, Func<T, string> key)
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
