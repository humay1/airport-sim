using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;

namespace AirportSim.App.Render.Tests
{
    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class RecordingCheckpointSink : ICheckpointSink
    {
        public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

        public void Record(in Checkpoint cp)
        {
            Recorded.Add(cp);
        }

        public List<string> Describe()
        {
            var result = new List<string>();
            foreach (Checkpoint cp in Recorded)
            {
                var parts = new List<string>();
                foreach (ulong h in cp.SystemHashes)
                {
                    parts.Add(h.ToString("X16", CultureInfo.InvariantCulture));
                }

                result.Add(string.Format(CultureInfo.InvariantCulture, "t={0} world={1:X16} core={2:X16} [{3}]", cp.Tick, cp.WorldHash, cp.CoreHash, string.Join(",", parts)));
            }

            return result;
        }
    }

    /// <summary>
    /// Test-owned content (08 §8.11a lets tests build definitions directly):
    /// the aircraft types of tests/fixtures/schedule/phase0-200.csv, the size
    /// categories the airside fixture's stands name, the two pax profiles,
    /// and the queue profile of the flow fixture's queue nodes. Fixture
    /// sizing, not balance.
    /// </summary>
    internal static class Phase1Content
    {
        private static readonly (string Id, int Ordinal)[] Sizes =
        {
            ("small", 1), ("medium", 2), ("heavy", 3), ("super", 4),
        };

