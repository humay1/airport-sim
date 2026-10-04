using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        /// <summary>The 19 §19.2a content source: Files() is the manifest's lines, under phase0-content/.</summary>
        private sealed class ManifestSource : IContentSource
        {
            private readonly List<string> _files = new List<string>();
            private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            public ManifestSource()
            {
                string manifest = Encoding.UTF8.GetString(Repo.Read("tests", "fixtures", "harness", "phase0-content.files"));
                Assert.EndsWith("\n", manifest);
                foreach (string line in manifest.Substring(0, manifest.Length - 1).Split('\n'))
                {
                    Assert.False(line.Length == 0 || line.IndexOf('\r') >= 0, "bad manifest line: '" + line + "'");
                    _files.Add(line);
                    var parts = new List<string> { "tests", "fixtures", "harness", "phase0-content" };
                    parts.AddRange(line.Split('/'));
                    _bytes.Add(line, Repo.Read(parts.ToArray()));
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

        [Fact]
        public void test_delay_integrated_day_holds_every_invariant()
        {
            // Step 1: the builder, from the test's seed and content index, with the
            // test's own checkpoint and log sinks. No bundle is read.
            IContentIndex content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(new ManifestSource()));
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
            rec.After = (env, payload, flight) =>
            {
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
                Invariants.CheckAll(delay, "after tick " + (host.CurrentTick - 1UL).ToString(CultureInfo.InvariantCulture));
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
