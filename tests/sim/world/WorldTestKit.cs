using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.World.Tests
{
    // Test doubles and graph builders for the T-012 suite. 08 §8.11a (Q-014):
    // sim.core publishes no null or empty IContentIndex, ICheckpointSink or
    // ISimLog, so tests write their own.
    //
    // Route tests build graphs through WalkGraph's public constructor (07 L10),
    // so they test routing apart from parsing. Every graph built that way obeys
    // WalkGraph's own contract (Nodes ascending NodeId, Edges ascending EdgeId,
    // 18 §18.2) and §18.2's load-time rules, so it is one the loader could emit.

    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class RecordingSink : ICheckpointSink
    {
        public readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();

        public void Record(in Checkpoint cp)
        {
            Checkpoints.Add(cp);
        }
    }

    internal sealed class EmptyContent : IContentIndex
    {
        public bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
        {
            definition = default!;
            return false;
        }

        public IReadOnlyList<ContentId> AllOf(ContentKind kind)
        {
            return Array.Empty<ContentId>();
        }
    }

    /// <summary>Counts every touch of the RNG service. 18 §18.4: sim.world has no RNG.</summary>
    internal sealed class CountingRandom : IRandomService
    {
        public int Touches;

        public IRandomStream Stream(RngStreamName name)
        {
            Touches++;
            throw new InvalidOperationException("sim.world must not draw from the RNG (18 §18.4)");
        }

        public ulong MasterSeed
        {
            get
            {
                Touches++;
                return 0;
            }
        }
    }

    /// <summary>Counts publications. T-012: sim.world emits no events.</summary>
    internal sealed class CountingPublisher : IEventPublisher
    {
        public int Published;

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            Published++;
            return default;
        }
    }

    internal sealed class FixedClock : ISimClock
    {
        public ulong CurrentTick { get; set; }

        public uint DayIndex => 0;

        public uint SecondOfDay => 0;

        public Fx MinutesBetween(ulong a, ulong b)
        {
            return default;
        }

        public ulong TickOfDayTime(uint dayIndex, uint secondOfDay)
        {
            return 0;
        }
    }

    internal delegate void ProbeTick(in TickContext ctx);

    /// <summary>
    /// A downstream consumer at a legal registry position whose module is
    /// absent from the build (08 §8.5, Q-014). Its hash is a caller-driven mix.
    /// </summary>
    internal sealed class ProbeSystem : ISimSystem
    {
        public ProbeSystem(ushort id)
        {
            Id = new SystemId(id);
        }

        public SystemId Id { get; }

        public string Name => "probe";

        public ulong State;

        public ProbeTick? OnTick;

        public void Tick(in TickContext ctx)
        {
            OnTick?.Invoke(ctx);
        }

        public void Mix(ulong x)
        {
            unchecked
            {
                State = (State ^ x) * 0x100000001B3UL + 1UL;
            }
        }

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            h.Feed(State);
            return h.Result;
        }
    }

    /// <summary>
    /// SplitMix64 as pinned by 08 §8.8, the input generator for property-style
    /// tests (07 L4). Always seeded with an integer literal inside the test.
    /// </summary>
    internal sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform-enough integer in [lo, hi]; test inputs only.</summary>
        public int Range(int lo, int hi)
        {
            return lo + (int)(Next() % (ulong)(hi - lo + 1));
        }
    }

    /// <summary>
    /// Writes a graph in the 18 §18.2 file format. With a generator, array
    /// order, key order and whitespace are scrambled, all of which §18.2
    /// permits and none of which may reach the routes (§18.3).
    /// </summary>
    internal static class GraphJson
    {
        public static string Write(in WalkGraph g, SplitMix64? scramble = null)
        {
            var nodes = new List<string>();
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                WalkNodeDef n = g.Nodes[i];
                nodes.Add(Obj(scramble, ("id", n.Id.Value), ("length_metres", n.LengthMetres)));
            }

            var edges = new List<string>();
            for (int i = 0; i < g.Edges.Count; i++)
            {
                WalkEdgeDef e = g.Edges[i];
                edges.Add(Obj(scramble, ("id", e.Id.Value), ("from", e.From.Value), ("to", e.To.Value)));
            }

            Shuffle(scramble, nodes);
            Shuffle(scramble, edges);
            var top = new List<string>
            {
                "\"schema_version\": 1",
                "\"nodes\": [" + string.Join("," + Ws(scramble), nodes) + "]",
                "\"edges\": [" + string.Join("," + Ws(scramble), edges) + "]",
            };
            Shuffle(scramble, top);
            return "{" + Ws(scramble) + string.Join("," + Ws(scramble), top) + Ws(scramble) + "}\n";
        }

        private static string Obj(SplitMix64? scramble, params (string Key, uint Value)[] fields)
        {
            var parts = new List<string>();
            foreach ((string key, uint value) in fields)
            {
                parts.Add("\"" + key + "\"" + Ws(scramble) + ":" + Ws(scramble) + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            Shuffle(scramble, parts);
            return "{" + Ws(scramble) + string.Join("," + Ws(scramble), parts) + Ws(scramble) + "}";
        }

        private static string Ws(SplitMix64? scramble)
        {
            if (scramble == null)
            {
                return " ";
            }

            switch (scramble.Range(0, 4))
            {
                case 0: return string.Empty;
                case 1: return " ";
                case 2: return "\n  ";
                case 3: return "\t";
                default: return "\r\n";
            }
        }

        private static void Shuffle(SplitMix64? scramble, List<string> items)
        {
            if (scramble == null)
            {
                return;
            }

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = scramble.Range(0, i);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }

    /// <summary>
    /// Builds a WalkGraph from nodes and edges given in any order, sorting them
    /// into the ascending order WalkGraph promises (18 §18.2).
    /// </summary>
    internal sealed class GraphBuilder
    {
        private readonly List<WalkNodeDef> _nodes = new List<WalkNodeDef>();
        private readonly List<WalkEdgeDef> _edges = new List<WalkEdgeDef>();

        public GraphBuilder Node(uint id, uint lengthMetres)
        {
            _nodes.Add(new WalkNodeDef(new NodeId(id), lengthMetres));
            return this;
        }

        public GraphBuilder Edge(uint id, uint from, uint to)
        {
            _edges.Add(new WalkEdgeDef(new EdgeId(id), new NodeId(from), new NodeId(to)));
            return this;
        }

        public WalkGraph Build(ulong fixtureHash = 0x0123456789ABCDEFUL)
        {
            var nodes = new List<WalkNodeDef>(_nodes);
            nodes.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            var edges = new List<WalkEdgeDef>(_edges);
            edges.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            return new WalkGraph(nodes.ToArray(), edges.ToArray(), fixtureHash);
        }
    }

    internal static class WorldKit
    {
        public static SimHostConfig Config(ulong seed, ICheckpointSink sink)
        {
            return new SimHostConfig(seed, new EmptyContent(), sink, new NullLog());
        }

        public static ISimHostBuilder Builder(ulong seed = 1, ICheckpointSink? sink = null)
        {
            return SimHostFactory.CreateBuilder(Config(seed, sink ?? new RecordingSink()));
        }

        public static IWorldSystem Create(in WalkGraph graph)
        {
            ISimHostBuilder b = Builder();
            return WorldFactory.CreateSystem(b.Services, graph);
        }

        public static TickContext Context(ulong tick, IRandomService rng, IEventPublisher events)
        {
            return new TickContext(tick, new FixedClock { CurrentTick = tick }, events, rng, new EmptyContent(), new NullLog());
        }

        /// <summary>FNV-1a-64 over raw bytes, no length prefix (08 §8.9 constants, 18 §18.2).</summary>
        public static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
        {
            ulong h = 0xCBF29CE484222325UL;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    h = (h ^ bytes[i]) * 0x100000001B3UL;
                }
            }

            return h;
        }

        public static byte[] Utf8(string s)
        {
            return System.Text.Encoding.UTF8.GetBytes(s);
        }

        public static WalkGraph Load(string json, string sourceName = "test.json")
        {
            return WorldFactory.CreateGraphLoader().Load(Utf8(json), sourceName);
        }

        public static ulong ExpectedHash(ulong fixtureHash)
        {
            // 18 §18.4: ComputeStateHash() feeds WalkGraph.FixtureHash only.
            var h = new StateHasher();
            h.Feed(fixtureHash);
            return h.Result;
        }

        public static uint[] Ids(IReadOnlyList<NodeId> list)
        {
            var r = new uint[list.Count];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = list[i].Value;
            }

            return r;
        }

        /// <summary>Nodes and Edges of a graph, in list order, excluding FixtureHash.</summary>
        public static string Content(in WalkGraph g)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                sb.Append('N').Append(g.Nodes[i].Id.Value).Append(':').Append(g.Nodes[i].LengthMetres).Append(';');
            }

            for (int i = 0; i < g.Edges.Count; i++)
            {
                WalkEdgeDef e = g.Edges[i];
                sb.Append('E').Append(e.Id.Value).Append(':').Append(e.From.Value).Append('>').Append(e.To.Value).Append(';');
            }

            return sb.ToString();
        }

        public static uint[] Ids(IReadOnlyList<EdgeId> list)
        {
            var r = new uint[list.Count];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = list[i].Value;
            }

            return r;
        }

        /// <summary>
        /// A random graph of 2..maxNodes nodes, every LengthMetres in [1, maxLength],
        /// and randomly numbered ids so id order is unrelated to insertion order.
        /// Lengths are at least 1 so every cycle has positive cost and the §18.3
        /// optimum is a simple path; small maxLength makes cost ties frequent.
        /// </summary>
        public static WalkGraph RandomGraph(SplitMix64 rng, int maxNodes, int maxLength, int edgePercent)
        {
            int n = rng.Range(2, maxNodes);
            uint[] nodeIds = DistinctIds(rng, n, 60);
            var b = new GraphBuilder();
            for (int i = 0; i < n; i++)
            {
                b.Node(nodeIds[i], (uint)rng.Range(1, maxLength));
            }

            var pairs = new List<(uint From, uint To)>();
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i != j && rng.Range(1, 100) <= edgePercent)
                    {
                        pairs.Add((nodeIds[i], nodeIds[j]));
                    }
                }
            }

            uint[] edgeIds = DistinctIds(rng, pairs.Count, 200);
            for (int k = 0; k < pairs.Count; k++)
            {
                b.Edge(edgeIds[k], pairs[k].From, pairs[k].To);
            }

            return b.Build(rng.Next());
        }

        private static uint[] DistinctIds(SplitMix64 rng, int count, int pool)
        {
            var all = new List<uint>();
            for (uint v = 1; v <= (uint)Math.Max(pool, count); v++)
            {
                all.Add(v);
            }

            for (int i = all.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i);
                (all[i], all[j]) = (all[j], all[i]);
            }

            return all.GetRange(0, count).ToArray();
        }
    }

    /// <summary>
    /// An independent reference for 18 §18.3 on small graphs whose lengths are
    /// all positive: exhaustive search over simple paths. Cost is the sum of
    /// LengthMetres of nodes entered after the start, including the destination;
    /// ties go to the lexicographically smallest EdgeId sequence.
    /// </summary>
    internal sealed class RouteOracle
    {
        private readonly Dictionary<uint, uint> _length = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, (uint From, uint To)> _edge = new Dictionary<uint, (uint From, uint To)>();
        private readonly Dictionary<uint, List<uint>> _out = new Dictionary<uint, List<uint>>();

        public RouteOracle(in WalkGraph g)
        {
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                _length[g.Nodes[i].Id.Value] = g.Nodes[i].LengthMetres;
                _out[g.Nodes[i].Id.Value] = new List<uint>();
            }

            for (int i = 0; i < g.Edges.Count; i++)
            {
                WalkEdgeDef e = g.Edges[i];
                _edge[e.Id.Value] = (e.From.Value, e.To.Value);
                _out[e.From.Value].Add(e.Id.Value);
            }

            foreach (List<uint> list in _out.Values)
            {
                list.Sort();
            }
        }

        public bool Reaches(uint from, uint destination)
        {
            var seen = new HashSet<uint> { from };
            var stack = new Stack<uint>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                uint n = stack.Pop();
                foreach (uint e in _out[n])
                {
                    uint to = _edge[e].To;
                    if (to == destination)
                    {
                        return true;
                    }

                    if (seen.Add(to))
                    {
                        stack.Push(to);
                    }
                }
            }

            return false;
        }

        /// <summary>The §18.3 node list for PathVia, or null when unreachable.</summary>
        public uint[]? PathVia(uint firstEdge, uint destination)
        {
            uint start = _edge[firstEdge].To;
            List<uint>? bestEdges = null;
            ulong bestCost = ulong.MaxValue;
            var edges = new List<uint> { firstEdge };
            var onPath = new HashSet<uint> { start };

            void Walk(uint at, ulong cost)
            {
                if (at == destination)
                {
                    if (bestEdges == null || cost < bestCost || (cost == bestCost && LexLess(edges, bestEdges)))
                    {
                        bestEdges = new List<uint>(edges);
                        bestCost = cost;
                    }

                    return;
                }

                foreach (uint e in _out[at])
                {
                    uint to = _edge[e].To;
                    if (onPath.Add(to))
                    {
                        edges.Add(e);
                        Walk(to, cost + _length[to]);
                        edges.RemoveAt(edges.Count - 1);
                        onPath.Remove(to);
                    }
                }
            }

            Walk(start, _length[start]);
            if (bestEdges == null)
            {
                return null;
            }

            var nodes = new uint[bestEdges.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = _edge[bestEdges[i]].To;
            }

            return nodes;
        }

        private static bool LexLess(List<uint> a, List<uint> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i];
                }
            }

            return a.Count < b.Count;
        }
    }

    /// <summary>
    /// The Phase 0/1 landside fixture, tests/fixtures/world/phase0-landside.json
    /// (18 §18.6). Two Sources reach the single pooled Gate only through one of
    /// two alternative security Queue nodes, and corridors have
    /// LengthMetres > 0. Build() is the same graph in code, the expectation the
    /// loaded file is checked against.
    /// </summary>
    internal static class Phase0Landside
    {
        public const string SourceName = "tests/fixtures/world/phase0-landside.json";

        public static byte[] ReadFixture()
        {
            // The test csproj is fixed byte for byte (07 L3) and cannot copy
            // fixtures to the output, so walk up from the output to the repo.
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir, "tests", "fixtures", "world", "phase0-landside.json");
                if (System.IO.File.Exists(candidate))
                {
                    return System.IO.File.ReadAllBytes(candidate);
                }

                dir = System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }

            throw new InvalidOperationException("fixture not found above " + AppContext.BaseDirectory);
        }

        public static WalkGraph Load()
        {
            return WorldFactory.CreateGraphLoader().Load(ReadFixture(), SourceName);
        }

        public const uint Kerb = 1;          // Source
        public const uint RailBox = 2;       // Source
        public const uint CheckInHall = 3;   // Hall
        public const uint LandsideCorridor = 4;
        public const uint SecurityA = 5;     // Queue
        public const uint SecurityB = 6;     // Queue
        public const uint AirsideCorridor = 7;
        public const uint Gate = 8;          // the one pooled Gate, 18 §18.5

        public const uint KerbToHall = 1;
        public const uint RailToHall = 2;
        public const uint HallToCorridor = 3;
        public const uint CorridorToSecurityA = 4;
        public const uint CorridorToSecurityB = 5;
        public const uint SecurityAToAirside = 6;
        public const uint SecurityBToAirside = 7;
        public const uint AirsideToGate = 8;

        public const ulong FixtureHash = 0x5EED0000_00000012UL;

        public static WalkGraph Build()
        {
            return new GraphBuilder()
                .Node(Kerb, 0)
                .Node(RailBox, 0)
                .Node(CheckInHall, 30)
                .Node(LandsideCorridor, 120)
                .Node(SecurityA, 10)
                .Node(SecurityB, 10)
                .Node(AirsideCorridor, 80)
                .Node(Gate, 20)
                .Edge(KerbToHall, Kerb, CheckInHall)
                .Edge(RailToHall, RailBox, CheckInHall)
                .Edge(HallToCorridor, CheckInHall, LandsideCorridor)
                .Edge(CorridorToSecurityA, LandsideCorridor, SecurityA)
                .Edge(CorridorToSecurityB, LandsideCorridor, SecurityB)
                .Edge(SecurityAToAirside, SecurityA, AirsideCorridor)
                .Edge(SecurityBToAirside, SecurityB, AirsideCorridor)
                .Edge(AirsideToGate, AirsideCorridor, Gate)
                .Build(FixtureHash);
        }
    }
}
