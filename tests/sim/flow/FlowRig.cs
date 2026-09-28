using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow.Tests
{
    internal delegate void ProbeTick(in TickContext ctx);

    /// <summary>
    /// A system at a legal registry position whose module is absent from the
    /// build (08 §8.5, Q-014). Used as the injector (position 2, sim.schedule's)
    /// and as the event subscriber (position 7, sim.delay's).
    /// </summary>
    internal sealed class ProbeSystem : ISimSystem
    {
        public ProbeSystem(ushort id)
        {
            Id = new SystemId(id);
        }

        public SystemId Id { get; }

        public string Name => "probe";

        public ProbeTick? OnTick;

        public void Tick(in TickContext ctx)
        {
            OnTick?.Invoke(ctx);
        }

        public ulong ComputeStateHash()
        {
            return 0;
        }
    }

    /// <summary>
    /// Builds a walk graph (18 §18.2 format) and its flow graph (09 §9.11
    /// format, Q-032) together. Edge ids are assigned 1, 2, 3... in call order.
    /// </summary>
    internal sealed class TestGraph
    {
        private readonly List<(uint Id, uint Length, string Kind, int Servers, int Open, string? Profile)> _nodes =
            new List<(uint, uint, string, int, int, string?)>();

        private readonly List<(uint Id, uint From, uint To)> _edges = new List<(uint, uint, uint)>();

        public TestGraph Node(uint id, string kind, uint length = 0)
        {
            _nodes.Add((id, length, kind, 0, 0, null));
            return this;
        }

        public TestGraph Queue(uint id, int servers, int open, ContentId profile, uint length = 0)
        {
            _nodes.Add((id, length, "queue", servers, open, profile.Value));
            return this;
        }

        public TestGraph Edge(uint from, uint to)
        {
            _edges.Add(((uint)_edges.Count + 1, from, to));
            return this;
        }

        /// <summary>Adds from -> to as the given edge id (for tie-break tests).</summary>
        public TestGraph Edge(uint id, uint from, uint to)
        {
            _edges.Add((id, from, to));
            return this;
        }

        /// <summary>Ids of the Corridor nodes.</summary>
        public HashSet<uint> Corridors()
        {
            var ids = new HashSet<uint>();
            foreach (var n in _nodes)
            {
                if (n.Kind == "corridor")
                {
                    ids.Add(n.Id);
                }
            }

            return ids;
        }

        public string WorldJson()
        {
            var sb = new StringBuilder("{\"schema_version\": 1, \"nodes\": [");
            for (int i = 0; i < _nodes.Count; i++)
            {
                sb.Append(i == 0 ? "" : ", ").Append("{\"id\": ").Append(_nodes[i].Id).Append(", \"length_metres\": ").Append(_nodes[i].Length).Append('}');
            }

            sb.Append("], \"edges\": [");
            for (int i = 0; i < _edges.Count; i++)
            {
                sb.Append(i == 0 ? "" : ", ").Append("{\"id\": ").Append(_edges[i].Id).Append(", \"from\": ").Append(_edges[i].From).Append(", \"to\": ").Append(_edges[i].To).Append('}');
            }

            return sb.Append("]}\n").ToString();
        }

        public string FlowJson()
        {
            var sb = new StringBuilder("{\n  \"schema_version\": 1,\n  \"nodes\": [\n");
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                sb.Append("    {\"id\": ").Append(n.Id).Append(", \"kind\": \"").Append(n.Kind).Append('"');
                if (n.Kind == "queue")
                {
                    sb.Append(", \"server_count\": ").Append(n.Servers.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"servers_open\": ").Append(n.Open.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"queue_profile\": \"").Append(n.Profile).Append('"');
                }

                sb.Append('}').Append(i == _nodes.Count - 1 ? "\n" : ",\n");
            }

            return sb.Append("  ]\n}\n").ToString();
        }

        public IWorldSystem World(ISimHostBuilder b)
        {
            WalkGraph g = WorldFactory.CreateGraphLoader().Load(Fixtures.Utf8(WorldJson()), "test-world.json");
            return WorldFactory.CreateSystem(b.Services, g);
        }
    }

    internal static class Fixtures
    {
        public const string WorldPath = "tests/fixtures/world/phase0-landside.json";
        public const string FlowPath = "tests/fixtures/flow/phase0-landside.flow.json";

        public static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        /// <summary>
        /// 07 "Fixture location" (Q-031): walk up from AppContext.BaseDirectory to
        /// the directory holding AirportSim.sln, then join the relative path. Not
        /// finding the root fails the test.
        /// </summary>
        public static byte[] Read(string repoRelative)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "AirportSim.sln")))
            {
                dir = System.IO.Path.GetDirectoryName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            }

            if (dir == null)
            {
                throw new InvalidOperationException("AirportSim.sln not found above " + AppContext.BaseDirectory);
            }

            return System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, repoRelative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        }
    }

    /// <summary>The fixture's node and edge ids (tests/fixtures/world/phase0-landside.json).</summary>
    internal static class Landside
    {
        public const uint Kerb = 1;
        public const uint RailBox = 2;
        public const uint CheckInHall = 3;
        public const uint LandsideCorridor = 4;
        public const uint SecurityA = 5;
        public const uint SecurityB = 6;
        public const uint AirsideCorridor = 7;
        public const uint Gate = 8;
        public const uint Departed = 9;

        public const uint CorridorToSecurityA = 4;
        public const uint CorridorToSecurityB = 5;

        public static readonly ContentId SecurityProfile = new ContentId("security_standard");
    }

    /// <summary>
    /// A composed sim: sim.world at 1, an injector probe at 2, sim.flow at 4 and
    /// an event recorder at 7, registered in registry order (08 §8.5).
    /// </summary>
    internal sealed class Rig
    {
        public IWorldSystem World = null!;
        public IFlowSystem Flow = null!;
        public ISimHost Host = null!;
        public FlowEvents? Events;
        public RecordingSink Checkpoints = null!;
        public ProbeTick? Inject;
        public ProbeSystem Injector = null!;
        public HashSet<uint> Corridors = new HashSet<uint>();

        public static Rig Create(TestGraph graph, IContentIndex content, ulong seed = 1, bool recordEvents = true)
        {
            Rig rig = Create(b => graph.World(b), Fixtures.Utf8(graph.FlowJson()), "test.flow.json", content, seed, recordEvents);
            rig.Corridors = graph.Corridors();
            return rig;
        }

        public static Rig Fixture(IContentIndex content, ulong seed = 1, bool recordEvents = true)
        {
            Rig rig = Create(
                b => WorldFactory.CreateSystem(b.Services, WorldFactory.CreateGraphLoader().Load(Fixtures.Read(Fixtures.WorldPath), Fixtures.WorldPath)),
                Fixtures.Read(Fixtures.FlowPath),
                Fixtures.FlowPath,
                content,
                seed,
                recordEvents);
            rig.Corridors = new HashSet<uint> { Landside.LandsideCorridor, Landside.AirsideCorridor };
            return rig;
        }

        public static Rig Create(Func<ISimHostBuilder, IWorldSystem> world, byte[] flowFile, string flowName, IContentIndex content, ulong seed, bool recordEvents)
        {
            var rig = new Rig { Checkpoints = new RecordingSink() };
            ISimHostBuilder b = FlowKit.Builder(content, seed, rig.Checkpoints);
            rig.World = world(b);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(flowFile, flowName, rig.World);
            rig.Flow = FlowFactory.CreateSystem(b.Services, flowGraph, rig.World);
            rig.Injector = new ProbeSystem(2) { OnTick = (in TickContext ctx) => rig.Inject?.Invoke(ctx) };
            b.Register(rig.World);
            b.Register(rig.Injector);
            b.Register(rig.Flow);
            if (recordEvents)
            {
                rig.Events = new FlowEvents(b.Services.Events);
                b.Register(new ProbeSystem(7));
            }

            rig.Host = b.Build();
            return rig;
        }

        public int Pop(uint node)
        {
            return Flow.Population(new NodeId(node));
        }

        public CohortId InjectNow(uint source, int count, ulong flight, ContentId? pax = null)
        {
            return Flow.Inject(FlowKit.Key(flight, pax), count, new NodeId(source));
        }

        public void Step(uint ticks = 1)
        {
            Host.Step(ticks);
        }

        /// <summary>The tick the next Step(1) executes (08 §8.9: ticks executed).</summary>
        public ulong NextTick => Host.CurrentTick;

        public bool Submit(uint node, int count, out CommandRejection reason)
        {
            var cmd = new Command(Host.CurrentTick + SimConstants.COMMAND_MIN_LEAD_TICKS, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, FlowKit.SetServersOpenPayload(node, count));
            return Host.TrySubmit(cmd, out reason);
        }

        public List<PassengerCohort> CohortsOn(uint node)
        {
            var list = new List<PassengerCohort>();
            IReadOnlyList<CohortId> ids = Flow.CohortsAt(new NodeId(node));
            for (int i = 0; i < ids.Count; i++)
            {
                if (!Flow.TryGetCohort(ids[i], out PassengerCohort c))
                {
                    throw new InvalidOperationException("CohortsAt lists unknown cohort " + ids[i].Value);
                }

                list.Add(c);
            }

            return list;
        }

        /// <summary>
        /// Checks the invariants 09 states that hold after every tick: every
        /// cohort has Count > 0, sits on the node that lists it, CohortsAt is
        /// ascending, node population equals the sum of its cohorts, and no two
        /// cohorts on a node share a CohortKey (and, on a Corridor, a DueAt)
        /// unless one is in an open blocking
        /// episode (merge is mandatory, §9.3; blocked cohorts do not merge, §9.12).
        /// Returns the total head count.
        /// </summary>
        public int CheckInvariants(string where, ISet<ulong> openEpisodes)
        {
            int total = 0;
            IReadOnlyList<NodeId> nodes = World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                IReadOnlyList<CohortId> ids = Flow.CohortsAt(nodes[i]);
                int sum = 0;
                bool corridor = Corridors.Contains(nodes[i].Value);
                var keys = new HashSet<(CohortKey, ulong)>();
                for (int k = 0; k < ids.Count; k++)
                {
                    if (k > 0 && ids[k - 1].Value >= ids[k].Value)
                    {
                        throw new Xunit.Sdk.XunitException(where + ": CohortsAt(" + nodes[i].Value + ") not strictly ascending");
                    }

                    if (!Flow.TryGetCohort(ids[k], out PassengerCohort c))
                    {
                        throw new Xunit.Sdk.XunitException(where + ": unknown cohort " + ids[k].Value);
                    }

                    if (c.Count <= 0 || c.Node != nodes[i] || c.Id != ids[k])
                    {
                        throw new Xunit.Sdk.XunitException(where + ": bad cohort " + ids[k].Value + " count " + c.Count + " node " + c.Node.Value);
                    }

                    if (c.ServiceCredit != Fx.Zero)
                    {
                        throw new Xunit.Sdk.XunitException(where + ": cohort " + ids[k].Value + " carries ServiceCredit (Q-032: always zero)");
                    }

                    if (!corridor && c.DueAt != c.EnteredNodeAt)
                    {
                        throw new Xunit.Sdk.XunitException(where + ": cohort " + ids[k].Value + " off a Corridor has DueAt != EnteredNodeAt (Q-033)");
                    }

                    // Corridor cohorts of one key merge only with equal DueAt (Q-033).
                    if (!openEpisodes.Contains(c.Id.Value) && !keys.Add((c.Key, corridor ? c.DueAt : 0UL)))
                    {
                        throw new Xunit.Sdk.XunitException(where + ": two unblocked cohorts with one key on node " + nodes[i].Value);
                    }

                    sum += c.Count;
                }

                if (sum != Flow.Population(nodes[i]))
                {
                    throw new Xunit.Sdk.XunitException(where + ": Population(" + nodes[i].Value + ") " + Flow.Population(nodes[i]) + " != cohort sum " + sum);
                }

                total += sum;
            }

            return total;
        }
    }

    /// <summary>
    /// An independent model of one Queue node's arithmetic (09 §9.4, exact per
    /// §9.12, Q-032), fed with the ticks at which passengers arrive, for a
    /// downstream node that is never full. It also applies the threshold rule.
    /// </summary>
    internal sealed class QueueModel
    {
        public static readonly Fx Epsilon = Fx.FromRatio(1, 1000);

        private readonly Fx _rate;
        private readonly int _serverCount;
        private readonly Fx _threshold;
        private readonly Fx _hysteresis;
        private readonly List<(ulong EnteredAt, int Count)> _waiting = new List<(ulong, int)>();

        public QueueModel(Fx rate, int serverCount, int open, Fx threshold, Fx hysteresis)
        {
            _rate = rate;
            _serverCount = serverCount;
            Open = open;
            _threshold = threshold;
            _hysteresis = hysteresis;
        }

        public int Open;
        public Fx Credit = Fx.Zero;
        public bool Flag;
        public readonly List<string> Events = new List<string>();

        public int Population
        {
            get
            {
                int p = 0;
                foreach (var w in _waiting)
                {
                    p += w.Count;
                }

                return p;
            }
        }

        public void Arrive(ulong tick, int count)
        {
            _waiting.Add((tick, count));
        }

        /// <summary>Runs tick t; returns the passengers moved on.</summary>
        public int Step(ulong t)
        {
            Fx capacity = Fx.Div(Fx.Mul(Fx.FromInt(Open * SimConstants.SIM_SECONDS_PER_TICK), _rate), Fx.FromInt(60));
            Fx serverTick = Fx.Div(Fx.Mul(Fx.FromInt(SimConstants.SIM_SECONDS_PER_TICK), _rate), Fx.FromInt(60));
            Credit = Fx.Add(Credit, capacity);
            long served = Fx.Floor(Credit);
            Credit = Fx.Sub(Credit, Fx.FromInt(served));

            long moved = 0;
            for (int i = 0; i < _waiting.Count && moved < served;)
            {
                if (_waiting[i].EnteredAt >= t)
                {
                    i++;
                    continue;
                }

                long take = Math.Min(served - moved, _waiting[i].Count);
                moved += take;
                int left = _waiting[i].Count - (int)take;
                if (left == 0)
                {
                    _waiting.RemoveAt(i);
                }
                else
                {
                    _waiting[i] = (_waiting[i].EnteredAt, left);
                    i++;
                }
            }

            if (moved < served)
            {
                Credit = Fx.Min(Credit, serverTick);
            }

            Fx w = Wait();
            if (!Flag && w > _threshold)
            {
                Flag = true;
                Events.Add(t + " exceeded w" + w.Raw + " " + Open + "/" + _serverCount);
            }
            else if (Flag && w < Fx.Sub(_threshold, _hysteresis))
            {
                Flag = false;
                Events.Add(t + " cleared w" + w.Raw + " " + Open + "/" + _serverCount);
            }

            return (int)moved;
        }

        public Fx Wait()
        {
            Fx perMinute = Fx.Mul(Fx.FromInt(Open), _rate);
            return Fx.Div(Fx.FromInt(Population), Fx.Max(perMinute, Epsilon));
        }
    }

    internal static class Graphs
    {
        /// <summary>Source 1 -> Queue 2 -> Gate 3 -> Sink 4, every length 0.</summary>
        public static TestGraph Line(int servers, int open, ContentId profile)
        {
            return new TestGraph()
                .Node(1, "source")
                .Queue(2, servers, open, profile)
                .Node(3, "gate")
                .Node(4, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4);
        }

        /// <summary>Source 1 -> Queue 2 -> Queue 3 -> Gate 4 -> Sink 5: a queue feeding a queue.</summary>
        public static TestGraph TwoQueues(ContentId upstream, int upOpen, ContentId downstream, int downOpen)
        {
            return new TestGraph()
                .Node(1, "source")
                .Queue(2, 4, upOpen, upstream)
                .Queue(3, 4, downOpen, downstream)
                .Node(4, "gate")
                .Node(5, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4).Edge(4, 5);
        }

        /// <summary>Content with the test walker (1 m/s) and the given queue profiles.</summary>
        public static IContentIndex Content(params QueueProfileDefinition[] queues)
        {
            var defs = new List<IContentDefinition> { FlowKit.Pax(FlowKit.Walker, Fx.One) };
            foreach (QueueProfileDefinition q in queues)
            {
                defs.Add(q);
            }

            return ContentIndexFactory.Create(defs);
        }

        public static QueueProfileDefinition Lane(Fx rate, int capacity = 100000, long thresholdMinutes = 1000000, long hysteresisMinutes = 0)
        {
            return FlowKit.Queue(FlowKit.Lane, rate, capacity, Fx.FromInt(thresholdMinutes), Fx.FromInt(hysteresisMinutes));
        }

        public static QueueProfileDefinition Lane(Fx rate, Fx threshold, Fx hysteresis)
        {
            return FlowKit.Queue(FlowKit.Lane, rate, 100000, threshold, hysteresis);
        }

        /// <summary>§9.12's traversalTicks, for expected values and cohort ceilings.</summary>
        public static long TraversalTicks(uint lengthMetres, Fx walkSpeedMps)
        {
            return System.Math.Max(1, Fx.Ceil(Fx.Div(Fx.FromInt(lengthMetres), Fx.Mul(walkSpeedMps, Fx.FromInt(SimConstants.SIM_SECONDS_PER_TICK)))));
        }

        /// <summary>
        /// The live-cohort ceiling of a fixture (09 §9.10: fixture sizing, set by
        /// the Test Author, Q-033), derived from the merge rules. Once merged
        /// (§9.3), an unblocked key has at most one cohort per non-Corridor node.
        /// On a Corridor, cohorts of one key merge only with an equal DueAt
        /// (Q-033). A cohort enters at most once per tick and stays traversalTicks
        /// ticks, so a Corridor holds at most traversalTicks cohorts per key.
        /// Blocked cohorts never merge, so each open episode adds at most one.
        /// The ceiling is (sum over nodes of that per-key bound) x live keys +
        /// open episodes.
        /// </summary>
        public static int CohortCeiling(IWorldSystem world, ISet<uint> corridors, Fx walkSpeedMps, int liveKeys, int openEpisodes)
        {
            long perKey = 0;
            foreach (NodeId n in world.Nodes())
            {
                perKey += corridors.Contains(n.Value) ? TraversalTicks(world.LengthMetres(n), walkSpeedMps) : 1;
            }

            return (int)(perKey * liveKeys) + openEpisodes;
        }

        /// <summary>§9.12's PredictedWaitMinutes formula, for expected values.</summary>
        public static Fx Wait(int population, int open, Fx rate)
        {
            return Fx.Div(Fx.FromInt(population), Fx.Max(Fx.Mul(Fx.FromInt(open), rate), QueueModel.Epsilon));
        }
    }
}
