using System;
using System.Collections.Generic;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// The §12.13 fixture layout with the playtest's exit (12 §12.13 "The exit
    /// node in the fixtures", Q-132), built in code: the fixture file does not
    /// change. Runway 1 keeps threshold 1 and gains exit junction 4; one-way
    /// edges 7 (4 to 5), 8 (5 to 6, 40 ticks) and 9 (6 to J1, 10 ticks) carry
    /// arrivals back to J1. Departures cannot use them, so every departure
    /// route is the fixture's.
    ///
    ///   T(1) --E1 30-- J1(2) --...the fixture's stands...
    ///                   ^
    ///                  E9 10 (one-way)
    ///                   |
    ///   X(4) --E7--&gt; (5) --E8 40--&gt; (6)
    /// </summary>
    internal static class ExitLayout
    {
        public const ushort Exit = 4;
        public const ushort P5 = 5;
        public const ushort P6 = 6;
        public const ushort E7 = 7;
        public const ushort E8 = 8;
        public const ushort E9 = 9;

        public static LayoutBuilder Builder(uint e7Ticks = 5U)
        {
            LayoutBuilder b = FixtureLayout.Builder();
            b.Runways.Clear();
            b.RunwayWithExit(FixtureLayout.Runway, FixtureLayout.Threshold, Exit, FixtureLayout.CapacityPerHour, FixtureLayout.OccupancyTicks)
             .Node(Exit, TaxiNodeKind.Junction)
             .Node(P5, TaxiNodeKind.Junction)
             .Node(P6, TaxiNodeKind.Junction)
             .Edge(E7, Exit, P5, e7Ticks, bidirectional: false)
             .Edge(E8, P5, P6, 40, bidirectional: false)
             .Edge(E9, P6, FixtureLayout.J1, 10, bidirectional: false);
            return b;
        }

        public static AirsideLayout Layout(uint e7Ticks = 5U)
        {
            return Builder(e7Ticks).Load();
        }

        /// <summary>RouteTicks(exit, stand) with the default edge 7: 55 ticks to J1, then the fixture's J1-to-stand part.</summary>
        public static ulong RouteTicks(ushort stand)
        {
            return 5UL + 40UL + 10UL + (FixtureLayout.RouteTicks(stand) - 30UL);
        }

        /// <summary>The same layout in 12 §12.4's file format, one object per line; the runway object is line 4.</summary>
        public static List<string> FileLines()
        {
            return new List<string>
            {
                "{",
                "  \"schema_version\": 1,",
                "  \"runways\": [",
                "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 4 }",
                "  ],",
                "  \"nodes\": [",
                "    { \"id\": 1, \"kind\": \"runway_threshold\" },",
                "    { \"id\": 2, \"kind\": \"junction\" },",
                "    { \"id\": 3, \"kind\": \"junction\" },",
                "    { \"id\": 4, \"kind\": \"junction\" },",
                "    { \"id\": 5, \"kind\": \"junction\" },",
                "    { \"id\": 6, \"kind\": \"junction\" },",
                "    { \"id\": 11, \"kind\": \"stand_position\" },",
                "    { \"id\": 12, \"kind\": \"stand_position\" },",
                "    { \"id\": 13, \"kind\": \"stand_position\" },",
                "    { \"id\": 14, \"kind\": \"stand_position\" }",
                "  ],",
                "  \"edges\": [",
                "    { \"id\": 1, \"from\": 1, \"to\": 2, \"traversal_ticks\": 30, \"bidirectional\": true },",
                "    { \"id\": 2, \"from\": 2, \"to\": 11, \"traversal_ticks\": 20, \"bidirectional\": true },",
                "    { \"id\": 3, \"from\": 2, \"to\": 3, \"traversal_ticks\": 15, \"bidirectional\": true },",
                "    { \"id\": 4, \"from\": 3, \"to\": 12, \"traversal_ticks\": 10, \"bidirectional\": true },",
                "    { \"id\": 5, \"from\": 3, \"to\": 13, \"traversal_ticks\": 15, \"bidirectional\": true },",
                "    { \"id\": 6, \"from\": 3, \"to\": 14, \"traversal_ticks\": 20, \"bidirectional\": true },",
                "    { \"id\": 7, \"from\": 4, \"to\": 5, \"traversal_ticks\": 5, \"bidirectional\": false },",
                "    { \"id\": 8, \"from\": 5, \"to\": 6, \"traversal_ticks\": 40, \"bidirectional\": false },",
                "    { \"id\": 9, \"from\": 6, \"to\": 2, \"traversal_ticks\": 10, \"bidirectional\": false }",
                "  ],",
                "  \"stands\": [",
                "    { \"id\": 1, \"node\": 11, \"max_aircraft_size_category\": \"medium\", \"departure_sink_node\": 9 },",
                "    { \"id\": 2, \"node\": 12, \"max_aircraft_size_category\": \"super\", \"departure_sink_node\": 9 },",
                "    { \"id\": 3, \"node\": 13, \"max_aircraft_size_category\": \"heavy\", \"departure_sink_node\": 9 },",
                "    { \"id\": 4, \"node\": 14, \"max_aircraft_size_category\": \"super\", \"departure_sink_node\": 9 }",
                "  ]",
                "}",
            };
        }

        public const int RunwayLine = 4;
        public const int Node4Line = 10;
        public const int Edge7Line = 25;
        public const int Stand1Line = 30;
    }

    /// <summary>
    /// 12 §12.4 "The exit node" (Q-132): RunwayDef.ExitNode, the kept
    /// five-field constructor (07 L10), the optional exit_node key of the
    /// file format, and load-time checks 4, 5 and 7. Per §12.4, a failure
    /// is asserted by type, prefix, field name and ids only.
    /// </summary>
    public sealed class ExitNodeLayoutTests
    {
        private static readonly string[] RunwayKeys =
        {
            "\"id\": 1, ",
            "\"threshold_node\": 1, ",
            "\"active_direction_deg\": 270, ",
            "\"declared_capacity_per_hour\": 15, ",
            "\"occupancy_ticks\": 10, ",
        };

        private static AirsideLayout ParseLines(List<string> lines, string source)
        {
            return AirsideFactory.CreateLayoutLoader().Parse(LayoutTests.Join(lines), source);
        }

        private static List<string> WithLine(List<string> lines, int line, string text)
        {
            var copy = new List<string>(lines);
            copy[line - 1] = text;
            return copy;
        }

        [Fact]
        public void test_layout_exit_node_defaults_to_threshold()
        {
            // The kept five-field constructor sets ExitNode to ThresholdNode (12 §12.4, 07 L10).
            var five = new RunwayDef(new RunwayId(7), new TaxiNodeId(8), 270, 32, 11U);
            Assert.Equal(8, five.ExitNode.Value);
            Assert.Equal(8, five.ThresholdNode.Value);

            // A file without exit_node gives ExitNode = threshold_node, and the
            // fixture still parses equal to its code-built layout, ExitNode included.
            AirsideLayout parsed = AirsideFixture.Parse();
            Assert.NotEmpty(parsed.Runways);
            foreach (RunwayDef r in parsed.Runways)
            {
                Assert.Equal(r.ThresholdNode.Value, r.ExitNode.Value);
            }

            Assert.Equal(FixtureLayout.Threshold, parsed.Runways[0].ExitNode.Value);
            LayoutTests.AssertSameLayout(FixtureLayout.Layout(), parsed);

            // Load keeps the default on every runway of a code-built multi-runway layout.
            AirsideLayout maxTier = MaxTierLayout.Layout();
            Assert.Equal(MaxTierLayout.Runways, maxTier.Runways.Count);
            foreach (RunwayDef r in maxTier.Runways)
            {
                Assert.Equal(r.ThresholdNode.Value, r.ExitNode.Value);
            }

            // The system's validated layout carries it too (12 §12.9 Layout()).
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")), record: false);
            Assert.Equal(FixtureLayout.Threshold, rig.Airside.Layout().Runways[0].ExitNode.Value);
        }

        [Fact]
        public void test_layout_runway_def_has_kept_and_full_constructors_in_declared_order()
        {
            // 07 L10 (Q-130, Q-132): exactly the two constructors the clause names.
            var full = new RunwayDef(new RunwayId(7), new TaxiNodeId(8), 271, 32, 11U, new TaxiNodeId(9));
            Assert.Equal(7, full.Id.Value);
            Assert.Equal(8, full.ThresholdNode.Value);
            Assert.Equal(271, full.ActiveDirectionDeg);
            Assert.Equal(32, full.DeclaredCapacityPerHour);
            Assert.Equal(11U, full.OccupancyTicks);
            Assert.Equal(9, full.ExitNode.Value);

            ConstructorInfo[] ctors = typeof(RunwayDef).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var shapes = new List<string>();
            foreach (ConstructorInfo c in ctors)
            {
                var names = new List<string>();
                foreach (ParameterInfo p in c.GetParameters())
                {
                    names.Add(p.ParameterType.Name);
                }

                shapes.Add(string.Join(",", names));
            }

            shapes.Sort(StringComparer.Ordinal);
            Assert.Equal(
                new[]
                {
                    "RunwayId,TaxiNodeId,Int32,Int32,UInt32",
                    "RunwayId,TaxiNodeId,Int32,Int32,UInt32,TaxiNodeId",
                },
                shapes.ToArray());

            PropertyInfo? exit = typeof(RunwayDef).GetProperty("ExitNode", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(exit);
            Assert.Equal(typeof(TaxiNodeId), exit!.PropertyType);
            Assert.False(exit.CanWrite);
        }

        [Fact]
        public void test_layout_load_keeps_exit_node_and_accepts_own_threshold_or_junction()
        {
            // A Junction exit (12 §12.4 check 5).
            AirsideLayout withExit = ExitLayout.Layout();
            Assert.Equal(ExitLayout.Exit, withExit.Runways[0].ExitNode.Value);
            Assert.Equal(FixtureLayout.Threshold, withExit.Runways[0].ThresholdNode.Value);

            // Its own threshold, through the six-field constructor.
            LayoutBuilder own = FixtureLayout.Builder();
            own.Runways.Clear();
            own.RunwayWithExit(FixtureLayout.Runway, FixtureLayout.Threshold, FixtureLayout.Threshold, FixtureLayout.CapacityPerHour, FixtureLayout.OccupancyTicks);
            Assert.Equal(FixtureLayout.Threshold, own.Load().Runways[0].ExitNode.Value);

            // Load sorts runways by id and keeps each one's exit with it.
            LayoutBuilder two = ExitLayout.Builder()
                .Node(53, TaxiNodeKind.RunwayThreshold)
                .Edge(53, 53, FixtureLayout.J1, 30);
            two.Runways.Insert(0, new RunwayDef(new RunwayId(9), new TaxiNodeId(53), 90, 15, 10U, new TaxiNodeId(FixtureLayout.J2)));
            AirsideLayout loaded = two.Load();
            Assert.Equal(1, loaded.Runways[0].Id.Value);
            Assert.Equal(ExitLayout.Exit, loaded.Runways[0].ExitNode.Value);
            Assert.Equal(9, loaded.Runways[1].Id.Value);
            Assert.Equal(FixtureLayout.J2, loaded.Runways[1].ExitNode.Value);

            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")), layout: withExit, record: false);
            Assert.Equal(ExitLayout.Exit, rig.Airside.Layout().Runways[0].ExitNode.Value);
        }

        [Fact]
        public void test_layout_parse_reads_optional_exit_node()
        {
            // Present once, it is read.
            AirsideLayout parsed = ParseLines(ExitLayout.FileLines(), "exit.json");
            Assert.Equal(ExitLayout.Exit, parsed.Runways[0].ExitNode.Value);
            LayoutTests.AssertSameLayout(ExitLayout.Layout(), parsed);

            // Keys may come in any order, the optional one included.
            List<string> first = WithLine(
                ExitLayout.FileLines(),
                ExitLayout.RunwayLine,
                "    { \"exit_node\": 4, \"occupancy_ticks\": 10, \"id\": 1, \"declared_capacity_per_hour\": 15, \"threshold_node\": 1, \"active_direction_deg\": 270 }");
            LayoutTests.AssertSameLayout(ExitLayout.Layout(), ParseLines(first, "exit-first.json"));

            // A duplicate exit_node is a shape failure with its line, even with equal values.
            LayoutTests.AssertParseFails(
                WithLine(
                    ExitLayout.FileLines(),
                    ExitLayout.RunwayLine,
                    "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 4, \"exit_node\": 4 }"),
                ExitLayout.RunwayLine);

            // Every other runway key is still required when exit_node is present.
            foreach (string key in RunwayKeys)
            {
                string full = "{ \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 4 }";
                Assert.Contains(key, full, StringComparison.Ordinal);
                string text = "    " + full.Replace(key, string.Empty);
                LayoutTests.AssertParseFails(WithLine(ExitLayout.FileLines(), ExitLayout.RunwayLine, text), ExitLayout.RunwayLine);
            }

            // It is optional only on a runway: on any other object it is an unknown key.
            LayoutTests.AssertParseFails(
                WithLine(ExitLayout.FileLines(), ExitLayout.Node4Line, "    { \"id\": 4, \"kind\": \"junction\", \"exit_node\": 4 },"),
                ExitLayout.Node4Line);
            LayoutTests.AssertParseFails(
                WithLine(ExitLayout.FileLines(), ExitLayout.Edge7Line, "    { \"id\": 7, \"from\": 4, \"to\": 5, \"traversal_ticks\": 5, \"bidirectional\": false, \"exit_node\": 4 },"),
                ExitLayout.Edge7Line);
            LayoutTests.AssertParseFails(
                WithLine(ExitLayout.FileLines(), ExitLayout.Stand1Line, "    { \"id\": 1, \"node\": 11, \"max_aircraft_size_category\": \"medium\", \"departure_sink_node\": 9, \"exit_node\": 4 },"),
                ExitLayout.Stand1Line);

            // Its value is a uint16: type and C#-type range failures are parse failures with the line.
            foreach (string value in new[] { "70000", "-1", "true", "\"4\"", "4.0", "04" })
            {
                LayoutTests.AssertParseFails(
                    WithLine(
                        ExitLayout.FileLines(),
                        ExitLayout.RunwayLine,
                        "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": " + value + " }"),
                    ExitLayout.RunwayLine);
            }

            // A node reference is not range-checked: 0 parses, then fails Load's check 4, with no line.
            LayoutTests.AssertLoadFailsThroughParse(
                WithLine(
                    ExitLayout.FileLines(),
                    ExitLayout.RunwayLine,
                    "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 0 }"),
                "exit_node",
                1,
                0);

            // An exit_node equal to threshold_node, written out, reads as the default.
            AirsideLayout explicitDefault = ParseLines(
                WithLine(
                    ExitLayout.FileLines(),
                    ExitLayout.RunwayLine,
                    "    { \"id\": 1, \"threshold_node\": 1, \"active_direction_deg\": 270, \"declared_capacity_per_hour\": 15, \"occupancy_ticks\": 10, \"exit_node\": 1 }"),
                "exit-default.json");
            Assert.Equal(FixtureLayout.Threshold, explicitDefault.Runways[0].ExitNode.Value);
        }

        [Fact]
        public void test_layout_rejects_bad_exit_node()
        {
            // Check 4: an undeclared exit names the runway, exit_node and the node.
            LayoutTests.AssertRejects(ExitLayout.Builder().RunwayWithExit(74, FixtureLayout.Threshold, 81, 15, 10), "exit_node", 74, 81);

            // Check 4: 0 is an undeclared node, not a range failure (check 1 skips node references).
            LayoutTests.AssertRejects(ExitLayout.Builder().RunwayWithExit(74, FixtureLayout.Threshold, 0, 15, 10), "exit_node", 74, 0);

            // Check 4, within a runway: threshold_node is checked before exit_node.
            LayoutTests.AssertRejects(ExitLayout.Builder().RunwayWithExit(75, 82, 83, 15, 10), "threshold_node", 75, 82);

            // Check 4, across lists: runways come before edges.
            LayoutTests.AssertRejects(
                ExitLayout.Builder().Edge(46, FixtureLayout.J2, 77, 10).RunwayWithExit(76, FixtureLayout.Threshold, 84, 15, 10),
                "exit_node",
                76,
                84);

            // Check 4, within runways: the lowest runway id is reported, whatever the declaration order.
            LayoutTests.AssertRejects(
                ExitLayout.Builder().RunwayWithExit(78, FixtureLayout.Threshold, 86, 15, 10).RunwayWithExit(77, FixtureLayout.Threshold, 85, 15, 10),
                "exit_node",
                77,
                85);

            // Check 5: a StandPosition node as exit.
            LayoutTests.AssertRejects(
                ExitLayout.Builder().RunwayWithExit(79, FixtureLayout.Threshold, FixtureLayout.StandNode(FixtureLayout.S1), 15, 10),
                "exit_node",
                79,
                FixtureLayout.StandNode(FixtureLayout.S1));

            // Check 5: another runway's threshold as exit (only its own threshold or a Junction is allowed).
            LayoutTests.AssertRejects(
                ExitLayout.Builder()
                    .Node(53, TaxiNodeKind.RunwayThreshold)
                    .Edge(53, 53, FixtureLayout.J1, 30)
                    .Runway(64, 53, 15, 10)
                    .RunwayWithExit(79, FixtureLayout.Threshold, 53, 15, 10),
                "exit_node",
                79,
                53);

            // Check 5, within a runway: threshold_node is checked before exit_node.
            LayoutTests.AssertRejects(
                ExitLayout.Builder()
                    .Node(73, TaxiNodeKind.Junction)
                    .Edge(73, 73, FixtureLayout.J1, 10)
                    .RunwayWithExit(80, 73, FixtureLayout.StandNode(FixtureLayout.S2), 15, 10),
                "threshold_node",
                80,
                73);

            // Check 4 runs before check 5, even on a higher runway id.
            LayoutTests.AssertRejects(
                ExitLayout.Builder()
                    .RunwayWithExit(79, FixtureLayout.Threshold, FixtureLayout.StandNode(FixtureLayout.S1), 15, 10)
                    .RunwayWithExit(81, FixtureLayout.Threshold, 87, 15, 10),
                "exit_node",
                81,
                87);

            // Check 7: an exit that reaches no stand names the runway, exit_node and the
            // lowest-id StandPosition node. Stand 5 sits on node 10, the lowest stand
            // node though not the lowest stand id, so a stand id in place of a node fails.
            LayoutTests.AssertRejects(TrappedExit(), "exit_node", 90, 10);

            // Check 5 runs before check 7.
            LayoutTests.AssertRejects(
                TrappedExit().RunwayWithExit(79, FixtureLayout.Threshold, FixtureLayout.StandNode(FixtureLayout.S1), 15, 10),
                "exit_node",
                79,
                FixtureLayout.StandNode(FixtureLayout.S1));

            // Check 6 runs before check 7.
            LayoutTests.AssertRejects(
                TrappedExit().Node(44, TaxiNodeKind.StandPosition).Stand(44, 44, AirsideContent.Super, 944),
                "nodes",
                44);
        }

        /// <summary>
        /// Runway 90 (threshold 1) with exit 4, a junction J1 reaches one way and that
        /// leads only to dead-end junction 5. Stand 5 on node 10 hangs off J1. It
        /// passes checks 1 to 6, and from exit 4 no StandPosition is reachable.
        /// </summary>
        private static LayoutBuilder TrappedExit()
        {
            LayoutBuilder b = FixtureLayout.Builder()
                .Node(4, TaxiNodeKind.Junction)
                .Node(5, TaxiNodeKind.Junction)
                .Node(10, TaxiNodeKind.StandPosition)
                .Edge(7, 4, 5, 5, bidirectional: false)
                .Edge(8, FixtureLayout.J1, 4, 10, bidirectional: false)
                .Edge(10, FixtureLayout.J1, 10, 10)
                .Stand(5, 10, AirsideContent.Super, 9);
            b.Runways.Clear();
            b.RunwayWithExit(90, FixtureLayout.Threshold, 4, FixtureLayout.CapacityPerHour, FixtureLayout.OccupancyTicks);
            return b;
        }
    }
}
