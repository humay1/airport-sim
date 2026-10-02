using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// The checkpoints kit of 19 §19.8. It composes the Phase 0 checkpoints bundle
    /// (§19.2c "The fixture", Q-076) itself, through the published surface only,
    /// exactly as §19.2c's composition does: 16 §16.4 steps 1 to 4, sourceName the
    /// bundle file name (Q-073), nothing else registered (Q-070) and no command
    /// submitted (Q-071). It renders the expected dump with its own code from
    /// 16 §16.8's format. Written from the spec, never from an implementation.
    /// </summary>
    internal static class CheckpointsKit
    {
        internal const string BundleDirectory = "tests/fixtures/harness/checkpoints-phase0";
        internal const string ContentDirectory = "tests/fixtures/harness/phase0-content";

        /// <summary>The bundle's seed: bundle.json's "seed" is "12345" (§19.2c "The fixture").</summary>
        internal const ulong BundleSeed = 12345;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static IContentIndex? _content;

        /// <summary>The CLI arguments of §19.8's default invocation, with the given --out path.</summary>
        internal static string[] DefaultArgs(string outPath)
        {
            return new[]
            {
                "checkpoints",
                "--bundle", BundleDirectory,
                "--content", ContentDirectory,
                "--days", "1",
                "--out", outPath,
            };
        }

        /// <summary>
        /// §19.2c "The content" (Q-072): Files() is every file under the content
        /// directory, at any depth, relative to it and '/'-separated. ReadAll
        /// returns that file's bytes.
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

        /// <summary>The kit content, loaded once with ContentLoaderFactory then ContentIndexFactory (08 §8.11).</summary>
        internal static IContentIndex Content()
        {
            if (_content == null)
            {
                var source = new DirectorySource(KillGateKit.RepoPath(ContentDirectory));
                _content = ContentIndexFactory.Create(ContentLoaderFactory.Create().Load(source));
            }

            return _content;
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

        private sealed class DiscardLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }

        /// <summary>One kit run: the dump's bytes, its checkpoint count and the final WorldStateHash().</summary>
        internal sealed class KitRun
        {
            public KitRun(byte[] dump, int checkpoints, ulong final)
            {
                Dump = dump;
                Checkpoints = checkpoints;
                Final = final;
            }

            public byte[] Dump { get; }
            public int Checkpoints { get; }
            public ulong Final { get; }
        }

        private static byte[] BundleFile(string name)
        {
            return KillGateKit.Fixture(BundleDirectory + "/" + name);
        }

        /// <summary>
        /// Composes the bundle (16 §16.4) and steps it with <paramref name="step"/>,
        /// which must run exactly one sim-day of ticks.
        /// </summary>
        internal static KitRun Run(Action<ISimHost> step)
        {
            var sink = new RecordingSink();
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(new SimHostConfig(BundleSeed, Content(), sink, new DiscardLog()));
            SystemServices services = builder.Services;

            // 16 §16.4 step 2: each listed system's file, with sourceName the bundle file name.
            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(BundleFile("world.fixture"), "world.fixture");

            // Step 3, dependency order: world; flow(world); schedule(flow).
            IWorldSystem world = WorldFactory.CreateSystem(services, walk);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(BundleFile("flow.fixture"), "flow.fixture", world);
            IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(BundleFile("schedule.csv"), "schedule.csv");
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);

            // Step 4, registry order (08 §8.5), and nothing else (Q-070).
            var registered = new ISimSystem[] { world, schedule, flow };
            foreach (ISimSystem system in registered)
            {
                builder.Register(system);
            }

            ISimHost host = builder.Build();
            step(host);
            Assert.Equal((ulong)HarnessTestKit.TicksPerDay, host.CurrentTick);
            ulong final = host.WorldStateHash();

            var names = new List<string>();
            foreach (ISimSystem system in registered)
            {
                names.Add(system.Name);
            }

            return new KitRun(Render(BundleSeed, names, sink), sink.Ticks.Count, final);
        }

        /// <summary>One Step(TICKS_PER_SIM_DAY), the stepping §19.2c pins for one day.</summary>
        internal static KitRun RunOneStep()
        {
            return Run(host => host.Step(HarnessTestKit.TicksPerDay));
        }

        /// <summary>
        /// 16 §16.8 checkpoint dump, version 1: UTF-8 without a BOM, LF line
        /// endings, a final newline, single spaces, decimal ticks and 16 lowercase
        /// hexadecimal digits per hash.
        /// </summary>
        internal static byte[] Render(ulong seed, IReadOnlyList<string> systemNames, RecordingSink sink)
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
                sb.Append(' ').Append(HarnessTestKit.Hex16(sink.WorldHashes[i]));
                foreach (ulong h in sink.SystemHashes[i])
                {
                    sb.Append(' ').Append(HarnessTestKit.Hex16(h));
                }

                sb.Append('\n');
            }

            return Utf8NoBom.GetBytes(sb.ToString());
        }

        /// <summary>A fresh temporary directory, deleted on Dispose (§19.7, §19.8).</summary>
        internal sealed class TempDir : IDisposable
        {
            public TempDir()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airport-sim-t030-" + Guid.NewGuid().ToString("N"));
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
}
