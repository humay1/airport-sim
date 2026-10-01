using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    // Test doubles and rigs. sim.core publishes no null IContentIndex,
    // ICheckpointSink or ISimLog (08 §8.11a), so tests supply their own.

    /// <summary>
    /// A stand-in IFlowSystem (09 §9.7) registered at position 4, after
    /// sim.airside. It learns the tick from its own Tick, so a call from
    /// sim.airside during tick t sees NextTick == t (12 §12.8: sim.airside
    /// sees sim.flow as of the end of the previous tick). It never publishes.
    /// </summary>
    internal abstract class FakeFlowBase : IFlowSystem
    {
        public ulong NextTick;
        public long InjectCalls;
        public long AbsorbCalls;
        public long OutstandingQueries;

        public SystemId Id => new SystemId(AirConst.FlowSystemId);

        public string Name => "probe.flow";

        public void Tick(in TickContext ctx)
        {
            NextTick = ctx.Tick + 1UL;
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }

        public int Population(NodeId node)
        {
            return 0;
        }

        public Fx PredictedWaitMinutes(NodeId node)
        {
            return Fx.Zero;
        }

        public int PopulationForFlight(FlightId flight, FlowDirection direction)
        {
            return 0;
        }

        public IReadOnlyList<CohortId> CohortsAt(NodeId node)
        {
            return Array.Empty<CohortId>();
        }

        public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
        {
            cohort = default;
            return false;
        }

        public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
        {
            OutstandingQueries++;
            return Answer(NextTick, flight, out outstanding);
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            lanes = default;
            return false;
        }

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            InjectCalls++;
            return new CohortId((ulong)InjectCalls);
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            AbsorbCalls++;
            OnAbsorb(NextTick, sink, flight);
            return 0;
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            return Array.Empty<AgentView>();
        }

        protected abstract bool Answer(ulong tick, FlightId flight, out OutstandingPassengers outstanding);

        protected virtual void OnAbsorb(ulong tick, NodeId sink, FlightId flight)
        {
        }
    }

    /// <summary>
    /// Answers TryGetOutstanding from a script over the ticks since the
    /// flight's first query. sim.airside first queries a departure at its
    /// doors-close point (12 §12.8), so "since first query" is "since the
    /// doors-close point". Records every query and Absorb.
    /// </summary>
    internal sealed class ScriptedFlow : FakeFlowBase
    {
        public readonly List<(ulong Tick, ulong Flight)> Queries = new List<(ulong Tick, ulong Flight)>();
        public readonly List<(ulong Tick, uint Sink, ulong Flight)> Absorbs = new List<(ulong Tick, uint Sink, ulong Flight)>();
        private readonly Dictionary<ulong, ulong> _first = new Dictionary<ulong, ulong>();
        private readonly Func<ulong, ulong, (int Count, uint Node)?> _script;

        /// <param name="script">(ticks since first query, flight) to outstanding (count, node), or null for none.</param>
        public ScriptedFlow(Func<ulong, ulong, (int Count, uint Node)?> script)
        {
            _script = script;
        }

        /// <summary>No passenger is ever outstanding.</summary>
        public static ScriptedFlow None()
        {
            return new ScriptedFlow((s, f) => null);
        }

        /// <summary>The given flight has (count, node) outstanding while fewer than <paramref name="ticks"/> ticks have passed; nobody else ever.</summary>
        public static ScriptedFlow For(ulong flight, ulong ticks, int count, uint node)
        {
            return new ScriptedFlow((s, f) => f == flight && s < ticks ? (count, node) : ((int, uint)?)null);
        }

        public bool TryFirstQuery(ulong flight, out ulong tick)
        {
            return _first.TryGetValue(flight, out tick);
        }

        public ulong FirstQuery(ulong flight)
        {
            Assert.True(_first.TryGetValue(flight, out ulong t), "sim.airside never called TryGetOutstanding for flight " + flight.ToString(CultureInfo.InvariantCulture));
            return t;
        }

        public List<ulong> QueryTicks(ulong flight)
        {
            return Queries.FindAll(q => q.Flight == flight).ConvertAll(q => q.Tick);
        }

        public List<(ulong Tick, uint Sink, ulong Flight)> AbsorbsOf(ulong flight)
        {
            return Absorbs.FindAll(a => a.Flight == flight);
        }

        protected override bool Answer(ulong tick, FlightId flight, out OutstandingPassengers outstanding)
        {
            Queries.Add((tick, flight.Value));
            if (!_first.TryGetValue(flight.Value, out ulong first))
            {
                first = tick;
                _first.Add(flight.Value, tick);
            }

            (int Count, uint Node)? o = _script(tick - first, flight.Value);
            if (o.HasValue)
            {
                outstanding = new OutstandingPassengers(flight, o.Value.Count, new NodeId(o.Value.Node));
                return true;
            }

            outstanding = default;
            return false;
        }

        protected override void OnAbsorb(ulong tick, NodeId sink, FlightId flight)
        {
            Absorbs.Add((tick, sink.Value, flight.Value));
        }
    }

    /// <summary>
    /// A stateless, allocation-free answer for day-long and max-tier runs: a
    /// flight has passengers outstanding iff (flight + tick / 150) % 3 != 0,
    /// so most departures are held and many time out. Records nothing.
    /// </summary>
    internal sealed class RuleFlow : FakeFlowBase
    {
        public static bool Outstanding(ulong tick, ulong flight)
        {
            return (flight + (tick / 150UL)) % 3UL != 0UL;
        }

        public static int Count(ulong flight)
        {
            return 1 + (int)(flight % 5UL);
        }

        public static uint Node(ulong flight)
        {
            return 500U + (uint)(flight % 3UL);
        }

        protected override bool Answer(ulong tick, FlightId flight, out OutstandingPassengers outstanding)
        {
            if (Outstanding(tick, flight.Value))
            {
                outstanding = new OutstandingPassengers(flight, Count(flight.Value), new NodeId(Node(flight.Value)));
                return true;
            }

            outstanding = default;
            return false;
        }
    }

    /// <summary>
    /// A probe at sim.turnaround's registry position 5 (08 §8.5 lets tests
    /// register probes at a legal position whose module is not in the build)
    /// that publishes scripted milestones during its own Tick.
    /// </summary>
    internal sealed class TurnaroundProbe : ISimSystem
    {
        public readonly List<(ulong Tick, ulong Flight, FlightMilestone Milestone)> Script = new List<(ulong Tick, ulong Flight, FlightMilestone Milestone)>();
        // Preallocated, so a script of up to 64 entries publishes without allocating.
        public readonly List<(ulong Flight, FlightMilestone Milestone, EventId Id)> Published = new List<(ulong Flight, FlightMilestone Milestone, EventId Id)>(64);

        public SystemId Id => new SystemId(AirConst.TurnaroundSystemId);

        public string Name => "probe.turnaround";

        public void Tick(in TickContext ctx)
        {
            foreach ((ulong tick, ulong flight, FlightMilestone m) in Script)
            {
                if (tick == ctx.Tick)
                {
                    EventId id = ctx.Events.Publish(new FlightMilestoneReached(new FlightId(flight), m, ctx.Tick, ctx.Tick), EventRef.None);
                    Published.Add((flight, m, id));
                }
            }
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    /// <summary>
    /// A stand-in for sim.turnaround at position 5, for the max-tier budget
    /// run with turnaroundRegistered true (T-021 "AirsideBudgetTests times
    /// handlers"). It answers each arrival's DoorsOpen with a DeboardComplete
    /// <see cref="DeboardTicks"/> later, and each departure's OnStand with a
    /// BoardingComplete <see cref="BoardingTicks"/> later, so rotations turn
    /// around and sim.airside's FlightMilestoneReached handler runs on both.
    /// Its own work is not sim.airside's and is not timed.
    /// </summary>
    internal sealed class StandInTurnaround : ISimSystem
    {
        public const ulong DeboardTicks = 100UL;
        public const ulong BoardingTicks = 250UL;

        private readonly List<(ulong Due, ulong Flight, FlightMilestone Milestone)> _due = new List<(ulong Due, ulong Flight, FlightMilestone Milestone)>(4096);
        public long Published;

        public SystemId Id => new SystemId(AirConst.TurnaroundSystemId);

        public string Name => "probe.turnaround";

        public void Subscribe(IEventBus bus)
        {
            var self = this;
            bus.Subscribe<FlightMilestoneReached>(Id, (in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx) =>
            {
                if (env.Source.Value != AirConst.AirsideSystemId)
                {
                    return;
                }

                // Only arrivals get InboundAirborne (12 §12.3), so an OnStand
                // for a flight never seen there is a departure's.
                if (evt.Milestone == FlightMilestone.InboundAirborne)
                {
                    self._arrivals.Add(evt.Flight.Value);
                }
                else if (evt.Milestone == FlightMilestone.DoorsOpen)
                {
                    // The arrival's OnStand has already come, so it can leave the set.
                    self._arrivals.Remove(evt.Flight.Value);
                    self._due.Add((ctx.Tick + DeboardTicks, evt.Flight.Value, FlightMilestone.DeboardComplete));
                }
                else if (evt.Milestone == FlightMilestone.OnStand && !self._arrivals.Contains(evt.Flight.Value))
                {
                    self._due.Add((ctx.Tick + BoardingTicks, evt.Flight.Value, FlightMilestone.BoardingComplete));
                }
            });
        }

        private readonly HashSet<ulong> _arrivals = new HashSet<ulong>();

        public void Tick(in TickContext ctx)
        {
            // Publish what is due and compact the rest in place: no per-tick closure.
            int kept = 0;
            for (int i = 0; i < _due.Count; i++)
            {
                if (_due[i].Due == ctx.Tick)
                {
                    ctx.Events.Publish(new FlightMilestoneReached(new FlightId(_due[i].Flight), _due[i].Milestone, ctx.Tick, ctx.Tick), EventRef.None);
                    Published++;
                }
                else if (_due[i].Due > ctx.Tick)
                {
                    _due[kept++] = _due[i];
                }
            }

            _due.RemoveRange(kept, _due.Count - kept);
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }
    }

    /// <summary>A system at a legal registry position whose module is absent (08 §8.5).</summary>
    internal sealed class ProbeSystem : ISimSystem
    {
        public ProbeSystem(ushort id)
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

    /// <summary>Counts lines by key without allocating.</summary>
    internal sealed class CountingLog : ISimLog
    {
        public int ReassignNoOps;
        public int Other;

        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
            if (key == LogKey.AirsideReassignStandNoOp)
            {
                ReassignNoOps++;
            }
            else
            {
                Other++;
            }
        }
    }

    /// <summary>The handler time of the current tick, summed by the shims below.</summary>
    internal sealed class HandlerTimer
    {
        public long Elapsed;
        public long Calls;
    }

    /// <summary>
    /// 03 "Timing a module's handlers" (Q-064): forwards to the real bus, and
    /// wraps each handler the module subscribes, at subscription, in a shim
    /// that adds the timestamp difference around the real handler to the
    /// timer. The shim is created once, during construction, and allocates
    /// nothing per call.
    /// </summary>
    internal sealed class TimingBus : IEventBus
    {
        private readonly IEventBus _real;
        private readonly HandlerTimer _timer;

        public TimingBus(IEventBus real, HandlerTimer timer)
        {
            _real = real;
            _timer = timer;
        }

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            return _real.Publish(evt, cause);
        }

        public void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
        {
            HandlerTimer timer = _timer;
            _real.Subscribe<T>(subscriber, (in EventEnvelope env, in T evt, in TickContext ctx) =>
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                handler(env, evt, ctx);
                timer.Elapsed += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                timer.Calls++;
            });
        }
    }

    /// <summary>The same for command handlers: Apply is timed, Validate (admission) is not (03).</summary>
    internal sealed class TimingRegistry : ICommandHandlerRegistry
    {
        private readonly ICommandHandlerRegistry _real;
        private readonly HandlerTimer _timer;

        public TimingRegistry(ICommandHandlerRegistry real, HandlerTimer timer)
        {
            _real = real;
            _timer = timer;
        }

        public void Register(SystemId owner, ICommandHandler handler)
        {
            _real.Register(owner, new Timed(handler, _timer));
        }

        private sealed class Timed : ICommandHandler
        {
            private readonly ICommandHandler _inner;
            private readonly HandlerTimer _timer;

            public Timed(ICommandHandler inner, HandlerTimer timer)
            {
                _inner = inner;
                _timer = timer;
            }

            public CommandKind Kind => _inner.Kind;

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                return _inner.Validate(payload);
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                _inner.Apply(cmd, ctx);
                _timer.Elapsed += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                _timer.Calls++;
            }
        }
    }

    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class CapturingLog : ISimLog
    {
        public readonly List<string> Lines = new List<string>();
        public readonly List<(ulong Tick, LogLevel Level, ushort System, LogKey Key, LogArgs Args)> Entries = new List<(ulong Tick, LogLevel Level, ushort System, LogKey Key, LogArgs Args)>();

        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
            Entries.Add((tick, level, system.Value, key, args));
            Lines.Add(string.Format(CultureInfo.InvariantCulture, "t={0} {1} s={2} k={3} n={4} {5} {6} {7} {8}", tick, level, system.Value, (int)key, args.Count, args.A0, args.A1, args.A2, args.A3));
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

    /// <summary>One recorded event, with sim.airside's track of its flight as seen by the handler (phase 3).</summary>
    internal sealed class Rec
    {
        public Rec(EventEnvelope env, object payload, ulong flight, bool hasTrack, AircraftTrack track)
        {
            Env = env;
            Payload = payload;
            Flight = flight;
            HasTrack = hasTrack;
            Track = track;
        }

        public EventEnvelope Env { get; }

        public object Payload { get; }

        public ulong Flight { get; }

        public bool HasTrack { get; }

        public AircraftTrack Track { get; }

        public ulong Tick => Env.Tick;

        public EventId Id => Env.Id;

        public bool FromAirside => Env.Source.Value == AirConst.AirsideSystemId;

        public FlightMilestoneReached Milestone => (FlightMilestoneReached)Payload;

        public bool IsMilestone(FlightMilestone m)
        {
            return Payload is FlightMilestoneReached ms && ms.Milestone == m;
        }

        public override string ToString()
        {
            string cause = Env.Cause.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", Env.Cause.Id.Tick, Env.Cause.Id.Sequence)
                : "-";
            string body;
            switch (Payload)
            {
                case FlightMilestoneReached m:
                    body = string.Format(CultureInfo.InvariantCulture, "milestone f={0} m={1} planned={2} actual={3}", m.Flight.Value, m.Milestone, m.PlannedTick, m.ActualTick);
                    break;
                case AircraftHeldForRunway h:
                    body = string.Format(CultureInfo.InvariantCulture, "rwyHeld f={0} r={1} q={2}", h.Flight.Value, h.Runway.Value, h.QueuePosition);
                    break;
                case AircraftHeldForRunwayReleased h:
                    body = string.Format(CultureInfo.InvariantCulture, "rwyReleased f={0} r={1} q={2}", h.Flight.Value, h.Runway.Value, h.QueuePosition);
                    break;
                case AircraftHeldOnTaxiway h:
                    body = string.Format(CultureInfo.InvariantCulture, "taxiHeld f={0} e={1} b={2}", h.Flight.Value, h.Edge.Value, Show.Opt(h.Blocking, x => x.Value));
                    break;
                case AircraftHeldOnTaxiwayReleased h:
                    body = string.Format(CultureInfo.InvariantCulture, "taxiReleased f={0} e={1} b={2}", h.Flight.Value, h.Edge.Value, Show.Opt(h.Blocking, x => x.Value));
                    break;
                case StandUnavailable s:
                    body = string.Format(CultureInfo.InvariantCulture, "standUnavailable f={0} s={1} o={2}", s.Flight.Value, Show.Opt(s.Stand, x => x.Value), Show.Opt(s.Occupying, x => x.Value));
                    break;
                case StandAssigned s:
                    body = string.Format(CultureInfo.InvariantCulture, "standAssigned f={0} s={1} o={2}", s.Flight.Value, Show.Opt(s.Stand, x => x.Value), Show.Opt(s.Occupying, x => x.Value));
                    break;
                case DepartureHeldForPassengers d:
                    body = string.Format(CultureInfo.InvariantCulture, "paxHeld f={0} n={1} at={2}", d.Flight.Value, d.Outstanding, Show.Opt(d.HeldAt, x => x.Value));
                    break;
                case DepartureHeldForPassengersReleased d:
                    body = string.Format(CultureInfo.InvariantCulture, "paxReleased f={0} n={1} at={2}", d.Flight.Value, d.Outstanding, Show.Opt(d.HeldAt, x => x.Value));
                    break;
                case PassengersMissedFlight p:
                    body = string.Format(CultureInfo.InvariantCulture, "missed f={0} n={1} at={2}", p.Flight.Value, p.Count, p.LastBlockedAt.Value);
                    break;
                default:
                    body = Payload.GetType().Name;
                    break;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "t={0} id={1}.{2} src={3} cause={4} {5} | {6}",
                Env.Tick, Env.Id.Tick, Env.Id.Sequence, Env.Source.Value, cause, body, HasTrack ? Show.Track(Track) : "untracked");
        }
    }

    /// <summary>
    /// A probe at position 7 that records every event sim.airside may emit
    /// (10 §10.6 "From sim.airside", §10.4) plus PassengersMissedFlight, and
    /// snapshots sim.airside's track of the event's flight in phase 3.
    /// </summary>
    internal sealed class Recorder
    {
        public readonly List<Rec> All = new List<Rec>();
        public IAirsideSystem? Airside;

        public Recorder(IEventBus bus, ushort subscriber)
        {
            var id = new SystemId(subscriber);
            Sub<FlightMilestoneReached>(bus, id, e => e.Flight);
            Sub<AircraftHeldForRunway>(bus, id, e => e.Flight);
            Sub<AircraftHeldForRunwayReleased>(bus, id, e => e.Flight);
            Sub<AircraftHeldOnTaxiway>(bus, id, e => e.Flight);
            Sub<AircraftHeldOnTaxiwayReleased>(bus, id, e => e.Flight);
            Sub<StandUnavailable>(bus, id, e => e.Flight);
            Sub<StandAssigned>(bus, id, e => e.Flight);
            Sub<DepartureHeldForPassengers>(bus, id, e => e.Flight);
            Sub<DepartureHeldForPassengersReleased>(bus, id, e => e.Flight);
            Sub<PassengersMissedFlight>(bus, id, e => e.Flight);
        }

        private void Sub<T>(IEventBus bus, SystemId id, Func<T, FlightId> flightOf) where T : struct, ISimEvent
        {
            bus.Subscribe<T>(id, (in EventEnvelope env, in T evt, in TickContext ctx) =>
            {
                FlightId f = flightOf(evt);
                AircraftTrack track = default;
                bool has = Airside != null && Airside.TryGetTrack(f, out track);
                All.Add(new Rec(env, evt, f.Value, has, track));
            });
        }

        public List<string> Trace()
        {
            return All.ConvertAll(r => r.ToString());
        }

        /// <summary>Events of type T, in publication order, optionally for one flight.</summary>
        public List<(Rec Rec, T Evt)> Of<T>(ulong? flight = null) where T : struct, ISimEvent
        {
            var list = new List<(Rec Rec, T Evt)>();
            foreach (Rec r in All)
            {
                if (r.Payload is T e && (!flight.HasValue || r.Flight == flight.Value))
                {
                    list.Add((r, e));
                }
            }

            return list;
        }

        /// <summary>sim.airside's milestones for one flight, in publication order.</summary>
        public List<Rec> Milestones(ulong flight)
        {
            return All.FindAll(r => r.FromAirside && r.Payload is FlightMilestoneReached && r.Flight == flight);
        }

        public List<Rec> MilestonesOf(ulong flight, FlightMilestone m)
        {
            return All.FindAll(r => r.FromAirside && r.Flight == flight && r.IsMilestone(m));
        }

        public bool Has(ulong flight, FlightMilestone m)
        {
            return MilestonesOf(flight, m).Count > 0;
        }

        /// <summary>The one sim.airside milestone m of the flight; fails unless there is exactly one.</summary>
        public Rec Milestone(ulong flight, FlightMilestone m)
        {
            List<Rec> found = MilestonesOf(flight, m);
            Assert.True(found.Count == 1, string.Format(CultureInfo.InvariantCulture, "expected exactly one {0} for flight {1}, found {2}:\n{3}", m, flight, found.Count, string.Join("\n", Trace())));
            return found[0];
        }
    }

    /// <summary>
    /// sim.schedule and sim.airside inside a real host, optionally with a fake
    /// sim.flow, a turnaround probe and the event recorder. sim.schedule gets
    /// no flow, so every Inject/Absorb the fake sees comes from sim.airside.
    /// </summary>
    internal sealed class HostRig
    {
        public readonly ISimHost Host;
        public readonly IScheduleSystem Schedule;
        public readonly IAirsideSystem Airside;
        public readonly AirsideLayout Layout;
        public readonly FakeFlowBase? Flow;
        public readonly Recorder? Events;
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        public readonly Dictionary<string, ulong> Ids;
        public readonly AirsideProbe? Probe;

        public HostRig(
            byte[] csv,
            AirsideLayout? layout = null,
            uint holdMinutes = 10,
            FakeFlowBase? flow = null,
            bool turnaroundRegistered = false,
            TurnaroundProbe? turnaround = null,
            ulong seed = 0x5EED_0021UL,
            bool record = true,
            ISimLog? log = null,
            uint doorDelayMinutes = AirConst.FixtureDoorDelayMinutes,
            bool probe = false,
            HandlerTimer? handlerTimer = null,
            StandInTurnaround? standIn = null)
        {
            Ids = Csv.Ids(csv);
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, AirsideContent.Index(), Sink, log ?? new NullLog()));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(csv, ScheduleFixture.SourceName);
            Schedule = ScheduleFactory.CreateSystem(b.Services, table, null);
            Layout = layout ?? FixtureLayout.Layout();
            Flow = flow;
            SystemServices services = b.Services;
            if (handlerTimer != null)
            {
                // 03 Q-064: only sim.airside's handlers are shimmed.
                services = new SystemServices(
                    new TimingBus(services.Events, handlerTimer), services.Ids, services.Content, new TimingRegistry(services.Commands, handlerTimer));
            }

            Airside = AirsideFactory.CreateSystem(services, Layout, new AirsideRules(holdMinutes, doorDelayMinutes), Schedule, flow, turnaroundRegistered);
            b.Register(Schedule);
            if (probe)
            {
                Probe = new AirsideProbe(Airside);
                b.Register(Probe);
            }
            else
            {
                b.Register(Airside);
            }
            if (flow != null)
            {
                b.Register(flow);
            }

            if (turnaround != null)
            {
                b.Register(turnaround);
            }
            else if (standIn != null)
            {
                standIn.Subscribe(b.Services.Events);
                b.Register(standIn);
            }

            if (record)
            {
                Events = new Recorder(b.Services.Events, AirConst.RecorderSystemId) { Airside = Airside };
                b.Register(new ProbeSystem(AirConst.RecorderSystemId));
            }

            Host = b.Build();
        }

        public Recorder Rec => Events!;

        public ulong Id(string flightRef)
        {
            return Ids[flightRef];
        }

        public void RunTo(ulong tick)
        {
            while (Host.CurrentTick < tick)
            {
                Host.Step((uint)Math.Min(tick - Host.CurrentTick, 1000000UL));
            }
        }

        /// <summary>Steps one tick at a time up to <paramref name="tick"/>, calling <paramref name="after"/> with each completed tick.</summary>
        public void StepEach(ulong tick, Action<ulong> after)
        {
            while (Host.CurrentTick < tick)
            {
                ulong t = Host.CurrentTick;
                Host.Step(1);
                after(t);
            }
        }

        public FlightRecord Flight(ulong id)
        {
            Assert.True(Schedule.TryGetFlight(new FlightId(id), out FlightRecord r), "TryGetFlight(" + id.ToString(CultureInfo.InvariantCulture) + ") returned false");
            return r;
        }

        public AircraftTrack Track(ulong id)
        {
            Assert.True(Airside.TryGetTrack(new FlightId(id), out AircraftTrack t), "no track for flight " + id.ToString(CultureInfo.InvariantCulture) + " at tick " + Host.CurrentTick.ToString(CultureInfo.InvariantCulture));
            return t;
        }

        public FlightId? Occupant(ushort stand)
        {
            Assert.True(Airside.TryGetStand(new StandId(stand), out StandState s), "TryGetStand(" + stand.ToString(CultureInfo.InvariantCulture) + ") returned false");
            Assert.Equal(stand, s.Id.Value);
            return s.Occupant;
        }

        public List<ushort> Free()
        {
            var list = new List<ushort>();
            foreach (StandId s in Airside.FreeStands())
            {
                list.Add(s.Value);
            }

            return list;
        }

        public bool Submit(in Command cmd, out CommandRejection reason)
        {
            return Host.TrySubmit(cmd, out reason);
        }
    }

    /// <summary>
    /// Registered in sim.airside's place (same SystemId, 3), so the real host
    /// and bus drive it, phase-3 handlers included. 03 "Measured" (Q-064)
    /// times a module's Tick plus the bodies of its handlers and Apply. This
    /// probe times the Tick part only. The handler part comes from the
    /// TimingBus and TimingRegistry shims, and a budget sample is the sum of
    /// the two. It can also hand the inner Tick a trap RNG in place of the
    /// host's, so any RNG use is sim.airside's own. It measures no
    /// allocation: allocation tests use the T-037 meter over Step (03, Q-061).
    /// </summary>
    internal sealed class AirsideProbe : ISimSystem
    {
        private readonly IAirsideSystem _inner;

        public AirsideProbe(IAirsideSystem inner)
        {
            _inner = inner;
        }

        public IRandomService? Rng;
        public bool Timing;
        public long LastElapsed;

        public SystemId Id => _inner.Id;

        public string Name => _inner.Name;

        public void Tick(in TickContext ctx)
        {
            TickContext inner = Rng == null ? ctx : new TickContext(ctx.Tick, ctx.Clock, ctx.Events, Rng, ctx.Content, ctx.Log);
            if (Timing)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                _inner.Tick(inner);
                LastElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }
            else
            {
                _inner.Tick(inner);
            }
        }

        public ulong ComputeStateHash()
        {
            return _inner.ComputeStateHash();
        }
    }

    /// <summary>Counts every RNG access. sim.airside must make none (12 §12.12).</summary>
    internal sealed class TrapRandomService : IRandomService
    {
        public int StreamCalls;
        public int Draws;
        private readonly TrapStream _stream;

        public TrapRandomService()
        {
            _stream = new TrapStream(this);
        }

        public ulong MasterSeed => 0x5EED_0021UL;

        public IRandomStream Stream(RngStreamName name)
        {
            StreamCalls++;
            return _stream;
        }

        private sealed class TrapStream : IRandomStream
        {
            private readonly TrapRandomService _owner;

            public TrapStream(TrapRandomService owner)
            {
                _owner = owner;
            }

            public ulong NextUInt64()
            {
                _owner.Draws++;
                return 0UL;
            }

            public int NextInt(int minInclusive, int maxExclusive)
            {
                _owner.Draws++;
                return minInclusive;
            }

            public Fx NextFx01()
            {
                _owner.Draws++;
                return Fx.Zero;
            }

            public bool Chance(Fx probability)
            {
                _owner.Draws++;
                return false;
            }

            public void Shuffle<T>(Span<T> items)
            {
                _owner.Draws++;
            }

            public ulong ComputeStateHash()
            {
                _owner.Draws++;
                return 0UL;
            }
        }
    }


    /// <summary>
    /// Every zero-allocation assertion measures through this; a copy of the
    /// sim.core test kit's Allocation (test projects share no code).
    /// GC.GetAllocatedBytesForCurrentThread subtracts the unused part of the
    /// thread's allocation context, so when the runtime retires a partly used
    /// context inside the window the delta can grow by up to one allocation
    /// quantum (about 8 KB) with nothing allocated. A full blocking collection
    /// right before the first read leaves the thread with no context to
    /// retire; any real allocation still shows up in full.
    /// </summary>
    internal static class Allocation
    {
        public static long Start()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetAllocatedBytesForCurrentThread();
        }

        public static long Since(long start)
        {
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
    }
}
