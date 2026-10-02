using System;
using System.Collections.Generic;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// The soak kit of 19 §19.7: §19.6's kit composition built over the soak
    /// fixture set of §19.2b (tests/fixtures/soak/) instead of the Phase 0 set,
    /// in §19.2a's construction and registration order, with §19.6's position-3
    /// probe. Written from 08 §8.5/§8.7/§8.9/§8.11, 09 §9.7/§9.11, 11
    /// §11.7/§11.9a, 16 §16.8 and 18, never from an implementation.
    ///
    /// <para><b>The soak fixture set</b> (fixture sizing, not balance; 03 "The
    /// soak fixture", §19.2b). The walk graph is the Phase 0 one. The flow graph
    /// is the Phase 0 one with its only Sink at NodeId(9), and with two of the
    /// three servers open on each security lane. soak.csv is 150 rotations, all
    /// day 0 with repeat_daily 1: 300 movements and 32 150 departing passengers a
    /// day, in five banks of 30 departures 4 minutes apart, from 06:00, 09:30,
    /// 13:00, 16:30 and 20:00. That is mid-tier: between Phase 0 (200 movements,
    /// 20 456 passengers) and max tier (01: 800 movements, 90 000 passengers).
    /// The sizing derivation is in
    /// <see cref="SoakTests.test_soak_fixture_sized_under_a_tenth_of_a_millisecond_per_tick"/>.</para>
    /// </summary>
    internal static class SoakKit
    {
        internal const ulong Seed = 12345;

        internal const string ContentManifestPath = "tests/fixtures/soak/soak-content.files";
        internal const string ContentDirectory = "tests/fixtures/soak/soak-content";
        internal const string WorldPath = "tests/fixtures/soak/soak-landside.json";
        internal const string FlowPath = "tests/fixtures/soak/soak-landside.flow.json";
        internal const string SchedulePath = "tests/fixtures/soak/soak.csv";

        /// <summary>Movements per day in soak.csv: 150 A and 150 D rows.</summary>
        internal const int DailyMovements = 300;

        /// <summary>Departures per day in soak.csv.</summary>
        internal const int DailyDepartures = 150;

        /// <summary>Departing passengers per day in soak.csv, the sum of its D rows' pax.</summary>
        internal const long DailyDepartingPax = 32150;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static IContentIndex? _content;

        private static string? _dayFinalHash;

        private static KitDump? _dayDump;

        /// <summary>
        /// §19.2a's content source over the soak manifest: Files() is exactly the
        /// manifest's lines, each a path under soak-content/, and ReadAll returns
        /// those files' bytes.
        /// </summary>
        internal sealed class ManifestSource : IContentSource
        {
            private readonly List<string> _files = new List<string>();
            private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public ManifestSource()
            {
                byte[] raw = KillGateKit.Fixture(ContentManifestPath);
                Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "soak manifest has a BOM");
                string manifest = Encoding.UTF8.GetString(raw);
                Assert.EndsWith("\n", manifest);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in manifest.Substring(0, manifest.Length - 1).Split('\n'))
                {
                    Assert.False(line.Length == 0 || line.IndexOf('\r') >= 0, "bad manifest line: '" + line + "'");
                    Assert.True(seen.Add(line), "duplicate manifest line: '" + line + "'");
                    _files.Add(line);
                    _bytes.Add(line, KillGateKit.Fixture(ContentDirectory + "/" + line));
                }
            }

            public IReadOnlyList<string> Files()
            {
                return _files;
            }

            public byte[] ReadAll(string path)
            {
                return (byte[])_bytes[path].Clone();
            }
        }

        /// <summary>The kit content, loaded once, as the CLI loads it once per invocation (§19.2a "When").</summary>
        internal static IContentIndex Content()
        {
            if (_content == null)
            {
                _content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(new ManifestSource()));
            }

            return _content;
        }

        /// <summary>HarnessGates.FinalHash of the soak kit content and a fresh soak composer (§19.7 "Equivalence").</summary>
        internal static string FinalHash(uint ticks)
        {
            return HarnessGates.FinalHash(Content(), new SoakComposer().Compose, Seed, ticks);
        }

        /// <summary>
        /// <see cref="FinalHash"/> of one sim-day, computed once for the test
        /// assembly, which runs its tests one at a time. The run is deterministic,
        /// so every test that compares with it sees the same value.
        /// </summary>
        internal static string DayFinalHash()
        {
            if (_dayFinalHash == null)
            {
                _dayFinalHash = FinalHash(HarnessTestKit.TicksPerDay);
            }

            return _dayFinalHash;
        }

        /// <summary><see cref="RunDump"/> of one sim-day, computed once, as <see cref="DayFinalHash"/> is.</summary>
        internal static KitDump DayDump()
        {
            if (_dayDump == null)
            {
                _dayDump = RunDump(1);
            }

            return _dayDump;
        }

        /// <summary>
        /// The soak kit composer: §19.2a's composer over the soak set, with the
        /// soak set's sourceNames, and §19.6's probe at position 3. Every call
        /// parses the fixtures afresh and builds fresh systems. It keeps the
        /// systems of its latest call for the load check.
        /// </summary>
        internal sealed class SoakComposer
        {
            private readonly byte[] _schedule = KillGateKit.Fixture(SchedulePath);
            private readonly byte[] _world = KillGateKit.Fixture(WorldPath);
            private readonly byte[] _flow = KillGateKit.Fixture(FlowPath);

            public int Calls { get; private set; }

            public IWorldSystem? World { get; private set; }

            public IScheduleSystem? Schedule { get; private set; }

            public KillGateKit.BoardingProbe? Boarding { get; private set; }

            public IFlowSystem? Flow { get; private set; }

            public void Compose(ISimHostBuilder builder)
            {
                Calls++;
                SystemServices services = builder.Services;

                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, "soak-landside.json");
                IWorldSystem world = WorldFactory.CreateSystem(services, walk);
                FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, "soak-landside.flow.json", world);
                IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, "soak.csv");
                IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);
                var boarding = new KillGateKit.BoardingProbe(schedule, flow);

                builder.Register(world);
                builder.Register(schedule);
                builder.Register(boarding);
                builder.Register(flow);

                World = world;
                Schedule = schedule;
                Boarding = boarding;
                Flow = flow;
            }
        }

        private sealed class DiscardLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }

        /// <summary>One kit run rendered as a 16 §16.8 dump, with its final WorldStateHash().</summary>
        internal sealed class KitDump
        {
            public KitDump(string[] lines, int checkpoints, ulong final, SoakComposer composer)
            {
                Lines = lines;
                Checkpoints = checkpoints;
                Final = final;
                Composer = composer;
            }

            /// <summary>The dump's lines, without their LFs.</summary>
            public string[] Lines { get; }

            public int Checkpoints { get; }

            public ulong Final { get; }

            public SoakComposer Composer { get; }
        }

        /// <summary>
        /// One run as §19.2b makes it, built by the test itself: one host from
        /// SimHostFactory.CreateBuilder with seed 12345, the kit content, a
        /// recording sink and a discarding log (§19.1); one composer call; §19.2's
        /// NoOp script for <c>days × TICKS_PER_SIM_DAY</c> ticks; then
        /// Step(TICKS_PER_SIM_DAY) per day, since chunking is invisible (08
        /// §8.2). The dump is rendered by CheckpointsKit.Render from 16 §16.8's
        /// format, so its systems line names the kit's probe at position 3, not
        /// the CLI's stand-in.
        /// </summary>
        internal static KitDump RunDump(uint days)
        {
            uint ticks = days * HarnessTestKit.TicksPerDay;
            var sink = new CheckpointsKit.RecordingSink();
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(new SimHostConfig(Seed, Content(), sink, new DiscardLog()));
            var composer = new SoakComposer();
            composer.Compose(builder);
            ISimHost host = builder.Build();

            for (uint t = 100; t < ticks; t += 100)
            {
                var cmd = new Command(t, SimConstants.PLAYER_LOCAL, CommandKind.NoOp, Array.Empty<byte>());
                Assert.True(host.TrySubmit(cmd, out CommandRejection reason), "NoOp at " + t + " rejected: " + reason);
            }

            for (uint d = 0; d < days; d++)
            {
                host.Step(HarnessTestKit.TicksPerDay);
            }

            Assert.Equal((ulong)ticks, host.CurrentTick);
            ulong final = host.WorldStateHash();

            var names = new List<string>
            {
                composer.World!.Name,
                composer.Schedule!.Name,
                composer.Boarding!.Name,
                composer.Flow!.Name,
            };
            string text = Utf8NoBom.GetString(CheckpointsKit.Render(Seed, names, sink));
            Assert.EndsWith("\n", text);
            return new KitDump(text.Substring(0, text.Length - 1).Split('\n'), sink.Ticks.Count, final, composer);
        }

        /// <summary>
        /// The live-cohort ceiling at the end of a run of whole sim-days (09 §9.10,
        /// §19.6, §19.7), derived from the fixtures:
        /// <list type="bullet">
        /// <item>A run of whole days ends at midnight, at tick <c>end</c>. A
        /// departure's passengers are injected no earlier than STD − 180 min, the
        /// longest show-up offset in the soak pax profiles (11 §11.6), and Absorb
        /// at STD removes every Departing cohort of the flight, boarded or missed
        /// (09 §9.7). So at <c>end</c>, only departures with STD in
        /// [end, end + 1 800 ticks] can hold passengers. <c>flights</c> counts the
        /// day's departures whose STD is at most 1 800 ticks after midnight. In
        /// soak.csv the first STD is 06:00, so <c>flights</c> is 0, and so is the
        /// ceiling: every queue, corridor and gate is empty at midnight.</item>
        /// <item>A flight has one pax profile, so at most 4 cohort keys (hold
        /// baggage × assistance, 09 §9.2).</item>
        /// <item>A key has at most one cohort on each of the 7 non-Corridor nodes
        /// (mandatory merging, 09 §9.3), and at most one per entry tick still
        /// crossing each of the 2 Corridors. The longest is 120 m, under 93 s at
        /// 1.3 m/s, under 16 ticks, and 20 per corridor allows for rounding.</item>
        /// </list>
        /// Ceiling = flights × 4 × (7 + 2 × 20). A Gate that never empties, or a
        /// queue that never drains, leaves cohorts at midnight and crosses it on
        /// the first day.
        /// </summary>
        internal static long CohortCeiling(IScheduleSystem schedule)
        {
            const ulong day = SimConstants.TICKS_PER_SIM_DAY;
            const ulong window = 180 * SimConstants.TICKS_PER_SIM_MINUTE;
            IReadOnlyList<FlightId> day0 = schedule.MovementsBetween(0, day, MovementKind.Departure);
            Assert.Equal(DailyDepartures, day0.Count);
            long flights = 0;
            foreach (FlightId id in day0)
            {
                Assert.True(schedule.TryGetFlight(id, out FlightRecord r), "day-0 flight " + id.Value + " not found");
                if (r.ScheduledTick <= window)
                {
                    flights++;
                }
            }

            return flights * 4 * (7 + 2 * 20);
        }

        /// <summary>
        /// §19.6's load check, applied to the soak composer's latest run, which
        /// stepped <paramref name="days"/> whole sim-days (§19.7 "Sizing").
        /// </summary>
        internal static void AssertRunCarriedLoad(SoakComposer composer, uint days)
        {
            Assert.NotNull(composer.World);
            Assert.NotNull(composer.Schedule);
            Assert.NotNull(composer.Boarding);
            Assert.NotNull(composer.Flow);
            IScheduleSystem schedule = composer.Schedule!;
            IFlowSystem flow = composer.Flow!;
            KillGateKit.BoardingProbe boarding = composer.Boarding!;

            // Day 0 is materialised at construction and day d + 1 at tick d · 14400
            // (11 §11.9), so days 0..days are published by the end.
            IReadOnlyList<FlightId> published = schedule.PublishedFlights();
            Assert.Equal((int)(days + 1) * DailyMovements, published.Count);

            // Every passenger of days 0..days-1 is injected by its STD, inside the
            // run. Day `days`'s first injection is at 06:00 − 180 min = 03:00 of that
            // day, after the run. So exactly `days` days' passengers were injected.
            long injected = 0;
            foreach (FlightId id in published)
            {
                Assert.True(schedule.TryGetFlight(id, out FlightRecord record), "published flight " + id.Value + " not found");
                if (record.Kind == MovementKind.Departure)
                {
                    int pending = schedule.PendingInjectionCount(id);
                    Assert.True(pending >= 0 && pending <= record.PaxCount, "flight " + id.Value + " pending " + pending + " of " + record.PaxCount);
                    injected += record.PaxCount - pending;
                }
            }

            Assert.Equal(days * DailyDepartingPax, injected);

            // Every departure of days 0..days-1 is absorbed exactly once, at its STD.
            Assert.Equal((int)days * DailyDepartures, boarding.AbsorbCalls);

            long onNodes = 0;
            long cohorts = 0;
            foreach (NodeId node in composer.World!.Nodes())
            {
                onNodes += flow.Population(node);
                cohorts += flow.CohortsAt(node).Count;
            }

            // Head count conserved: everyone injected is on a node or was taken by an Absorb.
            Assert.Equal(injected, onNodes + boarding.PopulationBeforeAbsorb);
            Assert.True(boarding.Boarded > 0, "no passenger boarded in " + days + " days");

            long ceiling = CohortCeiling(schedule);
            Assert.True(cohorts <= ceiling, cohorts + " live cohorts at the end, over the ceiling " + ceiling);
        }
    }
}
