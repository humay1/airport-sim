using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AirportSim.App.Host;
using AirportSim.Sim.Core;
using AirportSim.Tools.SimHarness;
using Xunit;

namespace AirportSim.Integration.Tests
{
    /// <summary>
    /// T-031. D7's equivalence (16 §16.8, Q-077): the harness's `checkpoints`
    /// subcommand (19 §19.2c) and app.host's IHeadlessRun, both in process on
    /// net8.0, on the same bundle and content for one sim-day, write
    /// byte-identical dumps. Both sides are called through their public
    /// surfaces only (HarnessCli.Run, HostFactory). No process is spawned and
    /// no golden is committed: the two dumps are compared directly. Every
    /// output is in a fresh temporary directory that the test deletes.
    /// </summary>
    public sealed class HostCompositionTests
    {
        /// <summary>The repository root, found as 07 "Fixture location" says (Q-031).</summary>
        private static string Root()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            return dir!;
        }

        private static string RepoPath(string relative)
        {
            return Path.Combine(Root(), relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>A bundle directory read by exact file name only, never listed (16 §16.3).</summary>
        private sealed class DirectoryBundle : IScenarioBundle
        {
            private readonly string _directory;

            public DirectoryBundle(string directory)
            {
                _directory = directory;
            }

            public bool Has(string fileName)
            {
                return File.Exists(Path.Combine(_directory, fileName));
            }

            public byte[] ReadAll(string fileName)
            {
                return File.ReadAllBytes(Path.Combine(_directory, fileName));
            }
        }

        /// <summary>
        /// A content directory as 19 §19.2c "The content" lists it (Q-072): every
        /// file at any depth, relative and '/'-separated. The player's content is
        /// a copy of data/ loaded the same way (16 §16.3).
        /// </summary>
        private sealed class DirectorySource : IContentSource
        {
            private readonly string _root;
            private readonly List<string> _files = new List<string>();

            public DirectorySource(string directory)
            {
                _root = Path.GetFullPath(directory);
                foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                {
                    _files.Add(Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }

                Assert.NotEmpty(_files);
            }

            public IReadOnlyList<string> Files()
            {
                return _files;
            }

            public byte[] ReadAll(string path)
            {
                return File.ReadAllBytes(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(what + ": the host's dump differs from the harness's.\nharness:\n"
                    + Encoding.UTF8.GetString(expected) + "\nhost:\n" + Encoding.UTF8.GetString(actual));
            }
        }

        // 07 L11a: 57 600 ticks, so not Slow by rule (a), but over 5 s in the
        // Test Author's Release run (6 s), so tagged until CI measures it.
        [Fact]
        [Trait("Category", "Slow")]
        public void test_host_composition_matches_harness_checkpoints()
        {
            var bundles = new List<(string Bundle, string Content, string Systems)>
            {
                // sim.world, sim.schedule and sim.flow, no boarding stand-in (19 §19.2a).
                ("tests/fixtures/harness/checkpoints-phase0", "tests/fixtures/harness/phase0-content", "systems sim.world sim.schedule sim.flow"),

                // All six Phase 1 systems over data/ (T-048, 19 §19.2c "The fixture").
                ("tests/fixtures/harness/checkpoints-phase1", "data", "systems sim.world sim.schedule sim.airside sim.flow sim.turnaround sim.delay"),
            };

            string tmp = Path.Combine(Path.GetTempPath(), "airport-sim-t031-d7-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                int index = 0;
                foreach ((string bundle, string content, string systems) in bundles)
                {
                    string bundleDir = RepoPath(bundle);
                    string contentDir = RepoPath(content);
                    string harnessOut = Path.Combine(tmp, "harness-" + index);
                    string hostOut = Path.Combine(tmp, "host-" + index);
                    index++;

                    // The harness side, through HarnessCli.Run (19 §19.1), one day.
                    var stdout = new StringWriter();
                    var stderr = new StringWriter();
                    int harnessExit = HarnessCli.Run(
                        new[] { "checkpoints", "--bundle", bundleDir, "--content", contentDir, "--days", "1", "--out", harnessOut },
                        stdout,
                        stderr);
                    Assert.True(harnessExit == 0, bundle + ": the harness exited " + harnessExit + ", stderr: " + stderr);

                    // The host side, through HostFactory (16 §16.3, §16.4, §16.8), one day.
                    IReadOnlyList<IContentDefinition> definitions = ContentLoaderFactory.Create().Load(new DirectorySource(contentDir));
                    IHeadlessRun run = HostFactory.CreateHeadlessRun(HostFactory.CreateSimComposer(definitions));
                    int hostExit = run.Run(new DirectoryBundle(bundleDir), new CheckpointRunRequest(1, hostOut));
                    Assert.True(hostExit == 0, bundle + ": IHeadlessRun.Run returned " + hostExit);

                    byte[] harnessDump = File.ReadAllBytes(harnessOut);
                    byte[] hostDump = File.ReadAllBytes(hostOut);
                    AssertBytesEqual(harnessDump, hostDump, bundle);

                    // Both dumps are real: the 16 §16.8 header with this bundle's systems, and a day of checkpoints.
                    string text = Encoding.UTF8.GetString(hostDump);
                    Assert.StartsWith("airport-sim-checkpoints 1\nseed 12345\n" + systems + "\n", text, StringComparison.Ordinal);
                    Assert.Equal(3 + 24, text.Split('\n').Length - 1);
                }
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }
    }
}
