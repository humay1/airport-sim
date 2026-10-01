using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// The kit composition of 19 §19.6: the Phase 0 composition of §19.2a, built by
    /// the test from the same four fixtures, so its hashes equal the CLI's. Written
    /// from 08 §8.5/§8.11/§8.11a, 09 §9.7/§9.11, 11 §11.7/§11.9a and 18 §18.4,
    /// never from an implementation.
    ///
    /// <para><b>Content sizing</b> (§19.2a "The content", 09 §9.10). The values in
    /// tests/fixtures/harness/phase0-content/ are fixture sizing, not balance.
    /// security_standard serves 15 passengers per server-minute. The flow fixture
    /// opens one server on each of its two security lanes, so together they clear
    /// 30 a minute, 43 200 a day, against the schedule's 20 456 departing
    /// passengers a day. Take each departure's pax, spread them over its
    /// profile's show-up curve (the curves in that directory), and serve 30 a
    /// minute. The combined backlog then peaks at about 670 passengers in the
    /// 09:00–11:00 bank. It empties repeatedly inside every bank and stays empty
    /// from the last evening bank to 04:00, so the queues drain between banks on
    /// every one of the 100 days. capacity_standing 1000 is above that whole
    /// peak, so even one lane taking all of it does not spill back.</para>
    /// </summary>
    internal static class KillGateKit
    {
        internal const ulong Seed = 12345;

        internal const uint Days = 100;

        internal const uint Ticks = Days * HarnessTestKit.TicksPerDay;

        internal const string ContentManifestPath = "tests/fixtures/harness/phase0-content.files";
        internal const string ContentDirectory = "tests/fixtures/harness/phase0-content";
        internal const string SchedulePath = "tests/fixtures/schedule/phase0-200.csv";
        internal const string WorldPath = "tests/fixtures/world/phase0-landside.json";
        internal const string FlowPath = "tests/fixtures/flow/phase0-landside.flow.json";

        /// <summary>§19.2a PHASE0_DEPARTURE_SINK: the flow fixture's only Sink node.</summary>
        internal static readonly NodeId DepartureSink = new NodeId(9);

        /// <summary>Departures per day in phase0-200.csv (11 §11.10: 100 D rows).</summary>
        internal const int DailyDepartures = 100;

        /// <summary>Departing passengers per day in phase0-200.csv, the sum of its D rows' pax.</summary>
        internal const long DailyDepartingPax = 20456;

        private static IContentIndex? _content;

        /// <summary>
        /// 07 "Fixture location" (Q-031): the nearest ancestor of
        /// AppContext.BaseDirectory holding AirportSim.sln, joined with the
        /// repository-relative path. Not finding the root fails the test.
        /// </summary>
        internal static string RepoPath(string relativePath)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            return Path.Combine(dir!, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static byte[] Fixture(string relativePath)
        {
            return File.ReadAllBytes(RepoPath(relativePath));
        }

        /// <summary>
        /// §19.2a's content source. Files() is exactly the manifest's lines, each a
        /// path under phase0-content/, and ReadAll returns those files' bytes.
        /// Never data/.
        /// </summary>
        internal sealed class ManifestSource : IContentSource
        {
            private readonly List<string> _files = new List<string>();
            private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public ManifestSource()
            {
                string manifest = Encoding.UTF8.GetString(Fixture(ContentManifestPath));
                Assert.EndsWith("\n", manifest);
                foreach (string line in manifest.Substring(0, manifest.Length - 1).Split('\n'))
                {
                    Assert.False(line.Length == 0 || line.IndexOf('\r') >= 0, "bad manifest line: '" + line + "'");
                    _files.Add(line);
                    _bytes.Add(line, Fixture(ContentDirectory + "/" + line));
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

        /// <summary>The kit content. It is loaded once, as the CLI loads it once per invocation (§19.2a "When").</summary>
        internal static IContentIndex Content()
        {
            if (_content == null)
            {
                _content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(new ManifestSource()));
            }

            return _content;
        }

        /// <summary>HarnessGates.FinalHash of the kit content and a fresh kit composer (§19.6).</summary>
        internal static string FinalHash(ulong seed, uint ticks)
        {
            return HarnessGates.FinalHash(Content(), new Phase0Composer().Compose, seed, ticks);
        }

        /// <summary>
        /// The position-3 probe of §19.6. It behaves as §19.2a's boarding stand-in
        /// and hashes 0. Through queries only, it also records each flight's
        /// departing population just before its Absorb, and what each Absorb
        /// returns.
        /// </summary>
        internal sealed class BoardingProbe : ISimSystem
        {
            private readonly IScheduleSystem _schedule;
            private readonly IFlowSystem _flow;
            private IReadOnlyList<FlightId> _day = Array.Empty<FlightId>();

            public BoardingProbe(IScheduleSystem schedule, IFlowSystem flow)
            {
                _schedule = schedule;
                _flow = flow;
            }

            public SystemId Id => new SystemId(3);

            public string Name => "probe.boarding";

            public int AbsorbCalls { get; private set; }

            /// <summary>The sum of PopulationForFlight(flight, Departing), each read just before its Absorb.</summary>
            public long PopulationBeforeAbsorb { get; private set; }

            /// <summary>The sum of the Absorb return values.</summary>
            public long Boarded { get; private set; }

            public void Tick(in TickContext ctx)
            {
                ulong t = ctx.Tick;
                if (t % SimConstants.TICKS_PER_SIM_DAY == 0)
                {
                    _day = _schedule.MovementsBetween(t, t + SimConstants.TICKS_PER_SIM_DAY, MovementKind.Departure);
                }

                foreach (FlightId flight in _day)
                {
                    Assert.True(_schedule.TryGetFlight(flight, out FlightRecord record), "day-list flight " + flight.Value + " not found");
                    if (record.ScheduledTick == t)
                    {
                        PopulationBeforeAbsorb += _flow.PopulationForFlight(flight, FlowDirection.Departing);
                        Boarded += _flow.Absorb(DepartureSink, flight);
                        AbsorbCalls++;
                    }
                }
            }

            public ulong ComputeStateHash()
            {
                return 0;
            }
        }

        /// <summary>
        /// The kit composer (§19.6), in §19.2a's construction and registration
        /// order. Every call parses the walk-graph, flow-graph and schedule
        /// fixtures afresh and builds fresh systems. It keeps the systems of its
        /// latest call for the load check.
        /// </summary>
        internal sealed class Phase0Composer
        {
            private readonly byte[] _schedule = Fixture(SchedulePath);
            private readonly byte[] _world = Fixture(WorldPath);
            private readonly byte[] _flow = Fixture(FlowPath);

            public int Calls { get; private set; }

            public IWorldSystem? World { get; private set; }

            public IScheduleSystem? Schedule { get; private set; }

            public BoardingProbe? Boarding { get; private set; }

            public IFlowSystem? Flow { get; private set; }

            public void Compose(ISimHostBuilder builder)
            {
                Calls++;
                SystemServices services = builder.Services;

                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, "phase0-landside.json");
                IWorldSystem world = WorldFactory.CreateSystem(services, walk);
                FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, "phase0-landside.flow.json", world);
                IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, "phase0-200.csv");
                IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);
                var boarding = new BoardingProbe(schedule, flow);

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

        /// <summary>
        /// The live-cohort ceiling at the end of a run (09 §9.10, §19.6), derived
        /// from the fixtures:
        /// <list type="bullet">
        /// <item>A departure's passengers are injected no earlier than
        /// STD − 180 min, the longest show-up offset in the kit's pax profiles
        /// (11 §11.6), and Absorb at STD removes all of them (09 §9.7). So at the
        /// end of a run, only flights whose STD falls within 1 800 ticks after it
        /// hold passengers. <c>flights</c> is the largest number of the day's
        /// departures with STD in any such window, wrapping at midnight.</item>
        /// <item>A flight has one pax profile, so it has at most 4 cohort keys
        /// (hold baggage × assistance, 09 §9.2).</item>
        /// <item>Merging is mandatory for equal keys on one node (09 §9.3), so a
        /// key has at most one cohort on each non-Corridor node: 7 of the 9
        /// fixture nodes. On a Corridor, equal keys merge only with equal DueAt,
        /// so a key holds at most one cohort per entry tick still crossing. The
        /// longest corridor is 120 m. At 1.3 m/s that takes under 93 s, which is
        /// under 16 ticks, and 20 per corridor allows for rounding. There are 2
        /// corridors.</item>
        /// </list>
        /// Ceiling = flights × 4 × (7 + 2 × 20). A Gate that never empties keeps
        /// one cohort per key of every departure so far, and crosses this within
        /// a few days.
        /// </summary>
        internal static long CohortCeiling(IScheduleSystem schedule)
        {
            const ulong day = SimConstants.TICKS_PER_SIM_DAY;
            const ulong window = 180 * SimConstants.TICKS_PER_SIM_MINUTE;
            IReadOnlyList<FlightId> day0 = schedule.MovementsBetween(0, day, MovementKind.Departure);
            var stds = new List<ulong>();
            foreach (FlightId id in day0)
            {
                Assert.True(schedule.TryGetFlight(id, out FlightRecord r), "day-0 flight " + id.Value + " not found");
                stds.Add(r.ScheduledTick);
            }

            Assert.Equal(DailyDepartures, stds.Count);
            long flights = 0;
            foreach (ulong start in stds)
            {
                long n = 0;
                foreach (ulong s in stds)
                {
                    if ((s + day - start) % day <= window)
                    {
                        n++;
                    }
                }

                flights = Math.Max(flights, n);
            }

            return flights * 4 * (7 + 2 * 20);
        }

        /// <summary>
        /// §19.6's load check on the composer's latest run, which stepped
        /// <see cref="Ticks"/> ticks.
        /// </summary>
        internal static void AssertRunCarriedLoad(Phase0Composer composer)
        {
            Assert.NotNull(composer.World);
            Assert.NotNull(composer.Schedule);
            Assert.NotNull(composer.Boarding);
            Assert.NotNull(composer.Flow);
            IScheduleSystem schedule = composer.Schedule!;
            IFlowSystem flow = composer.Flow!;
            BoardingProbe boarding = composer.Boarding!;

            // Day 0 is materialised at construction, and day d + 1 at tick d · 14400
            // (11 §11.9), so the last boundary in the run materialises day 100.
            // Every flight of days 0..100 publishes at max(0, STD − 14400) < 1 440 000.
            IReadOnlyList<FlightId> published = schedule.PublishedFlights();
            Assert.Equal(101 * 200, published.Count);

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

            // Every injection of days 0..99 falls at or before its STD, inside the run.
            Assert.True(injected >= Days * DailyDepartingPax, "only " + injected + " passengers injected in 100 days");

            // Every departure of days 0..99 has its STD inside the run and is absorbed
            // exactly once. Day 100's departures have their STD after it.
            Assert.Equal((int)Days * DailyDepartures, boarding.AbsorbCalls);

            long onNodes = 0;
            long cohorts = 0;
            foreach (NodeId node in composer.World!.Nodes())
            {
                onNodes += flow.Population(node);
                cohorts += flow.CohortsAt(node).Count;
            }

            Assert.Equal(injected, onNodes + boarding.PopulationBeforeAbsorb);
            Assert.True(boarding.Boarded > 0, "no passenger boarded in 100 days");

            long ceiling = CohortCeiling(schedule);
            Assert.True(cohorts <= ceiling, cohorts + " live cohorts at the end, over the ceiling " + ceiling);
        }
    }
}
