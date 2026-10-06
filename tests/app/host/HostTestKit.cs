using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// The repository root is the nearest ancestor of AppContext.BaseDirectory
    /// holding AirportSim.sln (07 "Fixture location", Q-031). A missing root
    /// fails the test.
    /// </summary>
    internal static class Repo
    {
        public static string Root()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(System.IO.Path.Combine(dir, "AirportSim.sln")))
            {
                dir = System.IO.Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            return dir!;
        }

        /// <summary>A '/'-separated repository-relative path, joined to the root.</summary>
        public static string Path(string relative)
        {
            return System.IO.Path.Combine(Root(), relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        public static byte[] Read(string relative)
        {
            return File.ReadAllBytes(Path(relative));
        }
    }

    /// <summary>
    /// The two Phase 1 test bundles of 16 §16.8 and their content, and the
    /// render layout fixture of 15 §15.12. Module names are 08 §8.5's (Q-069).
    /// </summary>
    internal static class Bundles
    {
        public const string Phase0 = "tests/fixtures/harness/checkpoints-phase0";
        public const string Phase0Content = "tests/fixtures/harness/phase0-content";
        public const string Phase1 = "tests/fixtures/harness/checkpoints-phase1";
        public const string Phase1Content = "data";
        public const string RenderLayout = "tests/fixtures/render/phase1-layout.json";

        /// <summary>Both bundles' bundle.json carry seed "12345" (19 §19.2c "The fixture").</summary>
        public const ulong Seed = 12345;

        /// <summary>TICKS_PER_SIM_DAY (08 §8.1) as the uint ISimHost.Step takes.</summary>
        public const uint Day = (uint)SimConstants.TICKS_PER_SIM_DAY;

        public const string World = "sim.world";
        public const string Schedule = "sim.schedule";
        public const string Airside = "sim.airside";
        public const string Flow = "sim.flow";
        public const string Turnaround = "sim.turnaround";
        public const string Delay = "sim.delay";

        /// <summary>08 §8.5 registry order of the Phase 1 set (16 §16.4 "Rules").</summary>
        public static readonly string[] RegistryOrder = { World, Schedule, Airside, Flow, Turnaround, Delay };

        /// <summary>08 §8.5 registry positions, in RegistryOrder.</summary>
        public static readonly ushort[] RegistryIds = { 1, 2, 3, 4, 5, 7 };

        /// <summary>The Phase 1 bundle's files, one row of 16 §16.3 each (render_layout.fixture aside).</summary>
        public static readonly string[] Phase1Files =
        {
            "world.fixture", "schedule.csv", "airside.fixture", "airside_rules.json", "flow.fixture", "turnaround.fixture",
        };

        public static readonly string[] Phase0Systems = { World, Schedule, Flow };

        private static readonly Dictionary<string, IReadOnlyList<IContentDefinition>> Loaded =
            new Dictionary<string, IReadOnlyList<IContentDefinition>>(StringComparer.Ordinal);

        /// <summary>
        /// The definitions of a content directory, loaded with 08 §8.11's loader
        /// as 19 §19.2c "The content" loads it (Q-072). Tests may supply
        /// definitions directly (16 §16.3); these are those definitions.
        /// </summary>
        public static IReadOnlyList<IContentDefinition> Content(string directory)
        {
            if (!Loaded.TryGetValue(directory, out IReadOnlyList<IContentDefinition>? defs))
            {
                defs = ContentLoaderFactory.Create().Load(new DirectorySource(Repo.Path(directory)));
                Loaded.Add(directory, defs);
            }

            return defs;
        }

        /// <summary>A bundle over the files of a fixture directory, plus extra named files.</summary>
        public static MemoryBundle FromDirectory(string directory, params string[] names)
        {
            var bundle = new MemoryBundle();
            bundle.Put("bundle.json", Repo.Read(directory + "/bundle.json"));
            foreach (string name in names)
            {
                bundle.Put(name, Repo.Read(directory + "/" + name));
            }

            return bundle;
        }

        /// <summary>The Phase 1 checkpoints bundle in memory, every file present.</summary>
        public static MemoryBundle Phase1Bundle()
        {
            return FromDirectory(Phase1, Phase1Files);
        }

        /// <summary>The Phase 0 checkpoints bundle in memory (19 §19.2c, Q-076).</summary>
        public static MemoryBundle Phase0Bundle()
        {
            return FromDirectory(Phase0, "world.fixture", "schedule.csv", "flow.fixture");
        }

        /// <summary>
        /// A bundle.json in 16 §16.3's strict form, schema_version 1, with the
        /// given seed text and systems, in the given order.
        /// </summary>
        public static byte[] BundleJson(string seed, params string[] systems)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema_version\": 1,\n  \"seed\": \"").Append(seed).Append("\",\n  \"systems\": [ ");
            for (int i = 0; i < systems.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('"').Append(systems[i]).Append('"');
            }

            sb.Append(" ]\n}\n");
            return Utf8(sb.ToString());
        }

        public static byte[] Utf8(string text)
        {
            return new UTF8Encoding(false).GetBytes(text);
        }
    }

    /// <summary>
    /// 19 §19.2c "The content" (Q-072): Files() is every file under the content
    /// directory, at any depth, relative to it and '/'-separated.
    /// </summary>
    internal sealed class DirectorySource : IContentSource
    {
        private readonly List<string> _files = new List<string>();
        private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public DirectorySource(string directory)
        {
            string root = Path.GetFullPath(directory);
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                _files.Add(relative);
                _bytes.Add(relative, File.ReadAllBytes(file));
            }

            Assert.NotEmpty(_files);
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

    /// <summary>
    /// An IScenarioBundle over named byte arrays (16 §16.3). It can only be read
    /// by exact name: it has no listing. Every call is recorded, in order.
    /// </summary>
    internal sealed class MemoryBundle : IScenarioBundle
    {
        public readonly List<string> Reads = new List<string>();

        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public MemoryBundle Put(string name, byte[] bytes)
        {
            _files[name] = bytes;
            return this;
        }

        public MemoryBundle Remove(string name)
        {
            Assert.True(_files.Remove(name), "the bundle has no " + name);
            return this;
        }

        public bool Has(string fileName)
        {
            return fileName != null && _files.ContainsKey(fileName);
        }

        public byte[] ReadAll(string fileName)
        {
            Reads.Add(fileName);
            if (fileName == null || !_files.TryGetValue(fileName, out byte[]? bytes))
            {
                throw new FileNotFoundException("the test bundle has no file " + fileName, fileName);
            }

            return (byte[])bytes.Clone();
        }

        public byte[] Bytes(string name)
        {
            return _files[name];
        }
    }

    /// <summary>Records every checkpoint in order (08 §8.9).</summary>
    internal sealed class RecordingSink : ICheckpointSink
    {
        public readonly List<ulong> Ticks = new List<ulong>();
        public readonly List<ulong> WorldHashes = new List<ulong>();
        public readonly List<ulong[]> SystemHashes = new List<ulong[]>();

        public void Record(in Checkpoint cp)
        {
            Ticks.Add(cp.Tick);
            WorldHashes.Add(cp.WorldHash);
            SystemHashes.Add((ulong[])cp.SystemHashes.Clone());
        }
    }

    internal sealed class DiscardLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    /// <summary>One composed and stepped run: the host, its systems in registry order, and its checkpoints.</summary>
    internal sealed class KitRun
    {
        public KitRun(ISimHost host, IReadOnlyList<ISimSystem> registered, RecordingSink sink)
        {
            Host = host;
            Registered = registered;
            Sink = sink;
        }

        public ISimHost Host { get; }

        public IReadOnlyList<ISimSystem> Registered { get; }

        public RecordingSink Sink { get; }

        public List<string> Names()
        {
            var names = new List<string>();
            foreach (ISimSystem s in Registered)
            {
                names.Add(s.Name);
            }

            return names;
        }

        public byte[] Dump(ulong seed)
        {
            return Dumps.Render(seed, Names(), Sink);
        }
    }

    /// <summary>
    /// The test's own composition of a bundle, through the published surface
    /// only, by 16 §16.4 steps 1 to 4 exactly: sourceName the bundle file name
    /// (Q-073), construction in dependency order, registration in registry
    /// order, nothing else registered and no command submitted. It is the
    /// reference ISimComposer.Compose is compared with. Written from the spec,
    /// never from an implementation.
    /// </summary>
    internal static class Kit
    {
        private static readonly Dictionary<string, IContentIndex> Indexes = new Dictionary<string, IContentIndex>(StringComparer.Ordinal);

        public static IContentIndex Index(string contentDirectory)
        {
            if (!Indexes.TryGetValue(contentDirectory, out IContentIndex? index))
            {
                index = ContentIndexFactory.Create(Bundles.Content(contentDirectory));
                Indexes.Add(contentDirectory, index);
            }

            return index;
        }

        /// <summary>04-data-schemas.md's AirsideRules, both keys (16 §16.3, Q-047).</summary>
        private static AirsideRules ParseRules(byte[] json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            return new AirsideRules(
                root.GetProperty("boarding_hold_max_minutes").GetUInt32(),
                root.GetProperty("doors_open_delay_minutes").GetUInt32());
        }

        /// <summary>Composes <paramref name="listed"/> from <paramref name="bundle"/>'s files and builds the host.</summary>
        public static KitRun Compose(MemoryBundle bundle, string contentDirectory, ulong seed, IReadOnlyCollection<string> listed)
        {
            var set = new HashSet<string>(listed, StringComparer.Ordinal);
            Func<string, byte[]> read = name => (byte[])bundle.Bytes(name).Clone();

            var sink = new RecordingSink();
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(new SimHostConfig(seed, Index(contentDirectory), sink, new DiscardLog()));
            SystemServices services = builder.Services;

            var built = new Dictionary<string, ISimSystem>(StringComparer.Ordinal);
            IWorldSystem? world = null;
            if (set.Contains(Bundles.World))
            {
                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(read("world.fixture"), "world.fixture");
                world = WorldFactory.CreateSystem(services, walk);
                built.Add(Bundles.World, world);
            }

            IFlowSystem? flow = null;
            if (set.Contains(Bundles.Flow))
            {
                Assert.NotNull(world);
                FlowGraph graph = FlowFactory.CreateGraphLoader().Load(read("flow.fixture"), "flow.fixture", world!);
                flow = FlowFactory.CreateSystem(services, graph, world!);
                built.Add(Bundles.Flow, flow);
            }

            IScheduleSystem? schedule = null;
            if (set.Contains(Bundles.Schedule))
            {
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(read("schedule.csv"), "schedule.csv");
                schedule = ScheduleFactory.CreateSystem(services, table, flow);
                built.Add(Bundles.Schedule, schedule);
            }

            if (set.Contains(Bundles.Airside))
            {
                Assert.NotNull(schedule);
                AirsideLayout layout = AirsideFactory.CreateLayoutLoader().Parse(read("airside.fixture"), "airside.fixture");
                AirsideRules rules = ParseRules(read("airside_rules.json"));
                built.Add(Bundles.Airside, AirsideFactory.CreateSystem(services, layout, rules, schedule!, flow, set.Contains(Bundles.Turnaround)));
            }

            if (set.Contains(Bundles.Turnaround))
            {
                Assert.NotNull(schedule);
                TurnaroundSetup setup = TurnaroundFactory.CreateSetupLoader().Load(read("turnaround.fixture"), "turnaround.fixture");
                built.Add(Bundles.Turnaround, TurnaroundFactory.CreateSystem(services, setup, schedule!));
            }

            if (set.Contains(Bundles.Delay))
            {
                built.Add(Bundles.Delay, DelayFactory.CreateSystem(services));
            }

            Assert.Equal(set.Count, built.Count);

            var registered = new List<ISimSystem>();
            foreach (string name in Bundles.RegistryOrder)
            {
                if (built.TryGetValue(name, out ISimSystem? system))
                {
                    builder.Register(system);
                    registered.Add(system);
                }
            }

            return new KitRun(builder.Build(), registered, sink);
        }
    }

    /// <summary>The test's own rendering of 16 §16.8's checkpoint dump, version 1.</summary>
    internal static class Dumps
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string Hex16(ulong v)
        {
            return v.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// UTF-8 without a BOM, LF line endings, a final newline, single spaces,
        /// decimal ticks and 16 lowercase hexadecimal digits per hash.
        /// </summary>
        public static byte[] Render(ulong seed, IReadOnlyList<string> systemNames, RecordingSink sink)
        {
            var sb = new StringBuilder();
            sb.Append("airport-sim-checkpoints 1\n");
            sb.Append("seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("systems");
            foreach (string name in systemNames)
            {
                sb.Append(' ').Append(name);
            }

            sb.Append('\n');
            for (int i = 0; i < sink.Ticks.Count; i++)
            {
                sb.Append(sink.Ticks[i].ToString(CultureInfo.InvariantCulture));
                sb.Append(' ').Append(Hex16(sink.WorldHashes[i]));
                foreach (ulong h in sink.SystemHashes[i])
                {
                    sb.Append(' ').Append(Hex16(h));
                }

                sb.Append('\n');
            }

            return Utf8NoBom.GetBytes(sb.ToString());
        }

        public static void AssertBytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(what + " differs from the expected dump.\nexpected:\n"
                    + Encoding.UTF8.GetString(expected) + "\nactual:\n" + Encoding.UTF8.GetString(actual));
            }
        }
    }

    /// <summary>A fresh temporary directory, deleted on Dispose. Tests write no repository file.</summary>
    internal sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airport-sim-t031-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
