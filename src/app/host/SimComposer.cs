using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Delay;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.Turnaround;
using AirportSim.Sim.World;

namespace AirportSim.App.Host
{
    /// <summary>
    /// Composes a scenario bundle by 16 §16.4's steps 1 to 4, through the modules'
    /// published factories only. Every load failure is a <see cref="FormatException"/>
    /// whose message starts with the bundle file name.
    /// </summary>
    internal sealed class SimComposer : ISimComposer
    {
        internal const string BundleFile = "bundle.json";
        internal const string WorldFile = "world.fixture";
        internal const string ScheduleFile = "schedule.csv";
        internal const string AirsideFile = "airside.fixture";
        internal const string AirsideRulesFile = "airside_rules.json";
        internal const string FlowFile = "flow.fixture";
        internal const string TurnaroundFile = "turnaround.fixture";

        private const string World = "sim.world";
        private const string Schedule = "sim.schedule";
        private const string Airside = "sim.airside";
        private const string Flow = "sim.flow";
        private const string Turnaround = "sim.turnaround";
        private const string Delay = "sim.delay";

        private static readonly string[] Phase1Names = { World, Schedule, Airside, Flow, Turnaround, Delay };

        private readonly IReadOnlyList<IContentDefinition> _content;

        internal SimComposer(IReadOnlyList<IContentDefinition> content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public ComposedSim Compose(IScenarioBundle bundle, ICheckpointSink checkpoints)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (checkpoints == null)
            {
                throw new ArgumentNullException(nameof(checkpoints));
            }

            // Step 1: bundle.json, then the builder.
            ulong seed = ReadBundleJson(bundle, out HashSet<string> listed);
            CheckDownwardInterfaces(listed);
            CheckFiles(bundle, listed);

            var config = new SimHostConfig(seed, ContentIndexFactory.Create(_content), checkpoints, new DiscardLog());
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            SystemServices services = builder.Services;

            // Steps 2 and 3: each file with its module's loader, systems in dependency order.
            IWorldSystem? world = null;
            if (listed.Contains(World))
            {
                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(bundle.ReadAll(WorldFile), WorldFile);
                world = WorldFactory.CreateSystem(in services, in walk);
            }

            IFlowSystem? flow = null;
            if (listed.Contains(Flow))
            {
                FlowGraph graph = FlowFactory.CreateGraphLoader().Load(bundle.ReadAll(FlowFile), FlowFile, world!);
                flow = FlowFactory.CreateSystem(in services, in graph, world!);
            }

            IScheduleSystem? schedule = null;
            if (listed.Contains(Schedule))
            {
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(bundle.ReadAll(ScheduleFile), ScheduleFile);
                schedule = ScheduleFactory.CreateSystem(in services, in table, flow);
            }

            IAirsideSystem? airside = null;
            if (listed.Contains(Airside))
            {
                AirsideLayout layout = AirsideFactory.CreateLayoutLoader().Parse(bundle.ReadAll(AirsideFile), AirsideFile);
                AirsideRules rules = ReadRules(bundle.ReadAll(AirsideRulesFile));
                airside = AirsideFactory.CreateSystem(in services, in layout, in rules, schedule!, flow, listed.Contains(Turnaround));
            }

            ITurnaroundSystem? turnaround = null;
            if (listed.Contains(Turnaround))
            {
                TurnaroundSetup setup = TurnaroundFactory.CreateSetupLoader().Load(bundle.ReadAll(TurnaroundFile), TurnaroundFile);
                turnaround = TurnaroundFactory.CreateSystem(in services, in setup, schedule!);
            }

            IDelaySystem? delay = null;
            if (listed.Contains(Delay))
            {
                delay = DelayFactory.CreateSystem(in services);
            }

            // Step 4: registry order (08 §8.5), whatever order bundle.json lists them in.
            if (world != null)
            {
                builder.Register(world);
            }

            if (schedule != null)
            {
                builder.Register(schedule);
            }

            if (airside != null)
            {
                builder.Register(airside);
            }

            if (flow != null)
            {
                builder.Register(flow);
            }

            if (turnaround != null)
            {
                builder.Register(turnaround);
            }

            if (delay != null)
            {
                builder.Register(delay);
            }

            return new ComposedSim(builder.Build(), world, schedule, airside, flow, turnaround, delay);
        }