        private static readonly (string Id, string Size)[] Aircraft =
        {
            ("a320", "medium"), ("a321", "medium"), ("a359", "heavy"), ("a388", "super"), ("atr72", "small"),
            ("b738", "medium"), ("b744", "heavy"), ("b789", "heavy"), ("crj900", "small"),
        };

        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        public static IContentIndex Index()
        {
            var defs = new List<IContentDefinition>();
            foreach ((string id, int ordinal) in Sizes)
            {
                defs.Add(new SizeCategoryDefinition(new ContentId(id), ordinal));
            }

            foreach ((string id, string size) in Aircraft)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId(size)));
            }

            defs.Add(Profile("business", Business));
            defs.Add(Profile("leisure", Leisure));
            defs.Add(new QueueProfileDefinition(new ContentId("security_standard"), Fx.FromInt(15), 1000, Fx.FromInt(10), Fx.FromInt(3), DelayCategory.SecurityQueue));
            return ContentIndexFactory.Create(defs);
        }

        private static PaxProfileDefinition Profile(string id, (uint Minutes, uint Share)[] curve)
        {
            var buckets = new List<ShowUpBucket>();
            foreach ((uint m, uint s) in curve)
            {
                buckets.Add(new ShowUpBucket(m, s));
            }

            return new PaxProfileDefinition(new ContentId(id), Fx.FromRatio(13, 10), buckets);
        }
    }

    /// <summary>The real sim of 15 §15.12's integration test: sim.world, sim.schedule, sim.airside and sim.flow.</summary>
    internal sealed class Phase1Sim
    {
        public const string WorldFile = "phase0-landside.json";
        public const string FlowFile = "phase0-landside.flow.json";
        public const string ScheduleFile = "phase0-200.csv";
        public const string AirsideFile = "phase1-single-runway.json";
        public const uint FlowSink = 9;
        public const ulong Seed = 0x5EED_0020UL;

        public readonly ISimHost Host;
        public readonly IAirsideSystem Airside;
        public readonly IFlowSystem Flow;
        public readonly IScheduleSystem Schedule;
        public readonly RecordingCheckpointSink Checkpoints = new RecordingCheckpointSink();

        /// <summary>16 §16.4's construction order (world; flow; schedule; airside), registered in 08 §8.5's order.</summary>
        public Phase1Sim()
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(Seed, Phase1Content.Index(), Checkpoints, new NullLog()));
            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(Repo.Read("tests", "fixtures", "world", WorldFile), WorldFile);
            IWorldSystem world = WorldFactory.CreateSystem(b.Services, walk);
            FlowGraph graph = FlowFactory.CreateGraphLoader().Load(FlowBytes(), FlowFile, world);
            Flow = FlowFactory.CreateSystem(b.Services, graph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(Repo.Read("tests", "fixtures", "schedule", ScheduleFile), ScheduleFile);
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(b.Services, table, Flow);
            Schedule = schedule;
            Airside = AirsideFactory.CreateSystem(b.Services, AirsideLayout(), new AirsideRules(10U, 2U), schedule, Flow, false);
            b.Register(world);
            b.Register(schedule);
            b.Register(Airside);
            b.Register(Flow);
            Host = b.Build();
        }

        public static byte[] FlowBytes()
        {
            return Repo.Read("tests", "fixtures", "flow", FlowFile);
        }

        /// <summary>
        /// tests/fixtures/airside/phase1-single-runway.json, with every
        /// stand's departure_sink_node moved to the flow fixture's one Sink.
        /// The airside fixture names sinks 901-904, which the flow fixture
        /// does not have, so a departure's Absorb would throw (09 §9.7). The
        /// taxi graph, and so every id the render layout positions, is the
        /// fixture's unchanged.
        /// </summary>
        public static AirsideLayout AirsideLayout()
        {
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            AirsideLayout parsed = loader.Parse(Repo.Read("tests", "fixtures", "airside", AirsideFile), AirsideFile);
            var stands = new List<StandDef>();
            foreach (StandDef s in parsed.Stands)
            {
                stands.Add(new StandDef(s.Id, s.Node, s.MaxAircraftSizeCategory, new NodeId(FlowSink)));
            }

            return loader.Load(new AirsideLayout(parsed.Runways, parsed.Nodes, parsed.Edges, stands));
        }

        /// <summary>(node id, kind) of every node in the flow fixture, read from its text.</summary>
        public static List<(uint Node, string Kind)> FlowFixtureNodes()
        {
            string text = Encoding.UTF8.GetString(FlowBytes());
            var nodes = new List<(uint Node, string Kind)>();
            foreach (Match m in Regex.Matches(text, "\"id\"\\s*:\\s*(\\d+)\\s*,\\s*\"kind\"\\s*:\\s*\"([a-z]+)\""))
            {
                nodes.Add((uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value));
            }

            return nodes;
        }

        /// <summary>The non-empty entry_node values of the schedule fixture.</summary>
        public static SortedSet<uint> ScheduleEntryNodes()
        {
            string[] lines = Encoding.UTF8.GetString(Repo.Read("tests", "fixtures", "schedule", ScheduleFile)).Split('\n');
            var nodes = new SortedSet<uint>();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] f = lines[i].TrimEnd('\r').Split(',');
                if (f.Length >= 14 && f[13].Length > 0)
                {
                    nodes.Add(uint.Parse(f[13], CultureInfo.InvariantCulture));
                }
            }

            return nodes;
        }
    }

    /// <summary>
    /// 12 §12.13's fixture with 19 §19.2c's Q-132 exit lines added in code
    /// (15 §15.23: the playtest airside file itself changes only in T-058):
    /// runway 1's "exit_node": 4, junction nodes 4, 5 and 6, and the one-way
    /// edges 7 (4 to 5), 8 (5 to 6) and 9 (6 to 2), appended at the ends of
    /// their lists. The size categories are not substituted: the render
    /// loader reads ids only.
    /// </summary>
    internal static class PlaytestAirside
    {
        public const string SourceName = "playtest-airside.json";

        public static string Text()
        {
            string text = Encoding.UTF8.GetString(Repo.Read("tests", "fixtures", "airside", Phase1Sim.AirsideFile));
            text = Replace(text, "\"occupancy_ticks\": 10 }", "\"occupancy_ticks\": 10, \"exit_node\": 4 }");
            text = Replace(
                text,
                "{ \"id\": 14, \"kind\": \"stand_position\" }",
                "{ \"id\": 14, \"kind\": \"stand_position\" },\n    { \"id\": 4, \"kind\": \"junction\" },\n    { \"id\": 5, \"kind\": \"junction\" },\n    { \"id\": 6, \"kind\": \"junction\" }");
            text = Replace(
                text,
                "{ \"id\": 6, \"from\": 3, \"to\": 14, \"traversal_ticks\": 20, \"bidirectional\": true }",
                "{ \"id\": 6, \"from\": 3, \"to\": 14, \"traversal_ticks\": 20, \"bidirectional\": true },\n"
                + "    { \"id\": 7, \"from\": 4, \"to\": 5, \"traversal_ticks\": 5, \"bidirectional\": false },\n"
                + "    { \"id\": 8, \"from\": 5, \"to\": 6, \"traversal_ticks\": 40, \"bidirectional\": false },\n"
                + "    { \"id\": 9, \"from\": 6, \"to\": 2, \"traversal_ticks\": 10, \"bidirectional\": false }");
            return text;
        }

        /// <summary>Parsed by sim.airside's own loader, so checks 4, 5 and 7 of 12 §12.4 have passed.</summary>
        public static AirsideLayout Layout()
        {
            return AirsideFactory.CreateLayoutLoader().Parse(new UTF8Encoding(false).GetBytes(Text()), SourceName);
        }

        private static string Replace(string text, string from, string to)
        {
            int at = text.IndexOf(from, StringComparison.Ordinal);
            if (at < 0 || text.IndexOf(from, at + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("the airside fixture does not hold exactly one '" + from + "'");
            }

            return text.Replace(from, to, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The 15 §15.12 render layout over the Phase 1 fixtures: a position for
    /// every taxi node of phase1-single-runway.json, geometry for its one
    /// runway, and a box for every node of phase0-landside.flow.json (the
    /// fixture T-007 and T-023 run), queues 5 and 6 among them.
    ///
    ///   runway 1: (-2000,0)-(0,0), width 45, threshold node 1 at (0,0)
    ///   junctions 2 (300,0), 3 (600,0); stand nodes 11..14 at (150k - 1350, -150)
    ///   flow node k: box ((k-1)·100, 200)-((k-1)·100 + 80, 260)
    ///
    /// Version 2 (Q-130): 15 §15.18's scenery, an apron, a terminal enclosing
    /// the landside zones, a satellite pier facing the stands, a control
    /// tower, and a jet bridge from the pier to each stand's aircraft door.
    ///
    /// Version 3 (Q-132): 15 §15.23's bridge stands and walkways.
    /// </summary>
    internal static class Phase1RenderLayout
    {
        public static List<LayoutArea> Areas()
        {
            return new List<LayoutArea>
            {
                new LayoutArea(1U, AreaKind.Apron, 20, -180, 900, 190),
                new LayoutArea(2U, AreaKind.Terminal, -10, 190, 890, 275),
                new LayoutArea(3U, AreaKind.Pier, 280, -205, 780, -180),
                new LayoutArea(4U, AreaKind.ControlTower, 920, 200, 940, 220),
            };
        }

        /// <summary>Version 3 (Q-132, 15 §15.23): bridge k serves stand k.</summary>
        public static List<LayoutBridge> Bridges()
        {
            return new List<LayoutBridge>
            {
                new LayoutBridge(1U, 308, -180, 306, -160, 3, new StandId(1)),
                new LayoutBridge(2U, 452, -180, 446, -163, 3, new StandId(2)),
                new LayoutBridge(3U, 608, -180, 606, -160, 3, new StandId(3)),
                new LayoutBridge(4U, 770, -180, 761, -158, 3, new StandId(4)),
            };
        }

        /// <summary>Version 3 (Q-132, 15 §15.23): along the middles of corridors 4 and 7.</summary>
        public static List<LayoutWalkway> Walkways()
        {
            return new List<LayoutWalkway>
            {
                new LayoutWalkway(new NodeId(4), 300, 230, 380, 230, 40),
                new LayoutWalkway(new NodeId(7), 600, 230, 680, 230, 40),
            };
        }

        public static RenderLayout Build()
        {
            var taxi = new List<TaxiNodePosition>
            {
                new TaxiNodePosition(new TaxiNodeId(1), 0, 0),
                new TaxiNodePosition(new TaxiNodeId(2), 300, 0),
                new TaxiNodePosition(new TaxiNodeId(3), 600, 0),
            };
            for (ushort n = 11; n <= 14; n++)
            {
                taxi.Add(new TaxiNodePosition(new TaxiNodeId(n), (150 * n) - 1350, -150));
            }

            var runways = new List<RunwayGeometry> { new RunwayGeometry(new RunwayId(1), -2000, 0, 0, 0, 45) };
            var boxes = new List<FlowNodeBox>();
            for (uint k = 1; k <= 9; k++)
            {
                int x = ((int)k - 1) * 100;
                boxes.Add(new FlowNodeBox(new NodeId(k), x, 200, x + 80, 260, k == 5 || k == 6 ? 200 : 100));
            }

            return new RenderLayout(taxi, runways, boxes, 40, 30, 2, 15, Areas(), Bridges(), Walkways());
        }

        /// <summary>
        /// tests/fixtures/render/playtest-layout.json (Q-132, 15 §15.23):
        /// Build() plus positions for the playtest exit's taxi nodes 4, 5
        /// and 6, in ascending node order as Load returns them.
        /// </summary>
        public static RenderLayout Playtest()
        {
            RenderLayout l = Build();
            var taxi = new List<TaxiNodePosition>(l.TaxiNodes)
            {
                new TaxiNodePosition(new TaxiNodeId(4), -1850, 0),
                new TaxiNodePosition(new TaxiNodeId(5), -1850, -90),
                new TaxiNodePosition(new TaxiNodeId(6), 0, -90),
            };
            taxi.Sort((a, b) => a.Node.Value.CompareTo(b.Node.Value));
            return new RenderLayout(taxi, l.Runways, l.FlowNodes, l.StandSize, l.AircraftSize, l.AgentSize, l.TaxiwayWidth, l.Areas, l.Bridges, l.Walkways);
        }
    }
}
