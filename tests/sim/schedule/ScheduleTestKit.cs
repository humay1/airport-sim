using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    // Shared test doubles and rigs for the T-008 suite. Everything here is
    // written from 11-interfaces-schedule.md, 08-interfaces-core.md and
    // 09 §9.7 only. sim.core publishes no null IContentIndex, ICheckpointSink
    // or ISimLog (08 §8.11a), so tests supply their own.

    internal static class SchedConst
    {
        // 11 §11.2 and 08 §8.1, as literals: the spec names no class for the
        // sim.schedule constants, so the tests do not reference one.
        public const ulong TicksPerDay = 14400UL;
        public const ulong TicksPerMinute = 10UL;
        public const ulong PublishLead = 14400UL;
        public const ulong DayStride = 100000UL;
        public const ulong TickUnscheduled = ulong.MaxValue;
        public const ushort ScheduleSystemId = 2;
        public const ushort FlowSystemId = 4;
        public const ushort RecorderSystemId = 7;
    }

    /// <summary>
    /// Test-owned content. The ids match data/aircraft and data/pax_profiles so
    /// the Phase 0 fixture references ids that exist (11 §11.10); the curves are
    /// fixed here so that the expected splits are literals of this suite, not
    /// of whatever data/ holds later.
    /// </summary>
    internal static class ScheduleContent
    {
        public static readonly string[] AircraftIds =
        {
            "a320", "a321", "a359", "a388", "atr72", "b738", "b744", "b789", "crj900",
        };

        // (minutes_before_std, share_permille), strictly ascending minutes.
        public static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        public static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        // Test-only profile with one bucket, to isolate the class split.
        public static readonly (uint Minutes, uint Share)[] Single =
        {
            (60, 1000),
        };

        public static readonly Dictionary<string, (uint Minutes, uint Share)[]> Curves =
            new Dictionary<string, (uint Minutes, uint Share)[]>(StringComparer.Ordinal)
            {
                { "business", Business },
                { "leisure", Leisure },
                { "single", Single },
            };

        public static IContentIndex Index()
        {
            var defs = new List<IContentDefinition>();
            defs.Add(new SizeCategoryDefinition(new ContentId("size_c"), 3));
            foreach (string id in AircraftIds)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId("size_c")));
            }

            foreach (KeyValuePair<string, (uint Minutes, uint Share)[]> kv in Curves)
            {
                var buckets = new List<ShowUpBucket>();
                foreach ((uint m, uint s) in kv.Value)
                {
                    buckets.Add(new ShowUpBucket(m, s));
                }

                defs.Add(new PaxProfileDefinition(new ContentId(kv.Key), Fx.FromRatio(13, 10), buckets));
            }

            return ContentIndexFactory.Create(defs);
        }
    }

    internal static class Fixture
    {
        public const string SourceName = "phase0-200.csv";

        private static byte[]? _bytes;

        /// <summary>tests/fixtures/schedule/phase0-200.csv, found by walking up from the test binaries.</summary>
        public static byte[] Bytes()
        {
            if (_bytes == null)
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null)
                {
                    string candidate = Path.Combine(dir, "tests", "fixtures", "schedule", "phase0-200.csv");
                    if (File.Exists(candidate))
                    {
                        _bytes = File.ReadAllBytes(candidate);
                        break;
                    }

                    dir = Path.GetDirectoryName(dir);
                }

                if (_bytes == null)
                {
                    throw new InvalidOperationException("tests/fixtures/schedule/phase0-200.csv not found above " + AppContext.BaseDirectory);
                }
            }

            return (byte[])_bytes.Clone();
        }

        public static string Text()
        {
            return Encoding.UTF8.GetString(Bytes());
        }

        /// <summary>The fixture with its data rows permuted by a seeded SplitMix64 Fisher-Yates shuffle.</summary>
        public static byte[] Permuted(ulong seed)
        {
            string[] lines = Text().Split('\n');
            var rows = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    rows.Add(lines[i]);
                }
            }

            var rng = new SplitMix64(seed);
            for (int i = rows.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.Next() % (ulong)(i + 1));
                (rows[i], rows[j]) = (rows[j], rows[i]);
            }

            var sb = new StringBuilder();
            sb.Append(lines[0]).Append('\n');
            foreach (string r in rows)
            {
                sb.Append(r).Append('\n');
            }

            return Csv.Utf8(sb.ToString());
        }
    }

    /// <summary>The pinned SplitMix64 of 08 §8.8, as L4 requires for test inputs.</summary>
    internal sealed class SplitMix64
    {
        private ulong _x;

        public SplitMix64(ulong seed)
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
    }

    internal static class Fnv
    {
        /// <summary>Plain FNV-1a-64 over raw bytes, no length prefix (11 §11.4 FixtureHash).</summary>
        public static ulong Raw64(byte[] bytes)
        {
            ulong h = 0xCBF29CE484222325UL;
            unchecked
            {
                foreach (byte b in bytes)
                {
                    h = (h ^ b) * 0x100000001B3UL;
                }
            }

            return h;
        }

        /// <summary>FNV-1a-32 of the ordinal (UTF-8) bytes, the airline mapping of 11 §11.4.</summary>
        public static uint Airline32(string code)
        {
            uint h = 0x811C9DC5U;
            unchecked
            {
                foreach (byte b in Encoding.UTF8.GetBytes(code))
                {
                    h = (h ^ b) * 0x01000193U;
                }
            }

            return h;
        }
    }

    /// <summary>
    /// The 08 §8.9 encoding, independent of StateHasher: each value as 8
    /// little-endian bytes.
    /// </summary>
    internal sealed class FnvWords
    {
        private ulong _h = 0xCBF29CE484222325UL;

        public ulong Result => _h;

        public void U64(ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    _h = (_h ^ (byte)(v >> (8 * i))) * 0x100000001B3UL;
                }
            }
        }

        public void I64(long v)
        {
            U64(unchecked((ulong)v));
        }
    }

    internal static class Csv
    {
        public const string Header =
            "flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node";

        public static byte[] Utf8(string s)
        {
            return new UTF8Encoding(false).GetBytes(s);
        }

        /// <summary>The byte-exact header, then each row, each followed by LF.</summary>
        public static byte[] Of(params string[] rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (string r in rows)
            {
                sb.Append(r).Append('\n');
            }

            return Utf8(sb.ToString());
        }

        /// <summary>
        /// One row with valid defaults. A departure defaults to 100 pax at
        /// entry node 1; an arrival to 0 pax and no entry node.
        /// </summary>
        public static string Row(
            string flightRef,
            string movement = "D",
            string sched = "12:00",
            string rotation = "",
            string day = "0",
            string repeat = "0",
            string airline = "NVA",
            string aircraft = "a320",
            string minTurn = "35",
            string? pax = null,
            string profile = "business",
            string hold = "500",
            string assist = "10",
            string? entry = null)
        {
            bool dep = movement == "D";
            return string.Join(",", new[]
            {
                flightRef, day, repeat, movement, airline, aircraft, sched, rotation, minTurn,
                pax ?? (dep ? "100" : "0"), profile, dep ? hold : "0", dep ? assist : "0", entry ?? (dep ? "1" : ""),
            });
        }

        /// <summary>A valid linked arrival/departure pair.</summary>
        public static string[] Pair(string arr, string dep, string sta, string std, string repeat = "0", string day = "0")
        {
            return new[]
            {
                Row(arr, "A", sta, dep, day: day, repeat: repeat),
                Row(dep, "D", std, arr, day: day, repeat: repeat),
            };
        }
    }

    internal static class Load
    {
        public static ScheduleTable Table(byte[] csv, string source = "test.csv")
        {
            return ScheduleFactory.CreateLoader().Load(csv, source);
        }

        /// <summary>
        /// Asserts a §11.4 hard load failure: FormatException (07 "Error
        /// handling", Q-030) whose message starts with "sourceName: " and names
        /// one of <paramref name="lines"/> as "line n".
        /// </summary>
        public static FormatException AssertFails(byte[] csv, string source, params int[] lines)
        {
            IScheduleLoader loader = ScheduleFactory.CreateLoader();
            FormatException ex = Assert.Throws<FormatException>(() => loader.Load(csv, source));
            Assert.StartsWith(source + ": ", ex.Message, StringComparison.Ordinal);
            if (lines.Length > 0)
            {
                string alternatives = string.Join("|", Array.ConvertAll(lines, l => l.ToString(CultureInfo.InvariantCulture)));
                Assert.True(
                    Regex.IsMatch(ex.Message, @"(?<![A-Za-z0-9_])line (" + alternatives + @")(?![0-9])"),
                    "expected the message to name line " + string.Join(" or ", Array.ConvertAll(lines, l => l.ToString(CultureInfo.InvariantCulture))) + ": " + ex.Message);
            }

            return ex;
        }
    }

    internal static class Describe
    {
        public static string Record(in FlightRecord r)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "id={0} airline={1} ac={2} kind={3} day={4} sched={5} pub={6} rot={7} hasRot={8} mt={9} prof={10} pax={11} hold={12} assist={13} entry={14}",
                r.Id.Value,
                r.Airline.Value,
                r.AircraftType.Value,
                r.Kind,
                r.DayIndex,
                r.ScheduledTick,
                r.PublishTick,
                r.Rotation.Value,
                r.HasRotation,
                r.MinTurnaround.Raw,
                r.PaxProfile.Value,
                r.PaxCount,
                r.HoldBagPermille,
                r.AssistPermille,
                r.Kind == MovementKind.Departure ? r.EntryNode.Value.ToString(CultureInfo.InvariantCulture) : "-");
        }

        public static List<ulong> Ids(IReadOnlyList<FlightId> ids)
        {
            var list = new List<ulong>(ids.Count);
            foreach (FlightId id in ids)
            {
                list.Add(id.Value);
            }

            return list;
        }
    }

    /// <summary>A pure clock over a settable tick, per 08 §8.2.</summary>
    internal sealed class TestClock : ISimClock
    {
        public ulong CurrentTick { get; set; }

        public uint DayIndex => (uint)(CurrentTick / SchedConst.TicksPerDay);

        public uint SecondOfDay => (uint)((CurrentTick % SchedConst.TicksPerDay) * 6UL);

        public Fx MinutesBetween(ulong a, ulong b)
        {
            return Fx.FromRatio((long)b - (long)a, (long)SchedConst.TicksPerMinute);
        }

        public ulong TickOfDayTime(uint dayIndex, uint secondOfDay)
        {
            if (secondOfDay >= 86400U)
            {
                throw new ArgumentOutOfRangeException(nameof(secondOfDay));
            }

            return (dayIndex * SchedConst.TicksPerDay) + (secondOfDay / 6U);
        }
    }

    /// <summary>Assigns EventIds like the bus (08 §8.6): (tick, sequence from 0 per tick).</summary>
    internal abstract class TestPublisher : IEventPublisher
    {
        private readonly TestClock _clock;
        private ulong _tick = ulong.MaxValue;
        private uint _seq;

        protected TestPublisher(TestClock clock)
        {
            _clock = clock;
        }

        public EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent
        {
            if (_clock.CurrentTick != _tick)
            {
                _tick = _clock.CurrentTick;
                _seq = 0;
            }

            var id = new EventId(_tick, _seq++);
            OnPublish(id, evt, cause);
            return id;
        }

        protected abstract void OnPublish<T>(EventId id, in T evt, in EventRef cause) where T : struct, ISimEvent;
    }

    /// <summary>Counts events without allocating.</summary>
    internal sealed class CountingPublisher : TestPublisher
    {
        public int Plans;
        public int Milestones;
        public int Others;

        public CountingPublisher(TestClock clock) : base(clock)
        {
        }

        protected override void OnPublish<T>(EventId id, in T evt, in EventRef cause)
        {
            if (typeof(T) == typeof(FlightPlanPublished))
            {
                Plans++;
            }
            else if (typeof(T) == typeof(FlightMilestoneReached))
            {
                Milestones++;
            }
            else
            {
                Others++;
            }
        }
    }

    internal sealed class RecordingPublisher : TestPublisher
    {
        public readonly List<(EventId Id, EventRef Cause, object Event)> Events = new List<(EventId Id, EventRef Cause, object Event)>();

        public RecordingPublisher(TestClock clock) : base(clock)
        {
        }

        protected override void OnPublish<T>(EventId id, in T evt, in EventRef cause)
        {
            Events.Add((id, cause, evt));
        }
    }

    /// <summary>Counts every RNG access. sim.schedule must make none (11 §11.9).</summary>
    internal sealed class TrapRandomService : IRandomService
    {
        public int StreamCalls;
        public int Draws;
        private readonly TrapStream _stream;

        public TrapRandomService()
        {
            _stream = new TrapStream(this);
        }

        public ulong MasterSeed => 0x5EED_0008UL;

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

    internal readonly struct RecordedEvent
    {
        public RecordedEvent(bool isPlan, EventEnvelope env, FlightPlanPublished plan, FlightMilestoneReached milestone)
        {
            IsPlan = isPlan;
            Env = env;
            Plan = plan;
            Milestone = milestone;
        }

        public bool IsPlan { get; }

        public EventEnvelope Env { get; }

        public FlightPlanPublished Plan { get; }

        public FlightMilestoneReached Milestone { get; }

        public ulong Flight => IsPlan ? Plan.Flight.Value : Milestone.Flight.Value;

        public override string ToString()
        {
            string cause = Env.Cause.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", Env.Cause.Id.Tick, Env.Cause.Id.Sequence)
                : "-";
            return IsPlan
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "plan t={0} id={1}.{2} src={3} cause={4} f={5} k={6} rot={7}/{8} al={9} ac={10} arr={11} dep={12} mt={13}",
                    Env.Tick, Env.Id.Tick, Env.Id.Sequence, Env.Source.Value, cause, Plan.Flight.Value, Plan.Kind, Plan.Rotation.Value,
                    Plan.HasRotation, Plan.Airline.Value, Plan.AircraftType.Value, Plan.SchedArr, Plan.SchedDep, Plan.MinTurnaround.Raw)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "ms t={0} id={1}.{2} src={3} cause={4} f={5} m={6} planned={7} actual={8}",
                    Env.Tick, Env.Id.Tick, Env.Id.Sequence, Env.Source.Value, cause, Milestone.Flight.Value, Milestone.Milestone,
                    Milestone.PlannedTick, Milestone.ActualTick);
        }
    }

    /// <summary>Subscribes, as a probe at position 7, to the two sim.schedule events.</summary>
    internal sealed class EventLog
    {
        public readonly List<RecordedEvent> Events = new List<RecordedEvent>();

        public EventLog(IEventBus bus, ushort subscriber)
        {
            var id = new SystemId(subscriber);
            List<RecordedEvent> events = Events;
            bus.Subscribe<FlightPlanPublished>(id, (in EventEnvelope env, in FlightPlanPublished evt, in TickContext ctx) =>
                events.Add(new RecordedEvent(true, env, evt, default)));
            bus.Subscribe<FlightMilestoneReached>(id, (in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx) =>
                events.Add(new RecordedEvent(false, env, default, evt)));
        }

        public List<string> Trace()
        {
            return Events.ConvertAll(e => e.ToString());
        }
    }

    internal readonly struct Injection
    {
        public Injection(ulong tick, CohortKey key, int count, uint at)
        {
            Tick = tick;
            Key = key;
            Count = count;
            At = at;
        }

        public ulong Tick { get; }

        public CohortKey Key { get; }

        public int Count { get; }

        public uint At { get; }

        public int ClassIndex => (Key.HasHoldBaggage ? 2 : 0) + (Key.RequiresAssistance ? 1 : 0);

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "t={0} f={1} dir={2} prof={3} class={4} n={5} at={6}",
                Tick, Key.Flight.Value, Key.Direction, Key.PaxProfile.Value, ClassIndex, Count, At);
        }
    }

    /// <summary>
    /// A stand-in IFlowSystem (09 §9.7) registered at position 4. It records
    /// every Inject with the tick it happened in. It learns the tick from its
    /// own Tick, which runs after sim.schedule's in the same tick, so an
    /// Inject during tick t sees NextTick == t.
    /// </summary>
    internal sealed class RecordingFlow : IFlowSystem
    {
        public readonly List<Injection> Injections;
        public readonly bool Recording;
        public ulong NextTick;
        public long InjectCalls;
        public long InjectedTotal;
        public int BadCalls;
        private ulong _cohorts;

        public RecordingFlow(bool recording = true)
        {
            Recording = recording;
            Injections = new List<Injection>(recording ? 8192 : 0);
        }

        public SystemId Id => new SystemId(SchedConst.FlowSystemId);

        public string Name => "probe.flow";

        public void Tick(in TickContext ctx)
        {
            NextTick = ctx.Tick + 1UL;
        }

        public ulong ComputeStateHash()
        {
            return (ulong)InjectedTotal;
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
            outstanding = default;
            return false;
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            lanes = default;
            return false;
        }

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            InjectCalls++;
            InjectedTotal += count;
            if (count <= 0)
            {
                BadCalls++;
            }

            if (Recording)
            {
                Injections.Add(new Injection(NextTick, key, count, at.Value));
            }

            return new CohortId(++_cohorts);
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            return 0;
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            return Array.Empty<AgentView>();
        }
    }

    /// <summary>sim.schedule inside a real host, optionally with the stand-in flow and an event recorder.</summary>
    internal sealed class HostRig
    {
        public readonly ISimHost Host;
        public readonly IScheduleSystem Schedule;
        public readonly ScheduleTable Table;
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        public readonly RecordingFlow? Flow;
        public readonly EventLog? Events;

        public HostRig(byte[] csv, string source = Fixture.SourceName, bool withFlow = false, bool record = true, ulong seed = 0x5EED_0008UL)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(seed, ScheduleContent.Index(), Sink, new NullLog()));
            Table = ScheduleFactory.CreateLoader().Load(csv, source);
            Flow = withFlow ? new RecordingFlow() : null;
            Schedule = ScheduleFactory.CreateSystem(b.Services, Table, Flow);
            b.Register(Schedule);
            if (Flow != null)
            {
                b.Register(Flow);
            }

            if (record)
            {
                Events = new EventLog(b.Services.Events, SchedConst.RecorderSystemId);
                b.Register(new ProbeSystem(SchedConst.RecorderSystemId));
            }

            Host = b.Build();
        }

        public void RunTo(ulong tick)
        {
            while (Host.CurrentTick < tick)
            {
                Host.Step((uint)Math.Min(tick - Host.CurrentTick, 1000000UL));
            }
        }

        public FlightRecord Flight(ulong id)
        {
            Assert.True(Schedule.TryGetFlight(new FlightId(id), out FlightRecord r), "TryGetFlight(" + id.ToString(CultureInfo.InvariantCulture) + ") returned false");
            return r;
        }
    }

    /// <summary>
    /// sim.schedule driven directly through ISimSystem.Tick with a
    /// test-built TickContext (08 §8.5), outside any host, so RNG use,
    /// allocation and time are attributable to sim.schedule alone.
    /// </summary>
    internal sealed class DirectRig
    {
        public readonly IScheduleSystem Schedule;
        public readonly TestClock Clock = new TestClock();
        public readonly IEventPublisher Publisher;
        public readonly IRandomService Rng;
        public readonly IContentIndex Content;
        public readonly ISimLog Log = new NullLog();
        public readonly RecordingFlow? Flow;
        public ulong NextTick;

        public DirectRig(byte[] csv, RecordingFlow? flow = null, bool counting = false, IRandomService? rng = null)
        {
            Content = ScheduleContent.Index();
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(0x5EED_0008UL, Content, new RecordingCheckpointSink(), Log));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(csv, Fixture.SourceName);
            Flow = flow;
            Schedule = ScheduleFactory.CreateSystem(b.Services, table, flow);
            Publisher = counting ? new CountingPublisher(Clock) : new RecordingPublisher(Clock);
            Rng = rng ?? new TrapRandomService();
        }

        public void TickOnce()
        {
            Clock.CurrentTick = NextTick;
            if (Flow != null)
            {
                Flow.NextTick = NextTick;
            }

            var ctx = new TickContext(NextTick, Clock, Publisher, Rng, Content, Log);
            Schedule.Tick(ctx);
            NextTick++;
        }

        public void RunTo(ulong tick)
        {
            while (NextTick < tick)
            {
                TickOnce();
            }
        }
    }
}
