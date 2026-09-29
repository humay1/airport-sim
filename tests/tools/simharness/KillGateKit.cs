using System;
using System.Collections.Generic;
using System.IO;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// The Phase 0 composition for the T-009 kill gate (tasks/T-009-100-day-run.md),
    /// written from 08 §8.5/§8.11a, 09 §9.11, 11 §11.9a/§11.10 and 18 §18.4, never
    /// from an implementation. Built only through each module's factory and the
    /// builder's SystemServices.
    /// </summary>
    internal static class KillGateKit
    {
        internal const ulong Seed = 12345;

        internal const uint Days = 100;

        internal const uint Ticks = Days * HarnessTestKit.TicksPerDay;

        internal const string SchedulePath = "tests/fixtures/schedule/phase0-200.csv";
        internal const string WorldPath = "tests/fixtures/world/phase0-landside.json";
        internal const string FlowPath = "tests/fixtures/flow/phase0-landside.flow.json";

        // (minutes_before_std, share_permille). The ids match data/pax_profiles so the
        // fixture's pax_profile column resolves (11 §11.10); the values are this
        // suite's literals, not whatever data/ holds later.
        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        private static readonly string[] AircraftIds =
        {
            "a320", "a321", "a359", "a388", "atr72", "b738", "b744", "b789", "crj900",
        };

        /// <summary>
        /// Test-owned content for the three fixtures. The security_standard values
        /// are fixture sizing (09 §9.10), never data/ balance values: 10 pax per
        /// server-minute with one server open on each of the two lanes clears
        /// 28 800 a day, above the fixture's 20 456 departing passengers, so the
        /// queues drain between banks instead of growing for 100 days.
        /// </summary>
        internal static IContentIndex Content()
        {
            var defs = new List<IContentDefinition>();
            defs.Add(new SizeCategoryDefinition(new ContentId("size_c"), 3));
            foreach (string id in AircraftIds)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId("size_c")));
            }

            defs.Add(Pax("business", Business));
            defs.Add(Pax("leisure", Leisure));
            defs.Add(new QueueProfileDefinition(
                new ContentId("security_standard"),
                Fx.FromRatio(10, 1),
                600,
                Fx.FromRatio(10, 1),
                Fx.FromRatio(3, 1),
                DelayCategory.SecurityQueue));
            return ContentIndexFactory.Create(defs);
        }

        private static PaxProfileDefinition Pax(string id, (uint Minutes, uint Share)[] curve)
        {
            var buckets = new List<ShowUpBucket>();
            foreach ((uint m, uint s) in curve)
            {
                buckets.Add(new ShowUpBucket(m, s));
            }

            return new PaxProfileDefinition(new ContentId(id), Fx.FromRatio(13, 10), buckets);
        }

        /// <summary>
        /// 07 "Fixture location" (Q-031): the nearest ancestor of
        /// AppContext.BaseDirectory holding AirportSim.sln, joined with the
        /// repository-relative path. Not finding the root fails the test.
        /// </summary>
        internal static byte[] Fixture(string relativePath)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            return File.ReadAllBytes(Path.Combine(dir!, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        /// <summary>
        /// A SimComposer for the Phase 0 set. Every call parses the fixtures afresh
        /// and builds fresh systems, so two runs share nothing but the bytes read
        /// once here. It keeps the systems of its latest call so a test can check,
        /// after the gate returns, that the run carried load.
        /// </summary>
        internal sealed class Phase0Composer
        {
            private readonly byte[] _schedule = Fixture(SchedulePath);
            private readonly byte[] _world = Fixture(WorldPath);
            private readonly byte[] _flow = Fixture(FlowPath);

            public int Calls { get; private set; }

            public IWorldSystem? World { get; private set; }

            public IScheduleSystem? Schedule { get; private set; }

            public IFlowSystem? Flow { get; private set; }

            public void Compose(ISimHostBuilder builder)
            {
                Calls++;
                SystemServices services = builder.Services;

                // Construct in dependency order (08 §8.11a): flow needs world,
                // schedule injects into flow. Register in registry order (08 §8.5):
                // world 1, schedule 2, flow 4.
                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, "phase0-landside.json");
                IWorldSystem world = WorldFactory.CreateSystem(services, walk);

                FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, "phase0-landside.flow.json", world);
                IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);

                ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, "phase0-200.csv");
                IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);

                builder.Register(world);
                builder.Register(schedule);
                builder.Register(flow);

                World = world;
                Schedule = schedule;
                Flow = flow;
            }
        }

        /// <summary>
        /// Asserts that the composer's latest run, which stepped <see cref="Ticks"/>
        /// ticks, carried the fixture's load end to end, so a passing gate is not a
        /// vacuous one.
        /// <list type="bullet">
        /// <item>Publication (11 §11.5, §11.9): day 0 is materialised at
        /// construction and day d + 1 at tick d · 14400, so the last boundary in
        /// the run, tick 99 · 14400, materialises day 100. Every flight of days
        /// 0..100 has PublishTick = max(0, ScheduledTick − 14400) &lt; 1 440 000,
        /// so exactly 101 × 200 flights are published.</item>
        /// <item>Head count (09 §9.7, §9.12): at Phase 0 nothing calls Absorb,
        /// and Gate and Sink nodes keep what they hold, so every passenger
        /// injected is still on some node. Injected is each published departure's
        /// PaxCount minus its PendingInjectionCount (11 §11.7).</item>
        /// </list>
        /// </summary>
        internal static void AssertRunCarriedLoad(Phase0Composer composer)
        {
            Assert.NotNull(composer.World);
            Assert.NotNull(composer.Schedule);
            Assert.NotNull(composer.Flow);
            IScheduleSystem schedule = composer.Schedule!;
            IFlowSystem flow = composer.Flow!;

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

            // 100 full days of 20 456 departing passengers are due inside the run.
            Assert.True(injected >= 100L * 20456, "only " + injected + " passengers injected in 100 days");

            long onNodes = 0;
            foreach (NodeId node in composer.World!.Nodes())
            {
                onNodes += flow.Population(node);
            }

            Assert.Equal(injected, onNodes);
        }
    }
}
