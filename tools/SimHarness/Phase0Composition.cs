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
        private const string ContentManifestPath = "tests/fixtures/harness/phase0-content.files";
        private const string ContentDirectory = "tests/fixtures/harness/phase0-content/";
        private const string WorldPath = "tests/fixtures/world/phase0-landside.json";
        private const string FlowPath = "tests/fixtures/flow/phase0-landside.flow.json";
        private const string SchedulePath = "tests/fixtures/schedule/phase0-200.csv";

        private readonly byte[] _world;
        private readonly byte[] _flow;
        private readonly byte[] _schedule;

        private Phase0Composition(IContentIndex content, byte[] world, byte[] flow, byte[] schedule)
        {
            Content = content;
            _world = world;
            _flow = flow;
            _schedule = schedule;
        }

        internal IContentIndex Content { get; }

        /// <summary>
        /// Finds the repository root, reads every fixture file once and loads the content
        /// once (§19.2a "When"). Any failure is an exception, which the CLI maps to exit 3.
        /// </summary>
        internal static Phase0Composition Load()
        {
            string root = FindRoot();
            var source = new ManifestSource(root);
            IContentIndex content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(source));
            return new Phase0Composition(
                content,
                ReadRepoFile(root, WorldPath),
                ReadRepoFile(root, FlowPath),
                ReadRepoFile(root, SchedulePath));
        }

        /// <summary>Every call parses the graphs and schedule afresh and registers fresh systems.</summary>
        internal void Compose(ISimHostBuilder builder)
        {
            SystemServices services = builder.Services;

            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, "phase0-landside.json");
            IWorldSystem world = WorldFactory.CreateSystem(services, walk);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, "phase0-landside.flow.json", world);
            IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, "phase0-200.csv");
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);
            var boarding = new BoardingStandIn(schedule, flow);

            builder.Register(world);
            builder.Register(schedule);
            builder.Register(boarding);
            builder.Register(flow);
        }

        private static string FindRoot()
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

            internal ManifestSource(string root)
            {
                string text = new UTF8Encoding(false, true).GetString(ReadRepoFile(root, ContentManifestPath));
                if (text.Length == 0 || text[text.Length - 1] != '\n')
                {
                    throw new InvalidOperationException("harness: " + ContentManifestPath + " must end in LF");
                }
                foreach (string line in text.Substring(0, text.Length - 1).Split('\n'))
                {
                    if (line.Length == 0 || line.IndexOf('\r') >= 0)
                    {
                        throw new InvalidOperationException("harness: " + ContentManifestPath + " has a bad line '" + line + "'");
                    }
                    if (_bytes.ContainsKey(line))
                    {
                        throw new InvalidOperationException("harness: " + ContentManifestPath + " has a duplicate line '" + line + "'");
                    }
                    _files.Add(line);
                    _bytes.Add(line, ReadRepoFile(root, ContentDirectory + line));
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
