using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    // Test doubles and rigs. sim.core publishes no null IContentIndex,
    // ICheckpointSink or ISimLog (08 §8.11a), so tests supply their own.

    /// <summary>
    /// A probe at sim.airside's registry position 3 (08 §8.5 lets tests
    /// register probes at a legal position whose module is not in the build)
    /// that publishes scripted FlightMilestoneReached{OnStand} during its own
    /// Tick, as sim.airside does in phase 2 (13 §13.9). It stores each
    /// published EventId so tests can check Causes against it. The script is
    /// held in arrays sized at construction, so publishing allocates nothing.
    /// </summary>
    internal sealed class OnStandDriver : ISimSystem
    {
        private readonly ulong[] _ticks;
        private readonly ulong[] _flights;
        private readonly ulong[] _planned;
        private readonly EventId[] _ids;
        private readonly bool[] _done;
        private int _next;

        public OnStandDriver(List<(ulong Tick, ulong Flight, ulong Planned)> script)
        {
            var sorted = new List<(ulong Tick, ulong Flight, ulong Planned)>(script);
            sorted.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Flight.CompareTo(b.Flight));
            _ticks = new ulong[sorted.Count];
            _flights = new ulong[sorted.Count];
            _planned = new ulong[sorted.Count];
            _ids = new EventId[sorted.Count];
            _done = new bool[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                _ticks[i] = sorted[i].Tick;
                _flights[i] = sorted[i].Flight;
                _planned[i] = sorted[i].Planned;
            }
        }

        public SystemId Id => new SystemId(TConst.AirsideSystemId);

        public string Name => "probe.airside";

        public int Published { get; private set; }

        public void Tick(in TickContext ctx)
        {
            while (_next < _ticks.Length && _ticks[_next] <= ctx.Tick)
            {
                if (_ticks[_next] == ctx.Tick)
                {
                    _ids[_next] = ctx.Events.Publish(
                        new FlightMilestoneReached(new FlightId(_flights[_next]), FlightMilestone.OnStand, _planned[_next], ctx.Tick),
                        EventRef.None);
                    _done[_next] = true;
                    Published++;
                }

                _next++;
            }
        }

        public ulong ComputeStateHash()
        {
            return 0UL;
        }

        /// <summary>The OnStand this probe published for the flight, if any.</summary>
        public bool TryGetOnStand(ulong flight, out EventId id, out ulong tick)
        {
            for (int i = 0; i < _flights.Length; i++)
            {
                if (_flights[i] == flight && _done[i])
                {
                    id = _ids[i];
                    tick = _ticks[i];
                    return true;
                }
            }

            id = default;
            tick = 0UL;
            return false;
        }

        public EventId OnStand(ulong flight)
        {
            Assert.True(TryGetOnStand(flight, out EventId id, out _), "the driver never published OnStand for flight " + flight.ToString(CultureInfo.InvariantCulture));
            return id;
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

    /// <summary>The handler time of the current tick, summed by the shims below.</summary>
    internal sealed class HandlerTimer
    {
        public long Elapsed;
        public long Calls;
    }

    /// <summary>
    /// Forwards to the real bus, and wraps each handler the module subscribes,
    /// at subscription, in a shim. With a timer it is 03 "Timing a module's
    /// handlers" (Q-064): the timestamp difference around the real handler is
    /// added to the timer. With a trap RNG it hands the handler a TickContext
    /// whose Rng is the trap, so RNG use in a handler is counted. The shim is
    /// created once, during construction, and allocates nothing per call.
    /// </summary>
    internal sealed class ShimBus : IEventBus
    {
        private readonly IEventBus _real;
        private readonly HandlerTimer? _timer;
        private readonly IRandomService? _rng;

        public ShimBus(IEventBus real, HandlerTimer? timer, IRandomService? rng)
        {
            _real = real;
            _timer = timer;
            _rng = rng;
        }

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            return _real.Publish(evt, cause);
        }

        public void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
        {
            HandlerTimer? timer = _timer;
            IRandomService? rng = _rng;
            _real.Subscribe<T>(subscriber, (in EventEnvelope env, in T evt, in TickContext ctx) =>
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                if (rng == null)
                {
                    handler(env, evt, ctx);
                }
                else
                {
                    handler(env, evt, new TickContext(ctx.Tick, ctx.Clock, ctx.Events, rng, ctx.Content, ctx.Log));
                }

                if (timer != null)
                {
                    timer.Elapsed += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                    timer.Calls++;
                }
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

    /// <summary>
    /// Registered in sim.turnaround's place (same SystemId, 5), so the real
    /// host and bus drive it, phase-3 handlers included. It times the inner
    /// Tick, and can hand the inner Tick a trap RNG in place of the host's.
    /// </summary>
    internal sealed class TurnaroundProbe : ISimSystem
    {
        private readonly ITurnaroundSystem _inner;

        public TurnaroundProbe(ITurnaroundSystem inner)
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

    /// <summary>Counts every RNG access. sim.turnaround must make none (13 §13.10).</summary>
    internal sealed class TrapRandomService : IRandomService
    {
        public int StreamCalls;
        public int Draws;
        private readonly TrapStream _stream;

        public TrapRandomService()
        {
            _stream = new TrapStream(this);
        }

        public ulong MasterSeed => 0x5EED_0022UL;

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

    /// <summary>One recorded event, as a phase-3 handler at position 7 saw it.</summary>
    internal sealed class Rec
    {
        public Rec(EventEnvelope env, object payload, ulong flight, JobKind? job, VehicleId? vehicle)
        {
            Env = env;
            Payload = payload;
            Flight = flight;
            Job = job;
            Vehicle = vehicle;
        }

        public EventEnvelope Env { get; }

        public object Payload { get; }

        public ulong Flight { get; }

        /// <summary>The JobKind of a TurnaroundJob* event, null for a milestone.</summary>
        public JobKind? Job { get; }

        /// <summary>For TurnaroundJobStarted: the job's Vehicle as TryGetJob gave it when the event was dispatched.</summary>
        public VehicleId? Vehicle { get; }

        public ulong Tick => Env.Tick;

        public EventId Id => Env.Id;

        public bool FromTurnaround => Env.Source.Value == TConst.TurnaroundSystemId;

        public bool IsMilestone(FlightMilestone m)
        {
            return Payload is FlightMilestoneReached ms && ms.Milestone == m;
        }

        public FlightMilestoneReached Milestone => (FlightMilestoneReached)Payload;

        public string Type => Payload.GetType().Name;

        public override string ToString()
        {
            string body;
            switch (Payload)
            {
                case FlightMilestoneReached m:
                    body = string.Format(CultureInfo.InvariantCulture, "milestone f={0} m={1} planned={2} actual={3}", m.Flight.Value, m.Milestone, m.PlannedTick, m.ActualTick);
                    break;
                case TurnaroundJobBlocked b:
                    body = string.Format(CultureInfo.InvariantCulture, "blocked f={0} {1} on={2} r={3} cat={4}", b.Flight.Value, b.Job, b.WaitingOn, b.Resource.HasValue ? b.Resource.Value.Value.ToString(CultureInfo.InvariantCulture) : "-", b.Category);
                    break;
                case TurnaroundJobUnblocked u:
                    body = string.Format(CultureInfo.InvariantCulture, "unblocked f={0} {1} on={2} r={3} cat={4}", u.Flight.Value, u.Job, u.WaitingOn, u.Resource.HasValue ? u.Resource.Value.Value.ToString(CultureInfo.InvariantCulture) : "-", u.Category);
                    break;
                case TurnaroundJobStarted s:
                    body = string.Format(CultureInfo.InvariantCulture, "started f={0} {1} v={2}", s.Flight.Value, s.Job, Vehicle.HasValue ? Vehicle.Value.Value.ToString(CultureInfo.InvariantCulture) : "-");
                    break;
                case TurnaroundJobCompleted c:
                    body = string.Format(CultureInfo.InvariantCulture, "completed f={0} {1}", c.Flight.Value, c.Job);
                    break;
                default:
                    body = Payload.GetType().Name;
                    break;
            }

            return string.Format(CultureInfo.InvariantCulture, "t={0} id={1} src={2} cause={3} {4}", Env.Tick, Show.Id(Env.Id), Env.Source.Value, Show.Id(Env.Cause), body);
        }
    }

    /// <summary>
    /// A probe at position 7 that records every event sim.turnaround may emit
    /// (10 §10.6 "From sim.turnaround", §10.4) and every milestone, in
    /// dispatch order.
    /// </summary>
    internal sealed class Recorder
    {
        public readonly List<Rec> All = new List<Rec>();

        /// <summary>Each flight's OnStand from position 3 (the driver or sim.airside): its EventId and tick.</summary>
        public readonly Dictionary<ulong, (EventId Id, ulong Tick)> OnStands = new Dictionary<ulong, (EventId Id, ulong Tick)>();
        public ITurnaroundSystem? Turnaround;

        public Recorder(IEventBus bus, ushort subscriber)
        {
            var id = new SystemId(subscriber);
            bus.Subscribe<FlightMilestoneReached>(id, (in EventEnvelope env, in FlightMilestoneReached e, in TickContext ctx) =>
            {
                if (e.Milestone == FlightMilestone.OnStand && env.Source.Value == TConst.AirsideSystemId)
                {
                    OnStands[e.Flight.Value] = (env.Id, env.Tick);
                }

                All.Add(new Rec(env, e, e.Flight.Value, null, null));
            });
            bus.Subscribe<TurnaroundJobBlocked>(id, (in EventEnvelope env, in TurnaroundJobBlocked e, in TickContext ctx) =>
                All.Add(new Rec(env, e, e.Flight.Value, e.Job, null)));
            bus.Subscribe<TurnaroundJobUnblocked>(id, (in EventEnvelope env, in TurnaroundJobUnblocked e, in TickContext ctx) =>
                All.Add(new Rec(env, e, e.Flight.Value, e.Job, null)));
            bus.Subscribe<TurnaroundJobCompleted>(id, (in EventEnvelope env, in TurnaroundJobCompleted e, in TickContext ctx) =>
                All.Add(new Rec(env, e, e.Flight.Value, e.Job, null)));
            bus.Subscribe<TurnaroundJobStarted>(id, (in EventEnvelope env, in TurnaroundJobStarted e, in TickContext ctx) =>
            {
                VehicleId? v = null;
                if (Turnaround != null && Turnaround.TryGetJob(TConst.Job(e.Flight.Value, e.Job), out TurnaroundJob job))
                {
                    v = job.Vehicle;
                }

                All.Add(new Rec(env, e, e.Flight.Value, e.Job, v));
            });
        }

        public List<string> Trace()
        {
            return All.ConvertAll(r => r.ToString());
        }

        public string Dump()
        {
            return string.Join("\n", Trace());
        }

        /// <summary>Events of type T, in dispatch order, optionally for one flight.</summary>
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

        /// <summary>sim.turnaround's events about one job, in dispatch order.</summary>
        public List<Rec> ForJob(ulong flight, JobKind kind)
        {
            return All.FindAll(r => r.FromTurnaround && r.Flight == flight && r.Job == kind);
        }

        /// <summary>Milestones m published by sim.turnaround, optionally for one flight.</summary>
        public List<Rec> Milestones(FlightMilestone m, ulong? flight = null)
        {
            return All.FindAll(r => r.FromTurnaround && r.IsMilestone(m) && (!flight.HasValue || r.Flight == flight.Value));
        }

        public Rec ById(in EventId id)
        {
            EventId want = id;
            Rec? found = All.Find(r => r.Id.Equals(want));
            Assert.True(found != null, "no recorded event with id " + Show.Id(want) + "\n" + Dump());
            return found!;
        }
    }

    /// <summary>
    /// sim.schedule, the OnStand driver at position 3 and sim.turnaround inside
    /// a real host, with the event recorder at 7 unless <c>record</c> is false.
    /// sim.schedule gets no flow (11 §11.6).
    /// </summary>
    internal sealed class Rig
    {
        /// <summary>The day driver's stand-in taxi-in: an arrival reaches its stand this long after STA.</summary>
        public const ulong ArrivalTaxiTicks = 50UL;

        public readonly ISimHost Host;
        public readonly IScheduleSystem Schedule;
        public readonly ITurnaroundSystem Turnaround;
        public readonly OnStandDriver Driver;
        public readonly Recorder? Events;
        public readonly TurnaroundProbe? Probe;
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        public readonly Dictionary<string, ulong> Ids;
        public readonly Dictionary<ulong, Movement> Moves = new Dictionary<ulong, Movement>();

        /// <param name="csv">The schedule.</param>
        /// <param name="setup">sim.turnaround's construction data.</param>
        /// <param name="onStand">Scripted OnStands, (flight_ref, tick), for day-0 occurrences.</param>
        /// <param name="driveDays">
        /// If above 0, also an OnStand for every occurrence on days 0 to
        /// driveDays − 1: an arrival at STA + <see cref="ArrivalTaxiTicks"/>, a
        /// departure at its planned OnStand, max(0, STD − MinTurnaround).
        /// </param>
        public Rig(
            byte[] csv,
            TurnaroundSetup setup,
            (string Ref, ulong Tick)[]? onStand = null,
            int driveDays = 0,
            ulong seed = 0x5EED_0022UL,
            bool record = true,
            HandlerTimer? handlerTimer = null,
            IRandomService? trapRng = null,
            bool probe = false,
            List<(ulong Tick, ulong Flight, ulong Planned)>? script = null)
        {
            Ids = Csv.Ids(csv);
            foreach ((string _, Movement m) in Csv.Rows(csv, Math.Max(driveDays, 1)))
            {
                Moves[m.Flight] = m;
            }

            script = script ?? new List<(ulong Tick, ulong Flight, ulong Planned)>();
            if (onStand != null)
            {
                foreach ((string r, ulong t) in onStand)
                {
                    Movement m = Moves[Ids[r]];
                    script.Add((t, m.Flight, m.Departure ? m.PlannedDepartureOnStand : t));
                }
            }

            if (driveDays > 0)
            {
                foreach (Movement m in Moves.Values)
                {
                    ulong t = m.Departure ? m.PlannedDepartureOnStand : m.ScheduledTick + ArrivalTaxiTicks;
                    script.Add((t, m.Flight, t));
                }
            }

            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, TurnContent.Index(), Sink, new NullLog()));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(csv, ScheduleFixture.SourceName);
            Schedule = ScheduleFactory.CreateSystem(b.Services, table, null);
            Driver = new OnStandDriver(script);

            SystemServices services = b.Services;
            if (handlerTimer != null || trapRng != null)
            {
                // Only sim.turnaround's handlers are shimmed (03 Q-064).
                ICommandHandlerRegistry commands = handlerTimer != null ? new TimingRegistry(services.Commands, handlerTimer) : services.Commands;
                services = new SystemServices(new ShimBus(services.Events, handlerTimer, trapRng), services.Ids, services.Content, commands);
            }

            Turnaround = TurnaroundFactory.CreateSystem(services, setup, Schedule);
            b.Register(Schedule);
            b.Register(Driver);
            if (probe || trapRng != null)
            {
                Probe = new TurnaroundProbe(Turnaround) { Rng = trapRng };
                b.Register(Probe);
            }
            else
            {
                b.Register(Turnaround);
            }

            if (record)
            {
                Events = new Recorder(b.Services.Events, TConst.RecorderSystemId) { Turnaround = Turnaround };
                b.Register(new ProbeSystem(TConst.RecorderSystemId));
            }

            Host = b.Build();
        }

        public Recorder Rec => Events!;

        public ulong Id(string flightRef)
        {
            return Ids[flightRef];
        }

        public Movement Move(string flightRef)
        {
            return Moves[Ids[flightRef]];
        }

        /// <summary>Runs every tick before <paramref name="tick"/>; afterwards CurrentTick == tick.</summary>
        public void RunTo(ulong tick)
        {
            while (Host.CurrentTick < tick)
            {
                Host.Step((uint)Math.Min(tick - Host.CurrentTick, 1000000UL));
            }
        }

        /// <summary>Runs every tick up to and including <paramref name="tick"/>.</summary>
        public void RunThrough(ulong tick)
        {
            RunTo(tick + 1UL);
        }

        public TurnaroundJob Job(ulong flight, JobKind kind)
        {
            Assert.True(
                Turnaround.TryGetJob(TConst.Job(flight, kind), out TurnaroundJob j),
                "TryGetJob(" + kind + " of flight " + flight.ToString(CultureInfo.InvariantCulture) + ") returned false at tick " + Host.CurrentTick.ToString(CultureInfo.InvariantCulture));
            return j;
        }

        public TurnaroundJob Job(string flightRef, JobKind kind)
        {
            return Job(Id(flightRef), kind);
        }

        public List<ushort> Free(VehicleKind kind)
        {
            var list = new List<ushort>();
            foreach (VehicleId v in Turnaround.FreeVehicles(kind))
            {
                list.Add(v.Value);
            }

            return list;
        }
    }

    /// <summary>
    /// Every zero-allocation assertion measures through this; a copy of the
    /// sim.core test kit's Allocation (test projects share no code). A full
    /// blocking collection right before the first read leaves the thread with
    /// no allocation context to retire inside the window.
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
