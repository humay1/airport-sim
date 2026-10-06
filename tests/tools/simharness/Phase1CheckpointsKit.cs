using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Delay;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.Turnaround;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// The Phase 1 checkpoints kit of 19 §19.8 (T-048): §19.8's checkpoints kit
    /// "built as above with all six factories". It composes a bundle whose
    /// listed systems are any subset of the six Phase 1 systems, through the
    /// published surface only, exactly as §19.2c's composition does: 16 §16.4
    /// steps 1 to 4, sourceName the bundle file name (Q-073), airside_rules.json
    /// parsed by the caller into AirsideRules (12 §12.12a, 04-data-schemas.md),
    /// nothing else registered (Q-070) and no command submitted (Q-071). It
    /// renders the expected dump with CheckpointsKit.Render (16 §16.8). Written
    /// from the spec, never from an implementation.
    /// </summary>
    internal static class Phase1CheckpointsKit
    {
        internal const string BundleDirectory = "tests/fixtures/harness/checkpoints-phase1";

        /// <summary>§19.2c "The fixture": the Phase 1 bundle's content is --content data.</summary>
        internal const string ContentDirectory = "data";

        /// <summary>The Phase 1 bundle's seed: bundle.json's "seed" is "12345" (§19.2c "The fixture").</summary>
        internal const ulong BundleSeed = 12345;

        internal const string World = "sim.world";
        internal const string Schedule = "sim.schedule";
        internal const string Airside = "sim.airside";
        internal const string Flow = "sim.flow";
        internal const string Turnaround = "sim.turnaround";
        internal const string Delay = "sim.delay";

        /// <summary>08 §8.5 registry order of the Phase 1 set (16 §16.4 "Rules").</summary>
        internal static readonly string[] RegistryOrder = { World, Schedule, Airside, Flow, Turnaround, Delay };

        private static IContentIndex? _content;

        /// <summary>The CLI arguments of a checkpoints run of one day over --content data.</summary>
        internal static string[] Args(string bundle, string outPath)
        {
            return new[]
            {
                "checkpoints",
                "--bundle", bundle,
                "--content", ContentDirectory,
                "--days", "1",
                "--out", outPath,
            };
        }

        /// <summary>The content of data/, loaded once as §19.2c "The content" loads it (Q-072).</summary>
        internal static IContentIndex Content()
        {
            if (_content == null)
            {
                var source = new CheckpointsKit.DirectorySource(KillGateKit.RepoPath(ContentDirectory));
                _content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(source));
            }

            return _content;
        }

        /// <summary>
        /// A bundle.json in 16 §16.3's strict form: schema_version 1, seed "12345"
        /// and the given systems, in the given order.
        /// </summary>
        internal static byte[] BundleJson(params string[] systems)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema_version\": 1,\n  \"seed\": \"12345\",\n  \"systems\": [ ");
            for (int i = 0; i < systems.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('"').Append(systems[i]).Append('"');
            }

            sb.Append(" ]\n}\n");
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        /// <summary>
        /// Writes into <paramref name="directory"/> a bundle.json listing
        /// <paramref name="systems"/>, plus byte copies of the named files of the
        /// Phase 1 checkpoints bundle.
        /// </summary>
        internal static void WriteBundle(string directory, string[] systems, params string[] files)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "bundle.json"), BundleJson(systems));
            foreach (string name in files)
            {
                File.WriteAllBytes(Path.Combine(directory, name), KillGateKit.Fixture(BundleDirectory + "/" + name));
            }
        }

        /// <summary>
        /// 04-data-schemas.md's AirsideRules file, both keys (16 §16.3,
        /// Q-047), parsed by the caller since no sim module parses it (12 §12.12a).
        /// </summary>
        private static AirsideRules ParseRules(byte[] json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return new AirsideRules(
                root.GetProperty("boarding_hold_max_minutes").GetUInt32(),
                root.GetProperty("doors_open_delay_minutes").GetUInt32());
        }

        /// <summary>
        /// Composes the bundle in <paramref name="bundleDirectory"/> (a fully
        /// qualified path) whose listed systems are <paramref name="listed"/>, by
        /// 16 §16.4, and steps it with <paramref name="step"/>, which must run
        /// exactly one sim-day of ticks.
        /// </summary>
        internal static CheckpointsKit.KitRun Run(string bundleDirectory, IReadOnlyCollection<string> listed, Action<ISimHost> step)
        {
            var set = new HashSet<string>(listed, StringComparer.Ordinal);
            Func<string, byte[]> read = name => File.ReadAllBytes(Path.Combine(bundleDirectory, name));

            // Step 1: the builder, from the bundle's seed and the content index.
            var sink = new CheckpointsKit.RecordingSink();
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(new SimHostConfig(BundleSeed, Content(), sink, new DiscardLog()));
            SystemServices services = builder.Services;

            // Steps 2 and 3: each listed system's file, with sourceName the bundle
            // file name, constructed in dependency order: world; flow(world);
            // schedule(flow); airside(schedule, flow, turnaroundRegistered);
            // turnaround(schedule); delay.
            var built = new Dictionary<string, ISimSystem>(StringComparer.Ordinal);
            IWorldSystem? world = null;
            if (set.Contains(World))
            {
                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(read("world.fixture"), "world.fixture");
                world = WorldFactory.CreateSystem(services, walk);
                built.Add(World, world);
            }

            IFlowSystem? flow = null;
            if (set.Contains(Flow))
            {
                Assert.NotNull(world);
                FlowGraph graph = FlowFactory.CreateGraphLoader().Load(read("flow.fixture"), "flow.fixture", world!);
                flow = FlowFactory.CreateSystem(services, graph, world!);
                built.Add(Flow, flow);
            }

            IScheduleSystem? schedule = null;
            if (set.Contains(Schedule))
            {
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(read("schedule.csv"), "schedule.csv");
                schedule = ScheduleFactory.CreateSystem(services, table, flow);
                built.Add(Schedule, schedule);
            }

            if (set.Contains(Airside))
            {
                Assert.NotNull(schedule);
                AirsideLayout layout = AirsideFactory.CreateLayoutLoader().Parse(read("airside.fixture"), "airside.fixture");
                AirsideRules rules = ParseRules(read("airside_rules.json"));
                built.Add(Airside, AirsideFactory.CreateSystem(services, layout, rules, schedule!, flow, set.Contains(Turnaround)));
            }

            if (set.Contains(Turnaround))
            {
                Assert.NotNull(schedule);
                TurnaroundSetup setup = TurnaroundFactory.CreateSetupLoader().Load(read("turnaround.fixture"), "turnaround.fixture");
                built.Add(Turnaround, TurnaroundFactory.CreateSystem(services, setup, schedule!));
            }

            if (set.Contains(Delay))
            {
                built.Add(Delay, DelayFactory.CreateSystem(services));
            }

            Assert.Equal(set.Count, built.Count);

            // Step 4: registry order (08 §8.5), and nothing else (Q-070).
            var names = new List<string>();
            foreach (string name in RegistryOrder)
            {
                if (built.TryGetValue(name, out ISimSystem? system))
                {
                    builder.Register(system);
                    Assert.Equal(name, system.Name);
                    names.Add(system.Name);
                }
            }

            ISimHost host = builder.Build();
            step(host);
            Assert.Equal((ulong)HarnessTestKit.TicksPerDay, host.CurrentTick);
            ulong final = host.WorldStateHash();
            return new CheckpointsKit.KitRun(CheckpointsKit.Render(BundleSeed, names, sink), sink.Ticks.Count, final);
        }

        /// <summary>One Step(TICKS_PER_SIM_DAY), the stepping §19.2c pins for one day.</summary>
        internal static CheckpointsKit.KitRun RunOneStep(string bundleDirectory, IReadOnlyCollection<string> listed)
        {
            return Run(bundleDirectory, listed, host => host.Step(HarnessTestKit.TicksPerDay));
        }

        private sealed class DiscardLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }
    }
}
