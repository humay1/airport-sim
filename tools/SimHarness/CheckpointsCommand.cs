using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The <c>checkpoints</c> subcommand: composes a scenario bundle by 16-interfaces-host.md
    /// §16.4's rules in the harness's own code, steps it and writes the checkpoint dump of §16.8.
    /// Spec: 19-interfaces-harness.md §19.2c, §19.3 (Q-066 to Q-076). This stage composes
    /// <c>sim.world</c>, <c>sim.schedule</c> and <c>sim.flow</c> only.
    /// </summary>
    internal static class CheckpointsCommand
    {
        private const string BundleFileName = "bundle.json";
        private const string WorldFile = "world.fixture";
        private const string ScheduleFile = "schedule.csv";
        private const string FlowFile = "flow.fixture";

        private const string World = "sim.world";
        private const string Schedule = "sim.schedule";
        private const string Airside = "sim.airside";
        private const string Flow = "sim.flow";
        private const string Turnaround = "sim.turnaround";
        private const string Delay = "sim.delay";

        private static readonly string[] Phase1Names = { World, Schedule, Airside, Flow, Turnaround, Delay };

        /// <summary>A step 2 to 4 failure with a message naming the file and the failure (exit 3).</summary>
        private sealed class HarnessFailure : Exception
        {
            internal HarnessFailure(string message)
                : base(message)
            {
            }
        }

        /// <summary>
        /// Runs steps 2 to 4 of §19.2c. The paths are non-empty and either fully qualified or
        /// repository-relative; the CLI has already decided usage. Returns 0 or 3.
        /// </summary>
        internal static int Run(string bundlePath, string contentPath, uint days, string outPath, TextWriter stdout, TextWriter stderr)
        {
            try
            {
                return RunChecked(bundlePath, contentPath, days, outPath, stdout);
            }
            catch (HarnessFailure ex)
            {
                stderr.WriteLine("tools.simharness: checkpoints: " + ex.Message);
                return 3;
            }
        }

        private static int RunChecked(string bundlePath, string contentPath, uint days, string outPath, TextWriter stdout)
        {
            // Step 2: the root, only when a path needs it; then P; then the bundle; then the content.
            string? root = null;
            string bundleDir = ResolvePath(bundlePath, ref root);
            string contentDir = ResolvePath(contentPath, ref root);
            string outFile = ResolvePath(outPath, ref root);

            if (File.Exists(outFile) || Directory.Exists(outFile))
            {
                throw new HarnessFailure("--out " + outFile + " already exists");
            }
            string? parent = Path.GetDirectoryName(outFile);
            if (parent != null && !Directory.Exists(parent))
            {
                throw new HarnessFailure("--out " + outFile + ": parent directory " + parent + " does not exist");
            }

            HashSet<string> listed = ReadBundle(bundleDir, out ulong seed);
            CheckComposable(listed);

            byte[]? worldBytes = listed.Contains(World) ? ReadBundleFile(bundleDir, WorldFile) : null;
            byte[]? scheduleBytes = listed.Contains(Schedule) ? ReadBundleFile(bundleDir, ScheduleFile) : null;
            byte[]? flowBytes = listed.Contains(Flow) ? ReadBundleFile(bundleDir, FlowFile) : null;

            IContentIndex content = LoadContent(contentDir);

            // Step 3: composition (16 §16.4 steps 1 to 4) and the run.
            var sink = new RecordingCheckpointSink();
            var config = new SimHostConfig(masterSeed: seed, content: content, checkpoints: sink, log: new NullSimLog());
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            SystemServices services = builder.Services;

            IWorldSystem? world = null;
            IFlowSystem? flow = null;
            IScheduleSystem? schedule = null;
            if (worldBytes != null)
            {
                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(worldBytes, WorldFile);
                world = WorldFactory.CreateSystem(services, walk);
            }
            if (flowBytes != null)
            {
                FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(flowBytes, FlowFile, world!);
                flow = FlowFactory.CreateSystem(services, flowGraph, world!);
            }
            if (scheduleBytes != null)
            {
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(scheduleBytes, ScheduleFile);
                schedule = ScheduleFactory.CreateSystem(services, table, flow);
            }

            // Registry order (08 §8.5): world, schedule, flow.
            var registered = new List<ISimSystem>(3);
            if (world != null)
            {
                registered.Add(world);
            }
            if (schedule != null)
            {
                registered.Add(schedule);
            }
            if (flow != null)
            {
                registered.Add(flow);
            }
            foreach (ISimSystem system in registered)
            {
                builder.Register(system);
            }

            ISimHost host = builder.Build();
            uint ticksPerDay = checked((uint)SimConstants.TICKS_PER_SIM_DAY);
            for (uint d = 0; d < days; d++)
            {
                host.Step(ticksPerDay);
            }
            ulong final = host.WorldStateHash();

            Checkpoint[] checkpoints = sink.ToArray();
            byte[] dump = Render(seed, registered, checkpoints);

            // Step 4: P is created as a new file, and the dump is written to it.
            try
            {
                using var file = new FileStream(outFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(dump, 0, dump.Length);
            }
            catch (IOException ex)
            {
                throw new HarnessFailure("cannot write " + outFile + ": " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new HarnessFailure("cannot write " + outFile + ": " + ex.Message);
            }

            ulong ticks = (ulong)days * (ulong)ticksPerDay;
            stdout.Write(
                "WROTE checkpoints ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                " checkpoints=" + checkpoints.Length.ToString(CultureInfo.InvariantCulture) +
                " final=" + final.ToString("x16", CultureInfo.InvariantCulture) + "\n");
            return 0;
        }

        // ---------------------------------------------------------------- paths

        /// <summary>
        /// §19.2b "Paths": a fully qualified path is used as given; any other is a
        /// <c>/</c>-separated repository-relative path joined to the root, which is found on the
        /// first need (§19.2c "Paths").
        /// </summary>
        private static string ResolvePath(string path, ref string? root)
        {
            if (Path.IsPathFullyQualified(path))
            {
                return path;
            }
            if (root == null)
            {
                try
                {
                    root = Phase0Composition.FindRoot();
                }
                catch (InvalidOperationException ex)
                {
                    throw new HarnessFailure(ex.Message);
                }
            }
            return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        }

        // ---------------------------------------------------------------- bundle

        private static byte[] ReadBundleFile(string bundleDir, string name)
        {
            string path = Path.Combine(bundleDir, name);
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new HarnessFailure("cannot read " + path + ": " + ex.Message);
            }
        }

        /// <summary>Reads and checks <c>bundle.json</c> by 16 §16.3's strict form (Q-069).</summary>
        private static HashSet<string> ReadBundle(string bundleDir, out ulong seed)
        {
            string path = Path.Combine(bundleDir, BundleFileName);
            byte[] bytes = ReadBundleFile(bundleDir, BundleFileName);

            object parsed;
            try
            {
                parsed = StrictJson.Parse(bytes);
            }
            catch (FormatException ex)
            {
                throw new HarnessFailure(path + ": " + ex.Message);
            }

            if (!(parsed is Dictionary<string, object> root))
            {
                throw new HarnessFailure(path + ": the document is not an object");
            }
            foreach (string key in root.Keys)
            {
                if (key != "schema_version" && key != "seed" && key != "systems")
                {
                    throw new HarnessFailure(path + ": unknown key '" + key + "'");
                }
            }
            if (!root.TryGetValue("schema_version", out object? version))
            {
                throw new HarnessFailure(path + ": missing key 'schema_version'");
            }
            if (!root.TryGetValue("seed", out object? seedValue))
            {
                throw new HarnessFailure(path + ": missing key 'seed'");
            }
            if (!root.TryGetValue("systems", out object? systemsValue))
            {
                throw new HarnessFailure(path + ": missing key 'systems'");
            }

            if (!(version is long v) || v != 1)
            {
                throw new HarnessFailure(path + ": schema_version must be 1");
            }
            if (!(seedValue is string seedText) || !IsCanonicalSeed(seedText) ||
                !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            {
                throw new HarnessFailure(path + ": seed must be a uint64 decimal string");
            }
            if (!(systemsValue is List<object> systems) || systems.Count == 0)
            {
                throw new HarnessFailure(path + ": systems must be a non-empty array");
            }

            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (object entry in systems)
            {
                if (!(entry is string name) || Array.IndexOf(Phase1Names, name) < 0)
                {
                    throw new HarnessFailure(path + ": systems holds an entry that is not a Phase 1 module name");
                }
                if (!listed.Add(name))
                {
                    throw new HarnessFailure(path + ": systems lists '" + name + "' twice");
                }
            }
            return listed;
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

        /// <summary>This stage composes world, schedule and flow only; flow needs world (16 §16.4).</summary>
        private static void CheckComposable(HashSet<string> listed)
        {
            foreach (string name in new[] { Airside, Turnaround, Delay })
            {
                if (listed.Contains(name))
                {
                    throw new HarnessFailure("the bundle lists " + name + ", which this harness stage does not compose");
                }
            }
            if (listed.Contains(Flow) && !listed.Contains(World))
            {
                throw new HarnessFailure(BundleFileName + ": " + Flow + " is listed without its downward interface " + World);
            }
        }

        // ---------------------------------------------------------------- content

        private static IContentIndex LoadContent(string contentDir)
        {
            if (!Directory.Exists(contentDir))
            {
                throw new HarnessFailure("content directory " + contentDir + " does not exist");
            }
            var source = new DirectorySource(contentDir);
            return ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(source));
        }

        /// <summary>Every file under the content directory, relative and <c>/</c>-separated (Q-072).</summary>
        private sealed class DirectorySource : IContentSource
        {
            private readonly List<string> _files = new List<string>();
            private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            internal DirectorySource(string directory)
            {
                string root = Path.GetFullPath(directory);
                try
                {
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                        _files.Add(relative);
                        _bytes.Add(relative, File.ReadAllBytes(file));
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new HarnessFailure("cannot read content under " + root + ": " + ex.Message);
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

        // ---------------------------------------------------------------- dump

        /// <summary>16 §16.8 checkpoint dump, version 1, byte for byte.</summary>
        private static byte[] Render(ulong seed, IReadOnlyList<ISimSystem> systems, Checkpoint[] checkpoints)
        {
            var sb = new StringBuilder();
            sb.Append("airport-sim-checkpoints 1\n");
            sb.Append("seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("systems");
            foreach (ISimSystem system in systems)
            {
                sb.Append(' ').Append(system.Name);
            }
            sb.Append('\n');

            foreach (Checkpoint cp in checkpoints)
            {
                sb.Append(cp.Tick.ToString(CultureInfo.InvariantCulture));
                sb.Append(' ').Append(cp.WorldHash.ToString("x16", CultureInfo.InvariantCulture));
                foreach (ulong h in cp.SystemHashes)
                {
                    sb.Append(' ').Append(h.ToString("x16", CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }
    }
}