        /// <summary>16 §16.3 "Strict form" (Q-069). Returns the seed and the set of listed systems.</summary>
        internal static ulong ReadBundleJson(IScenarioBundle bundle, out HashSet<string> listed)
        {
            if (!bundle.Has(BundleFile))
            {
                throw Failure(BundleFile, "the bundle has no such file");
            }

            object parsed;
            try
            {
                parsed = StrictJson.Parse(bundle.ReadAll(BundleFile));
            }
            catch (FormatException e)
            {
                throw Failure(BundleFile, e.Message);
            }

            if (!(parsed is Dictionary<string, object> root))
            {
                throw Failure(BundleFile, "the document is not an object");
            }

            foreach (string key in root.Keys)
            {
                if (key != "schema_version" && key != "seed" && key != "systems")
                {
                    throw Failure(BundleFile, "unknown key '" + key + "'");
                }
            }

            if (!root.TryGetValue("schema_version", out object? version))
            {
                throw Failure(BundleFile, "missing key 'schema_version'");
            }

            if (!root.TryGetValue("seed", out object? seedValue))
            {
                throw Failure(BundleFile, "missing key 'seed'");
            }

            if (!root.TryGetValue("systems", out object? systemsValue))
            {
                throw Failure(BundleFile, "missing key 'systems'");
            }

            if (!(version is long v) || v != 1)
            {
                throw Failure(BundleFile, "schema_version must be 1");
            }

            if (!(seedValue is string seedText) || !IsCanonicalSeed(seedText)
                || !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            {
                throw Failure(BundleFile, "seed must be a uint64 decimal string");
            }

            if (!(systemsValue is List<object> systems) || systems.Count == 0)
            {
                throw Failure(BundleFile, "systems must be a non-empty array");
            }

            listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (object entry in systems)
            {
                if (!(entry is string name) || Array.IndexOf(Phase1Names, name) < 0)
                {
                    throw Failure(BundleFile, "systems holds an entry that is not a Phase 1 module name");
                }

                if (!listed.Add(name))
                {
                    throw Failure(BundleFile, "systems lists '" + name + "' twice");
                }
            }

            return seed;
        }

        private static bool IsCanonicalSeed(string text)
        {
            if (text.Length == 0 || text.Length > 20)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return text.Length == 1 || text[0] != '0';
        }

        /// <summary>16 §16.4: flow needs world, airside and turnaround need schedule.</summary>
        private static void CheckDownwardInterfaces(HashSet<string> listed)
        {
            RequireListed(listed, Flow, World);
            RequireListed(listed, Airside, Schedule);
            RequireListed(listed, Turnaround, Schedule);
        }

        private static void RequireListed(HashSet<string> listed, string system, string needed)
        {
            if (listed.Contains(system) && !listed.Contains(needed))
            {
                throw Failure(BundleFile, system + " is listed without " + needed + ", which it needs");
            }
        }

        /// <summary>A listed system whose file is missing is a hard load failure naming the file (16 §16.3).</summary>
        private static void CheckFiles(IScenarioBundle bundle, HashSet<string> listed)
        {
            RequireFile(bundle, listed, World, WorldFile);
            RequireFile(bundle, listed, Schedule, ScheduleFile);
            RequireFile(bundle, listed, Airside, AirsideFile);
            RequireFile(bundle, listed, Airside, AirsideRulesFile);
            RequireFile(bundle, listed, Flow, FlowFile);
            RequireFile(bundle, listed, Turnaround, TurnaroundFile);
        }

        private static void RequireFile(IScenarioBundle bundle, HashSet<string> listed, string system, string file)
        {
            if (listed.Contains(system) && !bundle.Has(file))
            {
                throw Failure(file, "the bundle has no such file, and " + system + " is listed");
            }
        }

        /// <summary>04-data-schemas.md's airside_rules.json: schema_version 1 and both uint32 keys, nothing else.</summary>
        private static AirsideRules ReadRules(byte[] bytes)
        {
            object parsed;
            try
            {
                parsed = StrictJson.Parse(bytes);
            }
            catch (FormatException e)
            {
                throw Failure(AirsideRulesFile, e.Message);
            }

            if (!(parsed is Dictionary<string, object> root))
            {
                throw Failure(AirsideRulesFile, "the document is not an object");
            }

            foreach (string key in root.Keys)
            {
                if (key != "schema_version" && key != "boarding_hold_max_minutes" && key != "doors_open_delay_minutes")
                {
                    throw Failure(AirsideRulesFile, "unknown key '" + key + "'");
                }
            }

            if (!root.TryGetValue("schema_version", out object? version) || !(version is long v) || v != 1)
            {
                throw Failure(AirsideRulesFile, "schema_version must be 1");
            }

            return new AirsideRules(ReadUInt32(root, "boarding_hold_max_minutes"), ReadUInt32(root, "doors_open_delay_minutes"));
        }

        private static uint ReadUInt32(Dictionary<string, object> root, string key)
        {
            if (!root.TryGetValue(key, out object? value))
            {
                throw Failure(AirsideRulesFile, "missing key '" + key + "'");
            }

            if (!(value is long n) || n < 0 || n > uint.MaxValue)
            {
                throw Failure(AirsideRulesFile, "'" + key + "' must be a uint32");
            }

            return (uint)n;
        }

        private static FormatException Failure(string file, string what)
        {
            return new FormatException(file + ": " + what);
        }

        /// <summary>The host's log sink (08 §8.10): it keeps nothing, since a log is not sim state.</summary>
        private sealed class DiscardLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }
    }
}
