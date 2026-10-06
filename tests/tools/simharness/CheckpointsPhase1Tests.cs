using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;
using K = AirportSim.Tools.SimHarness.Tests.Phase1CheckpointsKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-048. The Phase 1 stage of the `checkpoints` subcommand (19 §19.2c
    /// "Which systems it composes"): sim.airside, sim.turnaround and sim.delay
    /// are composed with the T-030 systems, in 16 §16.4's dependency order and
    /// 08 §8.5's registry order, over --content data. In process through
    /// HarnessCli.Run; every --out path and every bundle written by a test is in
    /// a fresh temporary directory that the test deletes.
    /// </summary>
    public sealed class CheckpointsPhase1Tests
    {
        /// <summary>The Phase 1 bundle's files that sim.airside does not read (16 §16.3).</summary>
        private static readonly string[] NonAirsideFiles = { "world.fixture", "schedule.csv", "flow.fixture", "turnaround.fixture" };

        /// <summary>
        /// Every Phase 1 system except sim.airside, deliberately not in registry
        /// order: 16 §16.3, the order of "systems" does not matter.
        /// </summary>
        private static readonly string[] NonAirsideSystems = { K.Delay, K.Turnaround, K.Flow, K.Schedule, K.World };

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(what + " differs from the expected dump.\nexpected:\n"
                    + Encoding.UTF8.GetString(expected) + "\nactual:\n" + Encoding.UTF8.GetString(actual));
            }
        }

        /// <summary>
        /// 16 §16.8: 3 header lines with the given systems line, then 24 checkpoint
        /// lines at ticks 0, 600, ..., each a tick, the world hash and one hash per
        /// registered system.
        /// </summary>
        private static void AssertDumpShape(byte[] dump, string systemsLine, int systems)
        {
            string text = Encoding.UTF8.GetString(dump);
            Assert.StartsWith("airport-sim-checkpoints 1\nseed 12345\n" + systemsLine + "\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("\n", text);
            string[] lines = text.Substring(0, text.Length - 1).Split('\n');
            Assert.Equal(3 + 24, lines.Length);
            var checkpointLine = new Regex("^(0|[1-9][0-9]*)( [0-9a-f]{16}){" + (1 + systems).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}$");
            for (int i = 3; i < lines.Length; i++)
            {
                Assert.Matches(checkpointLine, lines[i]);
                Assert.StartsWith(((i - 3) * 600).ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", lines[i], StringComparison.Ordinal);
            }
        }

        // ------------------------------------------------------------ composition

        [Fact]
        public void test_checkpoints_phase1_bundle_composes_every_phase1_system()
        {
            // §19.8: over the Phase 1 checkpoints bundle with --content data and
            // --days 1, the systems line holds all six Phase 1 systems in registry
            // order, and the file is byte-identical to the Phase 1 checkpoints kit's
            // dump, built with all six factories over data/ (Q-113).
            using var tmp = new CheckpointsKit.TempDir();
            CliResult r = Cli(K.Args(K.BundleDirectory, tmp.File("a")));
            Assert.True(r.Exit == 0, "exit " + r.Exit + ", stderr: " + r.Stderr);

            CheckpointsKit.KitRun kit = K.RunOneStep(KillGateKit.RepoPath(K.BundleDirectory), K.RegistryOrder);
            Assert.Equal(24, kit.Checkpoints);
            Assert.Equal("WROTE checkpoints ticks=14400 checkpoints=24 final=" + Hex16(kit.Final) + "\n", r.Stdout);

            byte[] dump = File.ReadAllBytes(tmp.File("a"));
            AssertBytesEqual(kit.Dump, dump, "<tmp>/a");
            AssertDumpShape(dump, "systems sim.world sim.schedule sim.airside sim.flow sim.turnaround sim.delay", 6);
        }

        [Fact]
        public void test_checkpoints_phase1_bundle_without_airside_composes_turnaround_and_delay()
        {
            // §19.2c: the Phase 1 stage composes sim.turnaround (turnaround.fixture)
            // and sim.delay (no file) with the T-030 systems. A turnaround without
            // sim.airside is a valid composition: 16 §16.4 requires only schedule
            // below it, and 12 §12.12a's turnaroundRegistered is the airside side.
            using var tmp = new CheckpointsKit.TempDir();
            string bundle = tmp.File("bundle");
            K.WriteBundle(bundle, NonAirsideSystems, NonAirsideFiles);

            CliResult r = Cli(K.Args(bundle, tmp.File("a")));
            Assert.True(r.Exit == 0, "exit " + r.Exit + ", stderr: " + r.Stderr);

            CheckpointsKit.KitRun kit = K.RunOneStep(bundle, NonAirsideSystems);
            Assert.Equal(24, kit.Checkpoints);
            Assert.Equal("WROTE checkpoints ticks=14400 checkpoints=24 final=" + Hex16(kit.Final) + "\n", r.Stdout);

            byte[] dump = File.ReadAllBytes(tmp.File("a"));
            AssertBytesEqual(kit.Dump, dump, "<tmp>/a");

            // Registry order (08 §8.5), not the bundle's order.
            AssertDumpShape(dump, "systems sim.world sim.schedule sim.flow sim.turnaround sim.delay", 5);

            // 16 §16.4: composition is a pure function of the bundle and the content.
            CliResult again = Cli(K.Args(bundle, tmp.File("b")));
            Assert.Equal(0, again.Exit);
            Assert.Equal(r.Stdout, again.Stdout);
            AssertBytesEqual(dump, File.ReadAllBytes(tmp.File("b")), "<tmp>/b");
        }

        [Fact]
        public void test_checkpoints_delay_only_bundle_composes_without_a_file()
        {
            // 16 §16.3: sim.delay needs no file of its own, and 14 §14.13a constructs
            // it from builder.Services alone. A bundle of bundle.json only, listing
            // sim.delay, composes: §19.2c reads exactly the listed systems' files.
            using var tmp = new CheckpointsKit.TempDir();
            string bundle = tmp.File("bundle");
            K.WriteBundle(bundle, new[] { K.Delay });

            CliResult r = Cli(K.Args(bundle, tmp.File("a")));
            Assert.True(r.Exit == 0, "exit " + r.Exit + ", stderr: " + r.Stderr);

            CheckpointsKit.KitRun kit = K.RunOneStep(bundle, new[] { K.Delay });
            Assert.Equal("WROTE checkpoints ticks=14400 checkpoints=24 final=" + Hex16(kit.Final) + "\n", r.Stdout);

            byte[] dump = File.ReadAllBytes(tmp.File("a"));
            AssertBytesEqual(kit.Dump, dump, "<tmp>/a");
            AssertDumpShape(dump, "systems sim.delay", 1);

            // The harness writes nothing except P (§19.2c "Paths").
            Assert.Equal(new[] { "bundle.json" }, Array.ConvertAll(Directory.GetFiles(bundle), f => Path.GetFileName(f)));
        }

        // ------------------------------------------------------------ failures

        [Fact]
        public void test_checkpoints_phase1_unloadable_bundles_exit_3()
        {
            // Control: a Phase 1 bundle with sim.turnaround and sim.delay composes,
            // so each 3 that follows comes from the failure case and not from
            // T-030's rejection of a system it does not compose.
            using (var control = new CheckpointsKit.TempDir())
            {
                string bundle = control.File("bundle");
                K.WriteBundle(bundle, NonAirsideSystems, NonAirsideFiles);
                CliResult ok = Cli(K.Args(bundle, control.File("a")));
                Assert.True(ok.Exit == 0, "control: exit " + ok.Exit + ", stderr: " + ok.Stderr);
                Assert.True(File.Exists(control.File("a")), "control wrote no dump");
            }

            string[] all = K.RegistryOrder;
            string[] allFiles = { "world.fixture", "schedule.csv", "airside.fixture", "airside_rules.json", "flow.fixture", "turnaround.fixture" };
            var cases = new List<(string Label, string[] Systems, string[] Files, string? Named)>
            {
                // 16 §16.4: airside or turnaround without schedule is a load failure.
                ("turnaround without schedule", new[] { K.World, K.Flow, K.Turnaround }, new[] { "world.fixture", "flow.fixture", "turnaround.fixture" }, null),
                ("airside without schedule", new[] { K.World, K.Flow, K.Airside }, new[] { "world.fixture", "flow.fixture", "airside.fixture", "airside_rules.json" }, null),

                // 16 §16.3: a listed system whose file is missing is a hard load
                // failure naming the file, and §19.2c's stderr names the file.
                ("no turnaround.fixture", NonAirsideSystems, new[] { "world.fixture", "schedule.csv", "flow.fixture" }, "turnaround.fixture"),
                ("no airside.fixture", all, Without(allFiles, "airside.fixture"), "airside.fixture"),
                ("no airside_rules.json", all, Without(allFiles, "airside_rules.json"), "airside_rules.json"),
            };

            foreach ((string label, string[] systems, string[] files, string? named) in cases)
            {
                using var tmp = new CheckpointsKit.TempDir();
                string bundle = tmp.File("bundle");
                K.WriteBundle(bundle, systems, files);
                string outPath = tmp.File("a");
                CliResult r = Cli(K.Args(bundle, outPath));
                Assert.True(r.Exit == 3, label + ": exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.True(r.Stdout.Length == 0, label + ": stdout '" + r.Stdout + "'");
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), label + ": stderr empty");
                Assert.False(File.Exists(outPath), label + ": created " + outPath);
                if (named != null)
                {
                    Assert.True(r.Stderr.Contains(named, StringComparison.Ordinal), label + ": stderr does not name " + named + ": " + r.Stderr);
                }
            }
        }

        private static string[] Without(string[] names, string removed)
        {
            return Array.FindAll(names, n => !string.Equals(n, removed, StringComparison.Ordinal));
        }
    }
}
