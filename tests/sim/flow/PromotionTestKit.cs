using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    // Test doubles and rigs for the T-010 promotion suite, written from
    // 09-interfaces-flow.md (§9.1, §9.7, §9.10, §9.11, §9.12), 18 §18.2-§18.4,
    // 01 "Hierarchical simulation" and 02 rule 8. Every type is prefixed
    // "Promo" so it cannot collide with the T-007 suite in the same project.

    internal static class PromoConst
    {
        public const ulong TicksPerDay = 14400UL;
        public const ushort WorldId = 1;
        public const ushort InjectorId = 2;
        public const ushort AbsorberId = 3;
        public const ushort FlowId = 4;
        public const ushort AfterFlowId = 5;
        public const ushort RecorderId = 7;
        public const uint Gate = 8;
        public const uint Sink = 9;
        public static readonly uint[] AllNodes = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
    }

    /// <summary>
    /// The test landside: two Sources (1, 2) into a corridor (3), a long
    /// corridor (4) that splits into two security Queues (5, 6), which
    /// rejoin into a corridor (7) to the single Gate (8) and its Sink (9).
    /// </summary>
    internal static class PromoGraphs
    {
        public const string World =
            "{\n" +
            "  \"schema_version\": 1,\n" +
            "  \"nodes\": [\n" +
            "    { \"id\": 1, \"length_metres\": 0 },\n" +
            "    { \"id\": 2, \"length_metres\": 0 },\n" +
            "    { \"id\": 3, \"length_metres\": 30 },\n" +
            "    { \"id\": 4, \"length_metres\": 120 },\n" +
            "    { \"id\": 5, \"length_metres\": 10 },\n" +
            "    { \"id\": 6, \"length_metres\": 10 },\n" +
            "    { \"id\": 7, \"length_metres\": 80 },\n" +
            "    { \"id\": 8, \"length_metres\": 20 },\n" +
            "    { \"id\": 9, \"length_metres\": 0 }\n" +
            "  ],\n" +
            "  \"edges\": [\n" +
            "    { \"id\": 1, \"from\": 1, \"to\": 3 },\n" +
            "    { \"id\": 2, \"from\": 2, \"to\": 3 },\n" +
            "    { \"id\": 3, \"from\": 3, \"to\": 4 },\n" +
            "    { \"id\": 4, \"from\": 4, \"to\": 5 },\n" +
            "    { \"id\": 5, \"from\": 4, \"to\": 6 },\n" +
            "    { \"id\": 6, \"from\": 5, \"to\": 7 },\n" +
            "    { \"id\": 7, \"from\": 6, \"to\": 7 },\n" +
            "    { \"id\": 8, \"from\": 7, \"to\": 8 },\n" +
            "    { \"id\": 9, \"from\": 8, \"to\": 9 }\n" +
            "  ]\n" +
            "}\n";

        public static string Flow(int serverCount, int open5, int open6, string profile)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{{\n  \"schema_version\": 1,\n  \"nodes\": [\n" +
                "    {{ \"id\": 1, \"kind\": \"source\" }},\n" +
                "    {{ \"id\": 2, \"kind\": \"source\" }},\n" +
                "    {{ \"id\": 3, \"kind\": \"corridor\" }},\n" +
                "    {{ \"id\": 4, \"kind\": \"corridor\" }},\n" +
                "    {{ \"id\": 5, \"kind\": \"queue\", \"server_count\": {0}, \"servers_open\": {1}, \"queue_profile\": \"{3}\" }},\n" +
                "    {{ \"id\": 6, \"kind\": \"queue\", \"server_count\": {0}, \"servers_open\": {2}, \"queue_profile\": \"{3}\" }},\n" +
                "    {{ \"id\": 7, \"kind\": \"corridor\" }},\n" +
                "    {{ \"id\": 8, \"kind\": \"gate\" }},\n" +
                "    {{ \"id\": 9, \"kind\": \"sink\" }}\n" +
                "  ]\n}}\n",
                serverCount,
                open5,
                open6,
                profile);
        }

        /// <summary>Tight security: two lanes of three open, small capacity, so queues, spillback and thresholds all happen.</summary>
        public static readonly string Congested = Flow(3, 1, 2, "security_tight");

        /// <summary>Max-tier throughput for the budget run.</summary>
        public static readonly string Wide = Flow(20, 20, 20, "security_wide");

        public static byte[] Utf8(string s)
        {
            return new UTF8Encoding(false).GetBytes(s);
        }
    }

    internal static class PromoContent
    {
        public static readonly ContentId Business = new ContentId("business");
        public static readonly ContentId Leisure = new ContentId("leisure");

        public static IContentIndex Index()
        {
            var defs = new List<IContentDefinition>
            {
                new PaxProfileDefinition(Business, Fx.FromRatio(13, 10), new[] { new ShowUpBucket(60U, 1000U) }),
                new PaxProfileDefinition(Leisure, Fx.FromRatio(1, 1), new[] { new ShowUpBucket(90U, 1000U) }),
                new QueueProfileDefinition(new ContentId("security_tight"), Fx.FromRatio(5, 2), 60, Fx.FromInt(8), Fx.FromInt(2), DelayCategory.SecurityQueue),
                new QueueProfileDefinition(new ContentId("security_wide"), Fx.FromRatio(5, 2), 400, Fx.FromInt(30), Fx.FromInt(5), DelayCategory.SecurityQueue),
            };
            return ContentIndexFactory.Create(defs);
        }
    }

    /// <summary>The pinned SplitMix64 of 08 §8.8, as 07 L4 requires for test inputs.</summary>
    internal sealed class PromoSplitMix
    {
        private ulong _x;

        public PromoSplitMix(ulong seed)
        {
            _x = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _x += 0x9E3779B97F4A7C15UL;
                ulong z = _x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public int Below(int n)
        {
            return (int)(Next() % (ulong)n);
        }
    }

    /// <summary>
    /// A daily injection and boarding plan, repeated every sim-day. Flight
    /// f on day d has id d * 10000 + f + 1. Injections are sorted by tick of
    /// day; boarding (Absorb) happens at the flight's STD.
    /// </summary>
    internal sealed class PromoPlan
    {
        public readonly int[] InjTod;
        public readonly int[] InjFlight;
        public readonly uint[] InjNode;
        public readonly int[] InjCount;
        public readonly CohortKeyParts[] InjKey;
        public readonly int[] AbsorbTod;
        public readonly int[] AbsorbFlight;
        public readonly long DailyPax;

        public readonly struct CohortKeyParts
        {
            public CohortKeyParts(ContentId profile, bool bag, bool assist)
            {
                Profile = profile;
                Bag = bag;
                Assist = assist;
            }

            public ContentId Profile { get; }

            public bool Bag { get; }

            public bool Assist { get; }
        }

        public PromoPlan(ulong seed, int flights, int cohortsPerFlight, int maxCohort)
        {
            var rng = new PromoSplitMix(seed);
            var inj = new List<(int Tod, int Flight, uint Node, int Count, CohortKeyParts Key)>();
            var abs = new List<(int Tod, int Flight)>();
            for (int f = 0; f < flights; f++)
            {
                int std = 3000 + rng.Below(11000);
                abs.Add((std, f));
                for (int c = 0; c < cohortsPerFlight; c++)
                {
                    int tod = std - 300 - rng.Below(2400);
                    var key = new CohortKeyParts(rng.Below(2) == 0 ? PromoContent.Business : PromoContent.Leisure, rng.Below(3) != 0, rng.Below(20) == 0);
                    int count = 1 + rng.Below(maxCohort);
                    inj.Add((tod, f, 1U + (uint)rng.Below(2), count, key));
                    DailyPax += count;
                }
            }

            inj.Sort((a, b) => a.Tod != b.Tod ? a.Tod.CompareTo(b.Tod) : a.Flight != b.Flight ? a.Flight.CompareTo(b.Flight) : a.Count.CompareTo(b.Count));
            abs.Sort((a, b) => a.Tod != b.Tod ? a.Tod.CompareTo(b.Tod) : a.Flight.CompareTo(b.Flight));
            InjTod = inj.ConvertAll(x => x.Tod).ToArray();
            InjFlight = inj.ConvertAll(x => x.Flight).ToArray();
            InjNode = inj.ConvertAll(x => x.Node).ToArray();
            InjCount = inj.ConvertAll(x => x.Count).ToArray();
            InjKey = inj.ConvertAll(x => x.Key).ToArray();
            AbsorbTod = abs.ConvertAll(x => x.Tod).ToArray();
            AbsorbFlight = abs.ConvertAll(x => x.Flight).ToArray();
        }

        public static FlightId FlightOf(ulong day, int f)
        {
            return new FlightId((day * 10000UL) + (ulong)f + 1UL);
        }

        /// <summary>The standard congested day: 60 flights, 4 cohorts each of 1..40.</summary>
        public static PromoPlan Standard()
        {
            return new PromoPlan(0x0010_5EEDUL, 60, 4, 40);
        }
    }

    /// <summary>Calls IFlowSystem.Inject from registry position 2, as sim.schedule would (09 §9.7).</summary>
    internal sealed class PromoInjector : ISimSystem
    {
        private readonly PromoPlan _plan;
        private int _next;
        public IFlowSystem? Flow;
        public long Injected;

        public PromoInjector(PromoPlan plan)
        {
            _plan = plan;
        }

        public SystemId Id => new SystemId(PromoConst.InjectorId);

        public string Name => "probe.injector";

        public void Tick(in TickContext ctx)
        {
            ulong day = ctx.Tick / PromoConst.TicksPerDay;
            int tod = (int)(ctx.Tick % PromoConst.TicksPerDay);
            if (tod == 0)
            {
                _next = 0;
            }

            while (_next < _plan.InjTod.Length && _plan.InjTod[_next] == tod)
            {
                PromoPlan.CohortKeyParts k = _plan.InjKey[_next];
                var key = new CohortKey(PromoPlan.FlightOf(day, _plan.InjFlight[_next]), FlowDirection.Departing, k.Profile, k.Bag, k.Assist);
                Flow!.Inject(key, _plan.InjCount[_next], new NodeId(_plan.InjNode[_next]));
                Injected += _plan.InjCount[_next];
                _next++;
            }
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    /// <summary>Calls IFlowSystem.Absorb from registry position 3, as sim.airside would, at each STD.</summary>
    internal sealed class PromoAbsorber : ISimSystem
    {
        private readonly PromoPlan _plan;
        private int _next;
        public IFlowSystem? Flow;
        public long Absorbed;

        public PromoAbsorber(PromoPlan plan)
        {
            _plan = plan;
        }

        public SystemId Id => new SystemId(PromoConst.AbsorberId);

        public string Name => "probe.absorber";

        public void Tick(in TickContext ctx)
        {
            ulong day = ctx.Tick / PromoConst.TicksPerDay;
            int tod = (int)(ctx.Tick % PromoConst.TicksPerDay);
            if (tod == 0)
            {
                _next = 0;
            }

            while (_next < _plan.AbsorbTod.Length && _plan.AbsorbTod[_next] == tod)
            {
                Absorbed += Flow!.Absorb(new NodeId(PromoConst.Sink), PromoPlan.FlightOf(day, _plan.AbsorbFlight[_next]));
                _next++;
            }
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    internal delegate void PromoTickHook(in TickContext ctx, IFlowSystem flow);

    /// <summary>
    /// Registry position 5, right after sim.flow: optionally calls SetPromoted
    /// inside the tick.
    /// </summary>
    internal sealed class PromoAfterFlow : ISimSystem
    {
        public IFlowSystem? Flow;
        public PromoTickHook? Hook;

        public SystemId Id => new SystemId(PromoConst.AfterFlowId);

        public string Name => "probe.afterflow";

        public void Tick(in TickContext ctx)
        {
            Hook?.Invoke(ctx, Flow!);
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    internal sealed class PromoProbe : ISimSystem
    {
        public PromoProbe(ushort id)
        {
            Id = new SystemId(id);
        }

        public SystemId Id { get; }

        public string Name => "probe";

        public void Tick(in TickContext ctx)
        {
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    internal sealed class PromoNullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class PromoCheckpoints : ICheckpointSink
    {
        public readonly List<string> Lines = new List<string>();
        public readonly bool Recording;

        public PromoCheckpoints(bool recording = true)
        {
            Recording = recording;
        }

        public void Record(in Checkpoint cp)
        {
            if (!Recording)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("t=").Append(cp.Tick).Append(" world=").Append(cp.WorldHash.ToString("X16", CultureInfo.InvariantCulture))
              .Append(" core=").Append(cp.CoreHash.ToString("X16", CultureInfo.InvariantCulture)).Append(" [");
            foreach (ulong h in cp.SystemHashes)
            {
                sb.Append(h.ToString("X16", CultureInfo.InvariantCulture)).Append(',');
            }

            Lines.Add(sb.Append(']').ToString());
        }
    }

    /// <summary>Records every sim.flow event (10 §10.9) as text, from position 7.</summary>
    internal sealed class PromoEventLog
    {
        public readonly List<string> Lines = new List<string>();

        public PromoEventLog(IEventBus bus)
        {
            var id = new SystemId(PromoConst.RecorderId);
            List<string> l = Lines;
            bus.Subscribe<FlowBlocked>(id, (in EventEnvelope e, in FlowBlocked x, in TickContext c) =>
                l.Add(Env(e) + " FlowBlocked c=" + x.Cohort.Value + " held=" + x.Held.Value + " by=" + x.BlockedBy.Value));
            bus.Subscribe<FlowUnblocked>(id, (in EventEnvelope e, in FlowUnblocked x, in TickContext c) =>
                l.Add(Env(e) + " FlowUnblocked c=" + x.Cohort.Value + " held=" + x.Held.Value + " by=" + x.BlockedBy.Value));
            bus.Subscribe<QueueThresholdExceeded>(id, (in EventEnvelope e, in QueueThresholdExceeded x, in TickContext c) =>
                l.Add(Env(e) + " QTE n=" + x.Node.Value + " w=" + x.WaitMinutes.Raw + " open=" + x.ServersOpen + "/" + x.ServerCount));
            bus.Subscribe<QueueThresholdCleared>(id, (in EventEnvelope e, in QueueThresholdCleared x, in TickContext c) =>
                l.Add(Env(e) + " QTC n=" + x.Node.Value + " w=" + x.WaitMinutes.Raw + " open=" + x.ServersOpen + "/" + x.ServerCount));
            bus.Subscribe<PassengersArrivedAtGate>(id, (in EventEnvelope e, in PassengersArrivedAtGate x, in TickContext c) =>
                l.Add(Env(e) + " Arrived f=" + x.Flight.Value + " n=" + x.Count));
            bus.Subscribe<PassengersMissedFlight>(id, (in EventEnvelope e, in PassengersMissedFlight x, in TickContext c) =>
                l.Add(Env(e) + " Missed f=" + x.Flight.Value + " n=" + x.Count + " last=" + x.LastBlockedAt.Value));
        }

        private static string Env(in EventEnvelope e)
        {
            string cause = e.Cause.HasValue ? e.Cause.Id.Tick + "." + e.Cause.Id.Sequence : "-";
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1} src={2} cause={3}", e.Id.Tick, e.Id.Sequence, e.Source.Value, cause);
        }
    }

    /// <summary>
    /// sim.world (1) + injector (2) + absorber (3) + sim.flow (4) + a probe
    /// after flow (5) + optionally an event recorder (7), in one host.
    /// </summary>
    internal sealed class PromoRig
    {
        public readonly ISimHost Host;
        public readonly IFlowSystem Flow;
        public readonly IWorldSystem World;
        public readonly PromoInjector Injector;
        public readonly PromoAbsorber Absorber;
        public readonly PromoAfterFlow AfterFlow = new PromoAfterFlow();
        public readonly PromoCheckpoints Checkpoints;
        public readonly PromoEventLog? Events;

        /// <param name="clock">
        /// When given, sim.flow is built with the clock's shimmed services and
        /// registered behind <see cref="TimedSystem"/> (03 Q-064), for a budget test.
        /// </param>
        public PromoRig(PromoPlan plan, string? flowJson = null, bool record = true, ulong seed = 0x5EED_0010UL, bool recordCheckpoints = true, FlowClock? clock = null)
        {
            Checkpoints = new PromoCheckpoints(recordCheckpoints);
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, PromoContent.Index(), Checkpoints, new PromoNullLog()));
            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(PromoGraphs.Utf8(PromoGraphs.World), "promo-world.json");
            World = WorldFactory.CreateSystem(b.Services, walk);
            FlowGraph graph = FlowFactory.CreateGraphLoader().Load(PromoGraphs.Utf8(flowJson ?? PromoGraphs.Congested), "promo.flow.json", World);
            Flow = FlowFactory.CreateSystem(clock == null ? b.Services : clock.Shim(b.Services), graph, World);
            Injector = new PromoInjector(plan) { Flow = Flow };
            Absorber = new PromoAbsorber(plan) { Flow = Flow };
            AfterFlow.Flow = Flow;
            b.Register(World);
            b.Register(Injector);
            b.Register(Absorber);
            b.Register(clock == null ? Flow : new TimedSystem(Flow, clock));
            b.Register(AfterFlow);
            if (record)
            {
                Events = new PromoEventLog(b.Services.Events);
                b.Register(new PromoProbe(PromoConst.RecorderId));
            }

            Host = b.Build();
        }

        public void SetAll(bool promoted)
        {
            foreach (uint n in PromoConst.AllNodes)
            {
                Flow.SetPromoted(new NodeId(n), promoted);
            }
        }

        /// <summary>Every cohort on every node, in NodeId then CohortId order, with every field (09 §9.2).</summary>
        public string DescribeCohorts()
        {
            var sb = new StringBuilder();
            foreach (uint n in PromoConst.AllNodes)
            {
                var node = new NodeId(n);
                sb.Append('n').Append(n).Append(" pop=").Append(Flow.Population(node)).Append(" wait=").Append(Flow.PredictedWaitMinutes(node).Raw).Append(':');
                foreach (CohortId id in Flow.CohortsAt(node))
                {
                    Assert.True(Flow.TryGetCohort(id, out PassengerCohort c), "TryGetCohort false for listed cohort " + id.Value);
                    sb.Append(" [").Append(Describe(c)).Append(']');
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        public static string Describe(in PassengerCohort c)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "id={0} f={1} dir={2} prof={3} bag={4} asst={5} node={6} n={7} in={8} due={9} credit={10}",
                c.Id.Value,
                c.Key.Flight.Value,
                c.Key.Direction,
                c.Key.PaxProfile.Value,
                c.Key.HasHoldBaggage,
                c.Key.RequiresAssistance,
                c.Node.Value,
                c.Count,
                c.EnteredNodeAt,
                c.DueAt,
                c.ServiceCredit.Raw);
        }

        public int TotalPopulation()
        {
            int total = 0;
            foreach (uint n in PromoConst.AllNodes)
            {
                total += Flow.Population(new NodeId(n));
            }

            return total;
        }
    }

    /// <summary>What one run observed, for comparing a promoted run with a headless one.</summary>
    internal sealed class PromoTrace
    {
        public readonly List<string> Snapshots = new List<string>();
        public List<string> Checkpoints = new List<string>();
        public List<string> Events = new List<string>();
        public ulong FinalWorldHash;
        public ulong FinalFlowHash;
        public int MaxTotalPopulation;

        /// <summary>
        /// Runs <paramref name="ticks"/> ticks, calling <paramref name="between"/>
        /// before each Step, and snapshotting all cohort state after each Step
        /// that crosses a 600-tick boundary. Runs compared with each other use
        /// the same step size, so they snapshot at the same ticks.
        /// </summary>
        public static PromoTrace Run(PromoRig rig, ulong ticks, uint stepSize, Action<ulong>? between = null)
        {
            var trace = new PromoTrace();
            while (rig.Host.CurrentTick < ticks)
            {
                between?.Invoke(rig.Host.CurrentTick);
                ulong before = rig.Host.CurrentTick;
                ulong left = ticks - before;
                rig.Host.Step((uint)Math.Min(stepSize, left));
                ulong last = rig.Host.CurrentTick - 1UL;
                if (before / 600UL != rig.Host.CurrentTick / 600UL)
                {
                    trace.Snapshots.Add("t=" + last + "\n" + rig.DescribeCohorts());
                    trace.MaxTotalPopulation = Math.Max(trace.MaxTotalPopulation, rig.TotalPopulation());
                }
            }

            trace.Checkpoints = new List<string>(rig.Checkpoints.Lines);
            trace.Events = rig.Events != null ? new List<string>(rig.Events.Lines) : new List<string>();
            trace.FinalWorldHash = rig.Host.WorldStateHash();
            trace.FinalFlowHash = rig.Flow.ComputeStateHash();
            return trace;
        }

        public static void AssertSame(PromoTrace headless, PromoTrace promoted)
        {
            Assert.True(headless.MaxTotalPopulation > 0, "the scenario must put passengers on the graph");
            Assert.True(headless.Events.Count > 0, "the scenario must produce flow events");
            Assert.Equal(headless.Checkpoints.Count, promoted.Checkpoints.Count);
            for (int i = 0; i < headless.Checkpoints.Count; i++)
            {
                Assert.True(headless.Checkpoints[i] == promoted.Checkpoints[i], "checkpoint " + i + " differs:\n  headless " + headless.Checkpoints[i] + "\n  promoted " + promoted.Checkpoints[i]);
            }

            Assert.Equal(headless.Snapshots.Count, promoted.Snapshots.Count);
            for (int i = 0; i < headless.Snapshots.Count; i++)
            {
                Assert.True(headless.Snapshots[i] == promoted.Snapshots[i], "cohort state differs at snapshot " + i + ":\nheadless\n" + headless.Snapshots[i] + "promoted\n" + promoted.Snapshots[i]);
            }

            Assert.Equal(headless.Events.Count, promoted.Events.Count);
            for (int i = 0; i < headless.Events.Count; i++)
            {
                Assert.True(headless.Events[i] == promoted.Events[i], "event " + i + " differs: " + headless.Events[i] + " vs " + promoted.Events[i]);
            }

            Assert.Equal(headless.FinalFlowHash, promoted.FinalFlowHash);
            Assert.Equal(headless.FinalWorldHash, promoted.FinalWorldHash);
        }
    }

    internal static class PromoAgents
    {
        /// <summary>The refs a promoted node must show: every (cohort, index) with 0 ≤ index &lt; Count (09 §9.1 item 4).</summary>
        public static SortedSet<(ulong Cohort, int Index)> Expected(IFlowSystem flow, NodeId node)
        {
            var set = new SortedSet<(ulong, int)>();
            foreach (CohortId id in flow.CohortsAt(node))
            {
                Assert.True(flow.TryGetCohort(id, out PassengerCohort c));
                for (int i = 0; i < c.Count; i++)
                {
                    set.Add((id.Value, i));
                }
            }

            return set;
        }

        public static SortedSet<(ulong Cohort, int Index)> Refs(IReadOnlyList<AgentView> views)
        {
            var set = new SortedSet<(ulong, int)>();
            foreach (AgentView v in views)
            {
                Assert.True(set.Add((v.Ref.Cohort.Value, v.Ref.Index)), "duplicate PassengerRef " + v.Ref.Cohort.Value + "/" + v.Ref.Index);
            }

            return set;
        }

        public static string Describe(IReadOnlyList<AgentView> views)
        {
            var parts = new List<string>(views.Count);
            foreach (AgentView v in views)
            {
                parts.Add(v.Ref.Cohort.Value + "/" + v.Ref.Index + "@" + v.Node.Value + ":" + v.ProgressAlongEdge.Raw);
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(" ", parts);
        }
    }
}
