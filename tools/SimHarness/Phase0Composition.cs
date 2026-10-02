using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The Phase 0 CLI composition: world, schedule, boarding stand-in, flow, over the four
    /// repository fixtures. Spec: 19-interfaces-harness.md §19.2a (Q-042, Q-043).
    /// </summary>
    internal sealed class Phase0Composition
    {
        /// <summary>One fixture set's repository paths and <c>sourceName</c>s (§19.2a, §19.2b).</summary>
        internal sealed class FixtureSet
        {
            internal FixtureSet(
                string contentManifestPath,
                string contentDirectory,
                string worldPath,
                string flowPath,
                string schedulePath)
            {
                ContentManifestPath = contentManifestPath;
                ContentDirectory = contentDirectory;
                WorldPath = worldPath;
                FlowPath = flowPath;
                SchedulePath = schedulePath;
            }

            internal string ContentManifestPath { get; }

            internal string ContentDirectory { get; }

            internal string WorldPath { get; }

            internal string FlowPath { get; }

            internal string SchedulePath { get; }
        }

        /// <summary>The Phase 0 fixture set (§19.2a).</summary>
        internal static readonly FixtureSet Phase0 = new FixtureSet(
            "tests/fixtures/harness/phase0-content.files",
            "tests/fixtures/harness/phase0-content/",
            "tests/fixtures/world/phase0-landside.json",
            "tests/fixtures/flow/phase0-landside.flow.json",
            "tests/fixtures/schedule/phase0-200.csv");

        /// <summary>The soak fixture set (§19.2b).</summary>
        internal static readonly FixtureSet Soak = new FixtureSet(
            "tests/fixtures/soak/soak-content.files",
            "tests/fixtures/soak/soak-content/",
            "tests/fixtures/soak/soak-landside.json",
            "tests/fixtures/soak/soak-landside.flow.json",
            "tests/fixtures/soak/soak.csv");

        private readonly string _worldName;
        private readonly string _flowName;
        private readonly string _scheduleName;
        private readonly byte[] _world;
        private readonly byte[] _flow;
        private readonly byte[] _schedule;

        private Phase0Composition(IContentIndex content, FixtureSet set, byte[] world, byte[] flow, byte[] schedule)
        {
            Content = content;
            _worldName = Path.GetFileName(set.WorldPath);
            _flowName = Path.GetFileName(set.FlowPath);
            _scheduleName = Path.GetFileName(set.SchedulePath);
            _world = world;
            _flow = flow;
            _schedule = schedule;
        }

        internal IContentIndex Content { get; }

        /// <summary>The Names of the systems the last <see cref="Compose"/> call registered, in registry order.</summary>
        internal string[] RegisteredNames { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Finds the repository root, reads every fixture file once and loads the content
        /// once (§19.2a "When"). Any failure is an exception, which the CLI maps to exit 3.
        /// </summary>
        internal static Phase0Composition Load()
        {
            return Load(Phase0);
        }

        /// <summary>As <see cref="Load()"/>, over the given fixture set.</summary>
        internal static Phase0Composition Load(FixtureSet set)
        {
            string root = FindRoot();
            var source = new ManifestSource(root, set);
            IContentIndex content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(source));
            return new Phase0Composition(
                content,
                set,
                ReadRepoFile(root, set.WorldPath),
                ReadRepoFile(root, set.FlowPath),
                ReadRepoFile(root, set.SchedulePath));
        }

        /// <summary>Every call parses the graphs and schedule afresh and registers fresh systems.</summary>
        internal void Compose(ISimHostBuilder builder)
        {
            SystemServices services = builder.Services;

            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, _worldName);
            IWorldSystem world = WorldFactory.CreateSystem(services, walk);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, _flowName, world);
            IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, _scheduleName);
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);
            var boarding = new BoardingStandIn(schedule, flow);

            builder.Register(world);
            builder.Register(schedule);
            builder.Register(boarding);
            builder.Register(flow);
            RegisteredNames = new[] { world.Name, schedule.Name, boarding.Name, flow.Name };
        }

        internal static string FindRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (dir == null)
            {
                throw new InvalidOperationException("harness: no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            }
            return dir;
        }

        private static byte[] ReadRepoFile(string root, string relativePath)
        {
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("harness: cannot read fixture " + relativePath + ": " + ex.Message, ex);
            }
        }

        /// <summary>Exactly the manifest's lines, and those files' bytes (read once).</summary>
        private sealed class ManifestSource : IContentSource
        {
            private readonly List<string> _files = new List<string>();
            private readonly Dictionary<string, byte[]> _bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            internal ManifestSource(string root, FixtureSet set)
            {
                string manifest = set.ContentManifestPath;
                string text = new UTF8Encoding(false, true).GetString(ReadRepoFile(root, manifest));
                if (text.Length == 0 || text[text.Length - 1] != '\n')
                {
                    throw new InvalidOperationException("harness: " + manifest + " must end in LF");
                }
                foreach (string line in text.Substring(0, text.Length - 1).Split('\n'))
                {
                    if (line.Length == 0 || line.IndexOf('\r') >= 0)
                    {
                        throw new InvalidOperationException("harness: " + manifest + " has a bad line '" + line + "'");
                    }
                    if (_bytes.ContainsKey(line))
                    {
                        throw new InvalidOperationException("harness: " + manifest + " has a duplicate line '" + line + "'");
                    }
                    _files.Add(line);
                    _bytes.Add(line, ReadRepoFile(root, set.ContentDirectory + line));
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
    }
}
