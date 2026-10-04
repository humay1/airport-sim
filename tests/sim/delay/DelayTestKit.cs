using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    // Shared data and rigs for the T-024 suite, written from
    // 14-interfaces-delay.md, 06-delay-attribution.md, 10-events.md,
    // 08-interfaces-core.md, 03-module-map.md and 07-conventions.md only.
    // sim.delay is a pure function of its input events (14 §14.14), so every
    // rig drives it with scripted events from probes at the registry
    // positions of the modules it observes (08 §8.5 lets tests register
    // probes at a legal position whose module is not in the build).

    internal static class DConst
    {
        // 08 §8.1, 11 §11.2 and 14 §14.2, as literals: the spec names no class
        // for sim.delay's constants.
        public const ulong TicksPerDay = 14400UL;
        public const ulong TicksPerMinute = 10UL;
        public const ulong TickUnscheduled = ulong.MaxValue;
        public const ulong RetentionDays = 2UL;
        public const int MaxAttributionDepth = 6;
        public const ulong PlanPublishLead = 14400UL;
        public const ushort SchedulePos = 2;
        public const ushort AirsidePos = 3;
        public const ushort FlowPos = 4;
        public const ushort TurnaroundPos = 5;
        public const ushort DelayPos = 7;
        public const ushort RecorderPos = 9;

        private static readonly FlightMilestone[] ArrivalCheckpoints = { FlightMilestone.Landed, FlightMilestone.OnStand };
        private static readonly FlightMilestone[] DepartureCheckpoints = { FlightMilestone.OnStand, FlightMilestone.Pushback, FlightMilestone.Airborne };

        /// <summary>14 §14.4: the checkpoints of a MovementKind, in order; the last is terminal.</summary>
        public static FlightMilestone[] Checkpoints(MovementKind k)
        {
            return k == MovementKind.Arrival ? ArrivalCheckpoints : DepartureCheckpoints;
        }

        /// <summary>14 §14.3: Minutes = Fx.FromRatio(Ticks, TICKS_PER_SIM_MINUTE).</summary>
        public static Fx Minutes(ulong ticks)
        {
            return Fx.FromRatio((long)ticks, (long)TicksPerMinute);
        }

        public static FlightId F(ulong v)
        {
            return new FlightId(v);
        }

        public static DelayEventId D(ulong v)
        {
            return new DelayEventId(v);
        }
    }

    /// <summary>
    /// SplitMix64 as pinned by 08 §8.8, the input generator for property-style
    /// tests (07 L4). Always seeded with an integer literal inside the test
    /// that uses it.
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

        /// <summary>A value in [lo, hi], both inclusive. Test data only; the modulo bias does not matter.</summary>
        public long Range(long lo, long hi)
        {
            ulong span = (ulong)(hi - lo) + 1UL;
            return lo + (long)(Next() % span);
        }

        public bool Permille(int p)
        {
            return (int)(Next() % 1000UL) < p;
        }
    }

    /// <summary>One scripted publication: an event, the tick and the registry position it is published from.</summary>
    internal sealed class Step
    {
        private readonly Func<IEventPublisher, EventId> _publish;
        private EventId _id;

        public Step(ulong tick, ushort position, string label, Func<IEventPublisher, EventId> publish)
        {
            Tick = tick;
            Position = position;
            Label = label;
            _publish = publish;
        }

        public ulong Tick { get; }

        public ushort Position { get; }

        public string Label { get; }

        /// <summary>Insertion order within the driver; ties at one tick are published in this order.</summary>
        public long Order { get; set; }

        public bool Done { get; private set; }

        /// <summary>The EventId the bus gave this publication.</summary>
        public EventId Id
        {
            get
            {
                Assert.True(Done, "step not published yet: " + Label);
                return _id;
            }
        }

        public EventRef Ref => new EventRef(Id, true);

        public void Fire(IEventPublisher events)
        {
            _id = _publish(events);
            Done = true;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// A scripted event stream. Each helper publishes from the registry
    /// position of the module that emits the event in a real build (10 §10.6):
    /// sim.schedule 2, sim.airside 3, sim.flow 4, sim.turnaround 5. Within one
    /// tick, position order decides publication order (phase 2 runs systems in
    /// registry order), then the order the steps were added.
    /// </summary>
    internal sealed class Script
    {
        public readonly List<Step> Steps = new List<Step>();

        public Step Add<T>(ulong tick, ushort position, string label, T evt) where T : struct, ISimEvent
        {
            var s = new Step(tick, position, label, p => p.Publish(evt, EventRef.None));
            Steps.Add(s);
            return s;
        }

        private static string L(string format, params object?[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }

        /// <summary>FlightPlanPublished from position 2. rotation 0 means none, and Rotation is then the flight itself (11 §11.2).</summary>
        public Step Plan(ulong tick, ulong flight, MovementKind kind, ulong rotation = 0UL)
        {
            bool has = rotation != 0UL;
            var e = new FlightPlanPublished(
                new FlightId(flight),
                kind,
                new FlightId(has ? rotation : flight),
                has,
                new AirlineId(1U),
                new ContentId("a320"),
                DConst.TickUnscheduled,
                DConst.TickUnscheduled,
                Fx.FromInt(35));
            return Add(tick, DConst.SchedulePos, L("t={0} plan f={1} {2} rot={3}", tick, flight, kind, rotation), e);
        }

        /// <summary>
        /// FlightMilestoneReached with ActualTick = the publication tick (10 §10.3
        /// rule 3), from its emitter's position (10 §10.4).
        /// </summary>
        public Step Milestone(ulong tick, ulong flight, FlightMilestone m, ulong planned)
        {
            ushort pos = m == FlightMilestone.PlanPublished ? DConst.SchedulePos
                : (m == FlightMilestone.DeboardComplete || m == FlightMilestone.ReadyToBoard || m == FlightMilestone.BoardingComplete) ? DConst.TurnaroundPos
                : DConst.AirsidePos;
            return Add(tick, pos, L("t={0} milestone f={1} {2} planned={3}", tick, flight, m, planned), new FlightMilestoneReached(new FlightId(flight), m, planned, tick));
        }

        public Step RunwayHeld(ulong tick, ulong flight, ushort runway, int queuePosition)
        {
            return Add(tick, DConst.AirsidePos, L("t={0} runway-held f={1} rw={2} q={3}", tick, flight, runway, queuePosition), new AircraftHeldForRunway(new FlightId(flight), new RunwayId(runway), queuePosition));
        }

        public Step RunwayReleased(ulong tick, ulong flight, ushort runway)
        {
            return Add(tick, DConst.AirsidePos, L("t={0} runway-released f={1} rw={2}", tick, flight, runway), new AircraftHeldForRunwayReleased(new FlightId(flight), new RunwayId(runway), 0));
        }

        public Step TaxiHeld(ulong tick, ulong flight, ushort edge, ulong? blocking)
        {
            FlightId? b = blocking.HasValue ? new FlightId(blocking.Value) : (FlightId?)null;
            return Add(tick, DConst.AirsidePos, L("t={0} taxi-held f={1} edge={2} by={3}", tick, flight, edge, blocking), new AircraftHeldOnTaxiway(new FlightId(flight), new TaxiEdgeId(edge), b));
        }

        public Step TaxiReleased(ulong tick, ulong flight, ushort edge)
        {
            return Add(tick, DConst.AirsidePos, L("t={0} taxi-released f={1} edge={2}", tick, flight, edge), new AircraftHeldOnTaxiwayReleased(new FlightId(flight), new TaxiEdgeId(edge), null));
        }

        public Step StandUnavailable(ulong tick, ulong flight, ushort? stand, ulong? occupying)
        {
            StandId? s = stand.HasValue ? new StandId(stand.Value) : (StandId?)null;
            FlightId? o = occupying.HasValue ? new FlightId(occupying.Value) : (FlightId?)null;
            return Add(tick, DConst.AirsidePos, L("t={0} stand-unavailable f={1} stand={2} occ={3}", tick, flight, stand, occupying), new StandUnavailable(new FlightId(flight), s, o));
        }

        /// <summary>
        /// StandAssigned. The Stand family's key is the flight alone (14 §14.5,
        /// Q-107): it closes the flight's open Stand interval whatever either
        /// event's Stand holds.
        /// </summary>
        public Step StandAssigned(ulong tick, ulong flight, ushort? stand)
        {
            StandId? s = stand.HasValue ? new StandId(stand.Value) : (StandId?)null;
            return Add(tick, DConst.AirsidePos, L("t={0} stand-assigned f={1} stand={2}", tick, flight, stand), new StandAssigned(new FlightId(flight), s, null));
        }

        public Step JobBlocked(ulong tick, ulong flight, JobKind job, ResourceKind waitingOn, DelayCategory category, ulong? resource = null)
        {
            EntityId? r = resource.HasValue ? new EntityId(resource.Value) : (EntityId?)null;
            return Add(tick, DConst.TurnaroundPos, L("t={0} job-blocked f={1} {2} on={3} cat={4}", tick, flight, job, waitingOn, category), new TurnaroundJobBlocked(new FlightId(flight), job, waitingOn, r, category));
        }

        public Step JobUnblocked(ulong tick, ulong flight, JobKind job, ResourceKind waitingOn, DelayCategory category, ulong? resource = null)
        {
            EntityId? r = resource.HasValue ? new EntityId(resource.Value) : (EntityId?)null;
            return Add(tick, DConst.TurnaroundPos, L("t={0} job-unblocked f={1} {2} on={3} cat={4}", tick, flight, job, waitingOn, category), new TurnaroundJobUnblocked(new FlightId(flight), job, waitingOn, r, category));
        }

        public Step Hold(ulong tick, ulong flight, int outstanding, uint heldAt)
        {
            return Add(tick, DConst.AirsidePos, L("t={0} pax-hold f={1} out={2} at={3}", tick, flight, outstanding, heldAt), new DepartureHeldForPassengers(new FlightId(flight), outstanding, new NodeId(heldAt)));
        }

        public Step HoldReleased(ulong tick, ulong flight)
        {
            return Add(tick, DConst.AirsidePos, L("t={0} pax-hold-released f={1}", tick, flight), new DepartureHeldForPassengersReleased(new FlightId(flight), 0, null));
        }

        public Step Missed(ulong tick, ulong flight, int count, uint lastBlockedAt)
        {
            return Add(tick, DConst.FlowPos, L("t={0} missed f={1} n={2} at={3}", tick, flight, count, lastBlockedAt), new PassengersMissedFlight(new FlightId(flight), count, new NodeId(lastBlockedAt)));
        }
    }

    /// <summary>
    /// A probe at one of the observed modules' registry positions that
    /// publishes its scripted steps during its own Tick (phase 2), as the real
    /// emitter would. Steps live in a list sorted once when appended, so
    /// publishing allocates nothing.
    /// </summary>
    internal sealed class Driver : ISimSystem
    {
        private static readonly IComparer<Step> ByTickThenOrder = Comparer<Step>.Create((a, b) =>
            a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Order.CompareTo(b.Order));

        private readonly List<Step> _steps = new List<Step>();
        private long _order;
        private int _next;

        public Driver(ushort position)
        {
            Id = new SystemId(position);
        }

        public SystemId Id { get; }

        public string Name => "probe.driver";

        public int Pending => _steps.Count - _next;

        public void Append(List<Step> steps)
        {
            int added = 0;
            foreach (Step s in steps)
            {
                if (s.Position == Id.Value)
                {
                    s.Order = _order++;
                    _steps.Add(s);
                    added++;
                }
            }

            if (added > 0)
            {
                _steps.Sort(_next, _steps.Count - _next, ByTickThenOrder);
            }
        }

        public void Tick(in TickContext ctx)
        {
            while (_next < _steps.Count && _steps[_next].Tick <= ctx.Tick)
            {
                Step s = _steps[_next];
                if (s.Tick < ctx.Tick)
                {
                    throw new InvalidOperationException("test bug: script step appended after its tick: " + s.Label);
                }

                s.Fire(ctx.Events);
                _next++;
            }
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

        /// <summary>Every subscription the module made through this bus: (event type, subscriber).</summary>
        public readonly List<(Type Type, ushort Subscriber)> Subscribed = new List<(Type Type, ushort Subscriber)>();

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            return _real.Publish(evt, cause);
        }

        public void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent
        {
            Subscribed.Add((typeof(T), subscriber.Value));
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

    /// <summary>
    /// Registered in sim.delay's place (same SystemId, 7), so the real host and
    /// bus drive it, phase-3 handlers included. It can time the inner Tick, hand
    /// it a trap RNG, and meter the inner Tick's allocations on one tick.
    /// </summary>
    internal sealed class DelayProbe : ISimSystem
    {
        private readonly IDelaySystem _inner;

        public DelayProbe(IDelaySystem inner)
        {
            _inner = inner;
        }

        public IRandomService? Rng;
        public bool Timing;
        public long LastElapsed;
        public ulong MeterTick = ulong.MaxValue;
        public long MeteredBytes = -1L;

        public SystemId Id => _inner.Id;

        public string Name => _inner.Name;

        public void Tick(in TickContext ctx)
        {
            TickContext inner = Rng == null ? ctx : new TickContext(ctx.Tick, ctx.Clock, ctx.Events, Rng, ctx.Content, ctx.Log);
            if (ctx.Tick == MeterTick)
            {
                long start = Allocation.Start();
                _inner.Tick(inner);
                MeteredBytes = Allocation.Since(start);
            }
            else if (Timing)
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

    /// <summary>Counts every RNG access. sim.delay must make none (14 §14.13).</summary>
    internal sealed class TrapRandomService : IRandomService
    {
        public int StreamCalls;
        public int Draws;
        private readonly TrapStream _stream;

        public TrapRandomService()
        {
            _stream = new TrapStream(this);
        }

        public ulong MasterSeed => 0x5EED_0024UL;

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

    /// <summary>One event as the position-9 recorder saw it, after sim.delay's handler for it had run.</summary>
    internal sealed class Rec
    {
        public Rec(EventEnvelope env, object payload, ulong flight)
        {
            Env = env;
            Payload = payload;
            Flight = flight;
        }

        public EventEnvelope Env { get; }

        public object Payload { get; }

        public ulong Flight { get; }

        public bool FromDelay => Env.Source.Value == DConst.DelayPos;

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "t={0} id={1} src={2} cause={3} {4} f={5}", Env.Tick, Show.Id(Env.Id), Env.Source.Value, Show.Id(Env.Cause), Payload.GetType().Name, Flight);
        }
    }

    /// <summary>
    /// A probe at position 9 (after sim.delay's 7, 08 §8.6 rule 4) subscribed
    /// to every event type sim.core declares. For any one event its handler
    /// runs right after sim.delay's, so <see cref="After"/> sees the state
    /// sim.delay's handler left: "after every handler" (14 §14.6, §14.10).
    /// </summary>
    internal sealed class Recorder
    {
        public readonly List<Rec> All = new List<Rec>();
        public readonly List<(EventEnvelope Env, DelayEvent Evt)> Delays = new List<(EventEnvelope Env, DelayEvent Evt)>();

        /// <summary>Every event type published with Source = sim.delay's position, with its count.</summary>
        public readonly Dictionary<Type, int> FromDelay = new Dictionary<Type, int>();

        /// <summary>Called after each recorded event other than a DelayEvent: (envelope, payload, the event's flight or 0).</summary>
        public Action<EventEnvelope, object, ulong>? After;

        public bool Keep = true;

        public Recorder(IEventBus bus)
        {
            MethodInfo one = typeof(Recorder).GetMethod(nameof(SubscribeOne), BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (Type t in EventTypes())
            {
                one.MakeGenericMethod(t).Invoke(this, new object[] { bus });
            }
        }

        /// <summary>Every public event struct sim.core declares (03: events are defined in sim.core).</summary>
        public static List<Type> EventTypes()
        {
            var list = new List<Type>();
            foreach (Type t in typeof(ISimEvent).Assembly.GetTypes())
            {
                if (t.IsPublic && t.IsValueType && typeof(ISimEvent).IsAssignableFrom(t))
                {
                    list.Add(t);
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return list;
        }

        private void SubscribeOne<T>(IEventBus bus) where T : struct, ISimEvent
        {
            PropertyInfo? fp = typeof(T).GetProperty("Flight");
            Func<object, ulong> flightOf = fp != null && fp.PropertyType == typeof(FlightId)
                ? (o => ((FlightId)fp.GetValue(o)!).Value)
                : (o => 0UL);
            bus.Subscribe<T>(new SystemId(DConst.RecorderPos), (in EventEnvelope env, in T e, in TickContext ctx) =>
            {
                object boxed = e;
                if (env.Source.Value == DConst.DelayPos)
                {
                    FromDelay.TryGetValue(typeof(T), out int n);
                    FromDelay[typeof(T)] = n + 1;
                }

                if (boxed is DelayEvent de)
                {
                    Delays.Add((env, de));
                    if (Keep)
                    {
                        All.Add(new Rec(env, boxed, de.Node.Subject.Value));
                    }

                    return;
                }

                ulong flight = flightOf(boxed);
                if (Keep)
                {
                    All.Add(new Rec(env, boxed, flight));
                }

                After?.Invoke(env, boxed, flight);
            });
        }

        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (Rec r in All)
            {
                sb.Append(r).Append('\n');
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// The scripted drivers at 2, 3, 4 and 5, sim.delay at 7 (or a probe around
    /// it), and the recorder at 9 unless <c>record</c> is false, in a real host.
    /// </summary>
    internal sealed class DelayRig
    {
        public readonly ISimHost Host;
        public readonly IDelaySystem Delay;
        public readonly Recorder? Rec;
        public readonly DelayProbe? Probe;
        public readonly ShimBus? Shim;
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        private readonly Driver[] _drivers;

        public DelayRig(
            Script script,
            ulong seed = 0x5EED_0024UL,
            bool record = true,
            HandlerTimer? handlerTimer = null,
            IRandomService? trapRng = null,
            bool probe = false)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, ContentIndexFactory.Create(new List<IContentDefinition>()), Sink, new NullLog()));
            _drivers = new[]
            {
                new Driver(DConst.SchedulePos), new Driver(DConst.AirsidePos), new Driver(DConst.FlowPos), new Driver(DConst.TurnaroundPos),
            };

            SystemServices services = b.Services;
            if (handlerTimer != null || trapRng != null)
            {
                Shim = new ShimBus(services.Events, handlerTimer, trapRng);
                services = new SystemServices(Shim, services.Ids, services.Content, services.Commands);
            }

            Delay = DelayFactory.CreateSystem(services);
            foreach (Driver d in _drivers)
            {
                b.Register(d);
            }

            if (probe || trapRng != null)
            {
                Probe = new DelayProbe(Delay) { Rng = trapRng };
                b.Register(Probe);
            }
            else
            {
                b.Register(Delay);
            }

            if (record)
            {
                Rec = new Recorder(b.Services.Events);
                b.Register(new ProbeSystem(DConst.RecorderPos));
            }

            Append(script);
            Host = b.Build();
        }

        public Recorder R => Rec!;

        /// <summary>Adds steps; each must be at or after the next tick to run.</summary>
        public void Append(Script script)
        {
            foreach (Driver d in _drivers)
            {
                d.Append(script.Steps);
            }
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

        /// <summary>
        /// Runs to <paramref name="tick"/>, then asserts that tick throws per 14
        /// §14.1 "What throw means" (Q-112): the host's SimInvariantException with
        /// that Tick and HasWorldHash true, wrapping sim.delay's own
        /// SimInvariantException with the same Tick and HasWorldHash false.
        /// </summary>
        public void AssertThrowsAt(ulong tick, string what)
        {
            RunTo(tick);
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => Host.Step(1));
            Assert.Equal(tick, ex.Tick);
            Assert.True(ex.HasWorldHash, what + ": the host's wrapper carries no world hash");
            Assert.True(ex.InnerException is SimInvariantException, what + ": expected sim.delay to throw SimInvariantException, got " + (ex.InnerException == null ? "nothing inside the host's wrapper" : ex.InnerException.GetType().Name + ": " + ex.InnerException.Message));
            var inner = (SimInvariantException)ex.InnerException!;
            Assert.Equal(tick, inner.Tick);
            Assert.False(inner.HasWorldHash, what + ": sim.delay's exception must leave HasWorldHash false");
        }

        public FlightDelay Record(ulong flight)
        {
            Assert.True(Delay.TryGetFlightDelay(new FlightId(flight), out FlightDelay r), "no delay record for flight " + flight.ToString(CultureInfo.InvariantCulture) + " at tick " + Host.CurrentTick.ToString(CultureInfo.InvariantCulture));
            return r;
        }

        public DelayNode Node(DelayEventId id)
        {
            Assert.True(Delay.TryGetNode(id, out DelayNode n), "no node " + id.Value.ToString(CultureInfo.InvariantCulture));
            return n;
        }

        public DelayNode Root(ulong flight)
        {
            return Node(Record(flight).Root);
        }

        /// <summary>The flight's allocation leaves, in LeavesOf order.</summary>
        public List<DelayNode> Leaves(ulong flight)
        {
            var list = new List<DelayNode>();
            foreach (DelayEventId id in Delay.LeavesOf(new FlightId(flight)))
            {
                list.Add(Node(id));
            }

            return list;
        }

        /// <summary>The flight's one leaf with this source; fails if there is not exactly one.</summary>
        public DelayNode Leaf(ulong flight, DelaySource source)
        {
            List<DelayNode> found = Leaves(flight).FindAll(n => n.Explanation.Source == source);
            Assert.True(found.Count == 1, "flight " + flight.ToString(CultureInfo.InvariantCulture) + ": expected one " + source + " leaf, found " + found.Count.ToString(CultureInfo.InvariantCulture) + "\n" + Show.Tree(Delay, flight));
            return found[0];
        }

        public List<(EventEnvelope Env, DelayEvent Evt)> DelayEventsFor(ulong flight)
        {
            return R.Delays.FindAll(d => d.Evt.Node.Subject.Value == flight);
        }
    }

    /// <summary>Expected nodes as Show.Node text, so a comparison covers every field at once.</summary>
    internal static class Expect
    {
        public static string Leaf(ulong id, ulong subject, ulong root, DelayCategory category, ulong ticks, bool rootCause, ulong linked, EventRef source, DelaySource src, ulong a, ulong b, ulong createdAt)
        {
            return Show.Node(new DelayNode(
                new DelayEventId(id),
                DelayNodeKind.Allocation,
                new FlightId(subject),
                new DelayEventId(root),
                category,
                ticks,
                DConst.Minutes(ticks),
                rootCause,
                new FlightId(linked),
                source,
                new DelayExplanation(src, a, b),
                createdAt));
        }

        /// <summary>A FlightTotal root (14 §14.3, §14.7).</summary>
        public static string Root(ulong id, ulong subject, ulong ticks, ulong createdAt)
        {
            return Show.Node(new DelayNode(
                new DelayEventId(id),
                DelayNodeKind.FlightTotal,
                new FlightId(subject),
                new DelayEventId(0UL),
                null,
                ticks,
                DConst.Minutes(ticks),
                false,
                new FlightId(0UL),
                EventRef.None,
                new DelayExplanation(DelaySource.FlightTotal, 0UL, 0UL),
                createdAt));
        }

        public static string Unexplained(ulong id, ulong subject, ulong root, ulong ticks, ulong createdAt)
        {
            return Leaf(id, subject, root, DelayCategory.Propagated, ticks, true, 0UL, EventRef.None, DelaySource.Unexplained, 0UL, 0UL, createdAt);
        }

        public static string Job(ulong id, ulong subject, ulong root, JobKind job, ResourceKind on, DelayCategory category, ulong ticks, Step opener, ulong createdAt)
        {
            return Leaf(id, subject, root, category, ticks, true, 0UL, opener.Ref, DelaySource.TurnaroundJobWait, (ulong)(int)job, (ulong)(int)on, createdAt);
        }

        public static List<string> LeafTexts(DelayRig rig, ulong flight)
        {
            return rig.Leaves(flight).ConvertAll(n => Show.Node(n));
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

    /// <summary>
    /// The repository root is the nearest ancestor of AppContext.BaseDirectory
    /// holding AirportSim.sln (07 "Fixture location", Q-031). A missing root
    /// fails the test.
    /// </summary>
    internal static class Repo
    {
        public static byte[] Read(params string[] relative)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            var parts = new List<string> { dir! };
            parts.AddRange(relative);
            return File.ReadAllBytes(Path.Combine(parts.ToArray()));
        }
    }

    /// <summary>Text forms, so a failing comparison shows the whole difference.</summary>
    internal static class Show
    {
        public static string Id(in EventRef r)
        {
            return r.HasValue ? Id(r.Id) : "-";
        }

        public static string Id(in EventId id)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", id.Tick, id.Sequence);
        }

        public static string Record(in FlightDelay r)
        {
            return RecordLine(
                r.Flight.Value,
                r.Kind,
                r.HasRotation,
                r.Rotation.Value,
                r.Root.Value,
                r.TotalTicks,
                r.TotalMinutes,
                r.CheckpointsReached,
                r.LastCheckpointActual,
                r.Finalised,
                r.FinalisedAt,
                r.MissedPassengers,
                r.MissedLastBlockedAt);
        }

        public static string RecordLine(ulong flight, MovementKind kind, bool hasRotation, ulong rotation, ulong root, ulong total, Fx totalMinutes, int checkpoints, ulong last, bool finalised, ulong finalisedAt, int missed, NodeId? missedAt)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "flight {0} {1} hasRot={2} rot={3} root={4} total={5} totalMin={6} cp={7} last={8} fin={9} finAt={10} missed={11} missedAt={12}",
                flight,
                kind,
                hasRotation,
                rotation,
                root,
                total,
                totalMinutes.Raw,
                checkpoints,
                last,
                finalised,
                finalisedAt,
                missed,
                missedAt.HasValue ? missedAt.Value.Value.ToString(CultureInfo.InvariantCulture) : "-");
        }

        public static string Node(in DelayNode n)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "node {0} {1} subj={2} parent={3} cat={4} ticks={5} min={6} rc={7} linked={8} src={9} expl={10}/{11}/{12} at={13}",
                n.Id.Value,
                n.Kind,
                n.Subject.Value,
                n.Parent.Value,
                n.Category.HasValue ? n.Category.Value.ToString() : "-",
                n.Ticks,
                n.Minutes.Raw,
                n.RootCause,
                n.LinkedFlight.Value,
                Id(n.SourceEvent),
                n.Explanation.Source,
                n.Explanation.A,
                n.Explanation.B,
                n.CreatedAt);
        }

        /// <summary>A flight's record, root and leaves, or "absent".</summary>
        public static string Tree(IDelaySystem d, ulong flight)
        {
            if (!d.TryGetFlightDelay(new FlightId(flight), out FlightDelay r))
            {
                return "flight " + flight.ToString(CultureInfo.InvariantCulture) + " absent";
            }

            var sb = new StringBuilder();
            sb.Append(Record(r)).Append('\n');
            sb.Append("  ").Append(d.TryGetNode(r.Root, out DelayNode root) ? Node(root) : "root missing").Append('\n');
            foreach (DelayEventId id in d.LeavesOf(new FlightId(flight)))
            {
                sb.Append("  ").Append(d.TryGetNode(id, out DelayNode n) ? Node(n) : "leaf " + id.Value.ToString(CultureInfo.InvariantCulture) + " missing").Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>Everything IDelaySystem exposes, one flight per block, ascending FlightId.</summary>
        public static List<string> Snapshot(IDelaySystem d)
        {
            var lines = new List<string>();
            foreach (FlightId f in d.RetainedFlights())
            {
                lines.Add(Tree(d, f.Value));
            }

            return lines;
        }
    }

    /// <summary>
    /// The invariants 06 and 14 state, checked through the public queries only.
    /// Each failure names the context it was checked in.
    /// </summary>
    internal static class Invariants
    {
        /// <summary>
        /// One flight's tree (14 §14.7): one FlightTotal root, allocation leaves
        /// parented to it, sum of leaves == TotalTicks == root ticks (06 rule 1,
        /// 14 §14.6), RootCause per §14.7, references backwards, depth capped.
        /// </summary>
        public static void CheckFlight(IDelaySystem d, ulong flight, string context)
        {
            try
            {
                CheckFlightCore(d, flight, context);
            }
            catch (Xunit.Sdk.XunitException ex)
            {
                // The tree text is built only on failure: CheckFlight runs after
                // every handler of long runs.
                throw new Xunit.Sdk.XunitException(ex.Message + "\n" + Show.Tree(d, flight));
            }
        }

        private static void CheckFlightCore(IDelaySystem d, ulong flight, string context)
        {
            var f = new FlightId(flight);
            Assert.True(d.TryGetFlightDelay(f, out FlightDelay r), context + ": flight " + flight.ToString(CultureInfo.InvariantCulture) + " has no record");
            string where = context;
            Assert.True(r.Flight == f, where);
            Assert.True(r.Root.Value != 0UL, "root id is DELAY_EVENT_ID_NONE: " + where);
            Assert.True(d.TryGetNode(r.Root, out DelayNode root), "root node missing: " + where);
            Assert.True(root.Kind == DelayNodeKind.FlightTotal && root.Subject == f && root.Parent.Value == 0UL && !root.Category.HasValue && !root.RootCause, "malformed root: " + where);
            Assert.True(root.Explanation.Source == DelaySource.FlightTotal && root.Explanation.A == 0UL && root.Explanation.B == 0UL && root.LinkedFlight.Value == 0UL && !root.SourceEvent.HasValue, "malformed root: " + where);
            Assert.True(root.Ticks == r.TotalTicks, "FlightTotal.Ticks != TotalTicks: " + where);
            Assert.True(root.Minutes == DConst.Minutes(root.Ticks) && r.TotalMinutes == DConst.Minutes(r.TotalTicks), "derived minutes wrong: " + where);
            int count = DConst.Checkpoints(r.Kind).Length;
            Assert.True(r.CheckpointsReached >= 0 && r.CheckpointsReached <= count, "CheckpointsReached out of range: " + where);
            Assert.True(r.Finalised == (r.CheckpointsReached == count), "Finalised disagrees with CheckpointsReached: " + where);
            Assert.True(r.Finalised == (r.FinalisedAt != DConst.TickUnscheduled), "FinalisedAt disagrees with Finalised: " + where);
            Assert.True(r.MissedPassengers >= 0, where);

            ulong sum = 0UL;
            ulong previous = 0UL;
            int unexplained = 0;
            int inbound = 0;
            foreach (DelayEventId id in d.LeavesOf(f))
            {
                Assert.True(id.Value > previous, "LeavesOf not strictly ascending: " + where);
                previous = id.Value;
                Assert.True(d.TryGetNode(id, out DelayNode n), "orphan leaf id " + id.Value.ToString(CultureInfo.InvariantCulture) + ": " + where);
                Assert.True(n.Id == id && n.Kind == DelayNodeKind.Allocation && n.Subject == f, "malformed leaf: " + where);
                Assert.True(n.Parent == r.Root, "leaf parent is not the flight's root: " + where);
                Assert.True(n.Parent.Value < n.Id.Value, "leaf id not above its parent's: " + where);
                Assert.True(n.Ticks > 0UL, "zero-tick leaf retained (14 §14.6 step 6): " + where);
                Assert.True(n.Category.HasValue, "leaf with no category: " + where);
                Assert.True(n.Minutes == DConst.Minutes(n.Ticks), "leaf minutes wrong: " + where);
                bool linked = n.Explanation.Source == DelaySource.InboundAircraft && n.LinkedFlight.Value != 0UL;
                Assert.True(n.RootCause == !linked, "RootCause wrong (14 §14.7): " + where);
                Assert.True(n.Explanation.Source != DelaySource.FlightTotal, where);
                if (n.Explanation.Source == DelaySource.Unexplained)
                {
                    unexplained++;
                    Assert.True(n.Category == DelayCategory.Propagated && n.LinkedFlight.Value == 0UL && !n.SourceEvent.HasValue, "malformed Unexplained leaf: " + where);
                }
                else if (n.Explanation.Source == DelaySource.InboundAircraft)
                {
                    inbound++;
                    Assert.True(n.Category == DelayCategory.LateInbound && !n.SourceEvent.HasValue, "malformed InboundAircraft leaf: " + where);
                    Assert.True(linked && r.Kind == MovementKind.Departure && r.HasRotation && n.LinkedFlight == r.Rotation && n.Explanation.A == r.Rotation.Value, "InboundAircraft leaf not linked to the rotation: " + where);
                    Assert.True(d.TryGetFlightDelay(n.LinkedFlight, out FlightDelay linkedRecord), "dangling LinkedFlight: " + where);
                    Assert.True(linkedRecord.Root.Value < n.Id.Value, "LinkedFlight's root not below the leaf: " + where);
                    Assert.True(linkedRecord.Finalised && n.Ticks <= linkedRecord.TotalTicks, "late_inbound exceeds the inbound's own delay: " + where);
                }
                else
                {
                    Assert.True(n.SourceEvent.HasValue && n.LinkedFlight.Value == 0UL, "interval leaf without SourceEvent: " + where);
                }

                sum += n.Ticks;
            }

            Assert.True(unexplained <= 1 && inbound <= 1, "more than one Unexplained or InboundAircraft leaf: " + where);
            Assert.True(sum == r.TotalTicks, "sum of leaves " + sum.ToString(CultureInfo.InvariantCulture) + " != total " + r.TotalTicks.ToString(CultureInfo.InvariantCulture) + ": " + where);
            Assert.True(ChainDepth(d, flight, 0) <= DConst.MaxAttributionDepth, "ChainDepth above MAX_ATTRIBUTION_DEPTH: " + where);
        }

        /// <summary>14 §14.7: 1 with no leaves, else max over leaves of 2, or 2 + ChainDepth(LinkedFlight).</summary>
        public static int ChainDepth(IDelaySystem d, ulong flight, int guard)
        {
            Assert.True(guard < 64, "LinkedFlight chain does not terminate at flight " + flight.ToString(CultureInfo.InvariantCulture));
            int depth = 1;
            foreach (DelayEventId id in d.LeavesOf(new FlightId(flight)))
            {
                Assert.True(d.TryGetNode(id, out DelayNode n));
                int here = n.LinkedFlight.Value != 0UL ? 2 + ChainDepth(d, n.LinkedFlight.Value, guard + 1) : 2;
                depth = Math.Max(depth, here);
            }

            return depth;
        }

        /// <summary>Every retained flight, plus the cross-flight rules: ascending RetainedFlights, unique node ids.</summary>
        public static void CheckAll(IDelaySystem d, string context)
        {
            ulong previous = 0UL;
            var ids = new HashSet<ulong>();
            foreach (FlightId f in d.RetainedFlights())
            {
                Assert.True(f.Value > previous, context + ": RetainedFlights not strictly ascending");
                previous = f.Value;
                CheckFlight(d, f.Value, context);
                Assert.True(d.TryGetFlightDelay(f, out FlightDelay r));
                Assert.True(ids.Add(r.Root.Value), context + ": node id used twice: " + r.Root.Value.ToString(CultureInfo.InvariantCulture));
                foreach (DelayEventId id in d.LeavesOf(f))
                {
                    Assert.True(ids.Add(id.Value), context + ": node id used twice: " + id.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>Every retained node: roots and leaves of every retained flight.</summary>
        public static List<DelayNode> AllNodes(IDelaySystem d)
        {
            var nodes = new List<DelayNode>();
            foreach (FlightId f in d.RetainedFlights())
            {
                Assert.True(d.TryGetFlightDelay(f, out FlightDelay r));
                Assert.True(d.TryGetNode(r.Root, out DelayNode root));
                nodes.Add(root);
                foreach (DelayEventId id in d.LeavesOf(f))
                {
                    Assert.True(d.TryGetNode(id, out DelayNode n));
                    nodes.Add(n);
                }
            }

            nodes.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            return nodes;
        }
    }
}
