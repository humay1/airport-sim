using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.Turnaround;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 14 §14.14 "Integrated day" (Q-106): one headless sim-day of the six
    /// Phase 1 systems composed as 16 §16.4 steps 3 and 4 compose them, on the
    /// merged fixtures, with every invariant asserted over every retained
    /// flight after every tick, and over the touched flight after every
    /// handler. It does not assert that a particular leaf kind appears: the
    /// fixtures guarantee that a runway hold and a vehicle wait occur, not that
    /// either becomes delay. This is the only test in this project that
    /// constructs a module other than sim.delay (07 L3, Q-106).
    /// </summary>
    public sealed class DelayIntegratedDayTests
    {
        /// <summary>
        /// The 12 §12.12a rules, built in code. Fixture values, not balance (13
        /// §13.11): BoardingHoldMaxMinutes above 0 so the passenger-hold family
        /// can occur; the doors delay as in the sim.turnaround suite.
        /// </summary>
        private const uint BoardingHoldMaxMinutes = 10U;
        private const uint DoorsOpenDelayMinutes = 2U;

        private static readonly (string Id, int Ordinal)[] Sizes =
        {
            ("small", 1), ("medium", 2), ("heavy", 3), ("super", 4),
        };

        private static readonly (string Id, string Size)[] Aircraft =
        {
            ("atr72", "small"), ("crj900", "small"),
            ("a320", "medium"), ("a321", "medium"), ("b738", "medium"),
            ("a359", "heavy"), ("b744", "heavy"), ("b789", "heavy"),
            ("a388", "super"),
        };

        // The values of tests/fixtures/harness/phase0-content/pax_profiles/*.json.
        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        /// <summary>
        /// 14 §14.14 "The content (Q-106)", built in code (08 §8.11a): fixture
        /// sizing, not balance. Profiles carry the harness content files' values:
        /// walk speed 1.3 m/s; security_standard serves 15 per server-minute,
        /// stands 1000, thresholds 10 and 3 minutes, category security_queue.
        /// </summary>
        private static IContentIndex Content()
        {
            var defs = new List<IContentDefinition>();
            foreach ((string id, int ordinal) in Sizes)
            {
                defs.Add(new SizeCategoryDefinition(new ContentId(id), ordinal));
            }

            foreach ((string id, string size) in Aircraft)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId(size)));
            }

            defs.Add(Profile("business", Business));
            defs.Add(Profile("leisure", Leisure));
            defs.Add(new QueueProfileDefinition(new ContentId("security_standard"), Fx.FromInt(15), 1000, Fx.FromInt(10), Fx.FromInt(3), DelayCategory.SecurityQueue));
            return ContentIndexFactory.Create(defs);
        }

        private static PaxProfileDefinition Profile(string id, (uint Minutes, uint Share)[] curve)
        {
            var buckets = new List<ShowUpBucket>();
            foreach ((uint m, uint s) in curve)
            {
                buckets.Add(new ShowUpBucket(m, s));
            }

            return new PaxProfileDefinition(new ContentId(id), Fx.FromRatio(13, 10), buckets);
        }

        // Slow by 07 L11a rule (b)'s prompt: about 12-15 s in the Test Author's
        // Release run (rule (a) does not apply: one sim-day). CI's measurement
        // decides from then on.
        [Fact]
        [Trait("Category", "Slow")]
        public void test_delay_integrated_day_holds_every_invariant()
        {
            // Step 1: the builder, from the test's seed and content index, with the
            // test's own checkpoint and log sinks. No bundle is read.
            IContentIndex content = Content();
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(0x0024_1D4FUL, content, sink, new NullLog()));
            SystemServices sv = b.Services;

            // Step 3: construction in dependency order.
            const string WorldName = "phase0-landside.json";
            const string FlowName = "phase0-landside.flow.json";
            const string ScheduleName = "phase0-200.csv";
            const string LayoutName = "phase1-single-runway.json";
            const string SetupName = "phase1-five-vehicles.json";
            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(Repo.Read("tests", "fixtures", "world", WorldName), WorldName);
            IWorldSystem world = WorldFactory.CreateSystem(sv, walk);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(Repo.Read("tests", "fixtures", "flow", FlowName), FlowName, world);
            IFlowSystem flow = FlowFactory.CreateSystem(sv, flowGraph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(Repo.Read("tests", "fixtures", "schedule", ScheduleName), ScheduleName);
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(sv, table, flow);
            AirsideLayout layout = AirsideFactory.CreateLayoutLoader().Parse(Repo.Read("tests", "fixtures", "airside", LayoutName), LayoutName);
            IAirsideSystem airside = AirsideFactory.CreateSystem(sv, layout, new AirsideRules(BoardingHoldMaxMinutes, DoorsOpenDelayMinutes), schedule, flow, true);
            TurnaroundSetup setup = TurnaroundFactory.CreateSetupLoader().Load(Repo.Read("tests", "fixtures", "turnaround", SetupName), SetupName);
            ITurnaroundSystem turnaround = TurnaroundFactory.CreateSystem(sv, setup, schedule);
            IDelaySystem delay = DelayFactory.CreateSystem(sv);

            // The position-9 recorder checks the touched flight after every
            // handler sim.delay ran (08 §8.6 rule 4: 9 runs after 7).
            var rec = new Recorder(sv.Events) { Keep = false };
            string? failure = null;
            int runwayHolds = 0;
            int vehicleWaits = 0;
            bool dispatched = true;
            rec.After = (env, payload, flight) =>
            {
                dispatched = true;
                if (payload is AircraftHeldForRunway)
                {
                    runwayHolds++;
                }
                else if (payload is TurnaroundJobBlocked tb && tb.WaitingOn == ResourceKind.Vehicle)
                {
                    vehicleWaits++;
                }

                if (failure != null || flight == 0UL || !delay.TryGetFlightDelay(new FlightId(flight), out FlightDelay _))
                {
                    return;
                }

                try
                {
                    Invariants.CheckFlight(delay, flight, string.Format(CultureInfo.InvariantCulture, "after {0} {1} at tick {2}", payload.GetType().Name, Show.Id(env.Id), env.Tick));
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                }
            };

            // Step 4: registration in registry order (08 §8.5), then Build.
            b.Register(world);
            b.Register(schedule);
            b.Register(airside);
            b.Register(flow);
            b.Register(turnaround);
            b.Register(delay);
            b.Register(new ProbeSystem(DConst.RecorderPos));
            ISimHost host = b.Build();

            while (host.CurrentTick < DConst.TicksPerDay)
            {
                host.Step(1);
                Assert.True(failure == null, failure);

                // sim.delay's state changes only in its handlers and in its Tick at
                // a day boundary (§14.8), so a tick with neither leaves the last
                // full check standing; every other tick gets a full check.
                ulong done = host.CurrentTick - 1UL;
                if (dispatched || done % DConst.TicksPerDay == 0UL)
                {
                    Invariants.CheckAll(delay, "after tick " + done.ToString(CultureInfo.InvariantCulture));
                    dispatched = false;
                }
            }

            // The day ran: the fixtures' runway holds and vehicle waits occurred,
            // and sim.delay finalised most of the day's flights and published
            // their trees.
            int finalised = 0;
            foreach (FlightId f in delay.RetainedFlights())
            {
                if (delay.TryGetFlightDelay(f, out FlightDelay r) && r.Finalised)
                {
                    finalised++;
                }
            }

            Assert.True(runwayHolds > 0, "no AircraftHeldForRunway in the integrated day");
            Assert.True(vehicleWaits > 0, "no TurnaroundJobBlocked on a vehicle in the integrated day");
            Assert.True(finalised > 100, "only " + finalised + " flights finalised in the integrated day");
            Assert.True(rec.Delays.Count >= finalised, "fewer DelayEvents than finalised flights");
        }
    }
}
