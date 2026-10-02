using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-030. The harness `checkpoints` subcommand, in process through
    /// HarnessCli.Run, against 19 §19.2c, §19.3 and §19.8 (Q-066 to Q-076) and
    /// 16 §16.8's dump format. Every --out path is fully qualified, in a fresh
    /// temporary directory that the test deletes. No test writes a repository file.
    /// </summary>
    public sealed class CheckpointsTests
    {
        private static readonly Regex WroteLine = new Regex("^WROTE checkpoints ticks=14400 checkpoints=24 final=[0-9a-f]{16}\n$");

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(what + " differs from the expected dump.\nexpected:\n"
                    + Encoding.UTF8.GetString(expected) + "\nactual:\n" + Encoding.UTF8.GetString(actual));
            }
        }

        private static void AssertNoFiles(string directory)
        {
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }

        // ------------------------------------------------------------ format

        [Fact]
        [Trait("Category", "Slow")]
        public void test_checkpoint_dump_format_is_byte_exact()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string outPath = tmp.File("a");

            CliResult r = Cli(CheckpointsKit.DefaultArgs(outPath));
            Assert.Equal(0, r.Exit);

            CheckpointsKit.KitRun kit = CheckpointsKit.RunOneStep();
            Assert.Equal(24, kit.Checkpoints);
            Assert.Equal("WROTE checkpoints ticks=14400 checkpoints=24 final=" + Hex16(kit.Final) + "\n", r.Stdout);

            byte[] dump = File.ReadAllBytes(outPath);
            AssertBytesEqual(kit.Dump, dump, "<tmp>/a");

            // 16 §16.8 / §19.8: the header lines, literally. 08 §8.5 / 16 §16.3: each
            // Phase 1 system's Name is its module name, in registry order.
            string text = Encoding.UTF8.GetString(dump);
            Assert.StartsWith(
                "airport-sim-checkpoints 1\nseed 12345\nsystems sim.world sim.schedule sim.flow\n",
                text,
                StringComparison.Ordinal);

            // The rendered expectation carries the format rules: no BOM, LF only,
            // a final newline, and 3 header lines plus one line per checkpoint.
            Assert.False(dump.Length >= 3 && dump[0] == 0xEF && dump[1] == 0xBB && dump[2] == 0xBF, "dump starts with a BOM");
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("\n", text);
            string[] lines = text.Substring(0, text.Length - 1).Split('\n');
            Assert.Equal(3 + 24, lines.Length);
            var checkpointLine = new Regex("^(0|[1-9][0-9]*)( [0-9a-f]{16}){4}$");
            for (int i = 3; i < lines.Length; i++)
            {
                Assert.Matches(checkpointLine, lines[i]);
                Assert.StartsWith(((i - 3) * 600).ToString(System.Globalization.CultureInfo.InvariantCulture) + " ", lines[i], StringComparison.Ordinal);
            }
        }

        // ------------------------------------------------------------ batching

        [Fact]
        [Trait("Category", "Slow")]
        public void test_checkpoints_result_independent_of_step_batch_size()
        {
            // Q-074: the CLI's batching is pinned to Step(TICKS_PER_SIM_DAY) per day,
            // and 08 §8.2 "Chunking is invisible" makes every other batching equal.
            CheckpointsKit.KitRun ones = CheckpointsKit.Run(host =>
            {
                for (uint i = 0; i < TicksPerDay; i++)
                {
                    host.Step(1);
                }
            });
            CheckpointsKit.KitRun whole = CheckpointsKit.Run(host => host.Step(14400));
            CheckpointsKit.KitRun mixed = CheckpointsKit.Run(host =>
            {
                for (int i = 0; i < 14; i++)
                {
                    host.Step(997);
                }

                host.Step(442);
            });

            AssertBytesEqual(whole.Dump, ones.Dump, "Step(1) x 14400");
            AssertBytesEqual(whole.Dump, mixed.Dump, "Step(997) x 14 then Step(442)");
            Assert.Equal(whole.Final, ones.Final);
            Assert.Equal(whole.Final, mixed.Final);

            using var tmp = new CheckpointsKit.TempDir();
            string outPath = tmp.File("a");
            CliResult r = Cli(CheckpointsKit.DefaultArgs(outPath));
            Assert.Equal(0, r.Exit);
            byte[] cli = File.ReadAllBytes(outPath);
            AssertBytesEqual(ones.Dump, cli, "CLI dump vs Step(1) x 14400");
            AssertBytesEqual(whole.Dump, cli, "CLI dump vs Step(14400)");
            AssertBytesEqual(mixed.Dump, cli, "CLI dump vs Step(997) x 14 then Step(442)");
        }

        // ------------------------------------------------------------ composition

        [Fact]
        public void test_checkpoints_subcommand_composes_through_published_factories_only()
        {
            // Behavioural (Q-075): the CLI's dump equals the kit's, which is built
            // through the published surface only.
            using (var tmp = new CheckpointsKit.TempDir())
            {
                string outPath = tmp.File("a");
                CliResult r = Cli(CheckpointsKit.DefaultArgs(outPath));
                Assert.Equal(0, r.Exit);
                AssertBytesEqual(CheckpointsKit.RunOneStep().Dump, File.ReadAllBytes(outPath), "<tmp>/a");
            }

            // Static: the harness references no AirportSim.App.* assembly, so D7
            // compares two independent compositions (16 §16.4), and no AirportSim.*
            // assembly it references carries InternalsVisibleTo (07 L5).
            Assembly harness = typeof(HarnessCli).Assembly;
            Assert.Equal("AirportSim.Tools.SimHarness", harness.GetName().Name);
            var checkedNames = new List<string>();
            foreach (AssemblyName reference in harness.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                Assert.False(
                    name.StartsWith("AirportSim.App", StringComparison.Ordinal),
                    "the harness references " + name);
                if (!name.StartsWith("AirportSim.", StringComparison.Ordinal))
                {
                    continue;
                }

                Assembly referenced = Assembly.Load(reference);
                Assert.Empty(referenced.GetCustomAttributes<InternalsVisibleToAttribute>());
                checkedNames.Add(name);
            }

            // The harness composes sim.core, sim.world, sim.schedule and sim.flow
            // (T-009), so the check covers at least sim.core.
            Assert.Contains("AirportSim.Sim.Core", checkedNames);
        }

        // ------------------------------------------------------------ usage errors

        [Fact]
        public void test_checkpoints_rejects_usage_errors()
        {
            // Control: the default invocation is recognised and writes its dump. An
            // unknown subcommand is also exit 2 (§19.3), so this must hold first.
            using (var control = new CheckpointsKit.TempDir())
            {
                CliResult ok = Cli(CheckpointsKit.DefaultArgs(control.File("a")));
                Assert.Equal(0, ok.Exit);
                Assert.Matches(WroteLine, ok.Stdout);
                Assert.True(File.Exists(control.File("a")), "control wrote no dump");
            }

            const string bundle = CheckpointsKit.BundleDirectory;
            const string content = CheckpointsKit.ContentDirectory;
            var cases = new List<(string Label, Func<string, string[]> Args)>
            {
                ("--bundle missing", p => new[] { "checkpoints", "--content", content, "--days", "1", "--out", p }),
                ("--content missing", p => new[] { "checkpoints", "--bundle", bundle, "--days", "1", "--out", p }),
                ("--days missing", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--out", p }),
                ("--out missing", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--days", "1" }),
                ("--days twice", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--days", "1", "--days", "1", "--out", p }),
                ("--days 0", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--days", "0", "--out", p }),
                ("--days 298262", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--days", "298262", "--out", p }),
                ("--seed 1 added", p => new[] { "checkpoints", "--bundle", bundle, "--content", content, "--days", "1", "--seed", "1", "--out", p }),
                ("--bundle empty", p => new[] { "checkpoints", "--bundle", "", "--content", content, "--days", "1", "--out", p }),
            };

            foreach ((string label, Func<string, string[]> args) in cases)
            {
                using var tmp = new CheckpointsKit.TempDir();
                CliResult r = Cli(args(tmp.File("a")));
                Assert.True(r.Exit == 2, label + ": exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.True(r.Stdout.Length == 0, label + ": stdout '" + r.Stdout + "'");
                AssertNoFiles(tmp.Path);
            }

            if (OperatingSystem.IsWindows())
            {
                // Rooted but not fully qualified (§19.2b "Paths"): a usage error.
                string resolved = Path.GetFullPath("C:a");
                bool existedBefore = File.Exists(resolved);
                CliResult r = Cli(CheckpointsKit.DefaultArgs("C:a"));
                Assert.Equal(2, r.Exit);
                Assert.Equal(string.Empty, r.Stdout);
                if (!existedBefore)
                {
                    Assert.False(File.Exists(resolved), "--out C:a created " + resolved);
                }
            }
        }

        // ------------------------------------------------------------ harness failures

        [Fact]
        public void test_checkpoints_harness_failures_exit_3()
        {
            // Control: the default invocation writes its dump, so each 3 that follows
            // comes from the failure case, not from a subcommand that always fails.
            using (var control = new CheckpointsKit.TempDir())
            {
                CliResult ok = Cli(CheckpointsKit.DefaultArgs(control.File("a")));
                Assert.Equal(0, ok.Exit);
                Assert.Matches(WroteLine, ok.Stdout);
                Assert.True(File.Exists(control.File("a")), "control wrote no dump");
            }

            const string bundle = CheckpointsKit.BundleDirectory;
            const string content = CheckpointsKit.ContentDirectory;

            // An --out path that already exists is left byte-unchanged.
            using (var tmp = new CheckpointsKit.TempDir())
            {
                string outPath = tmp.File("a");
                byte[] before = Encoding.UTF8.GetBytes("not a dump\n");
                File.WriteAllBytes(outPath, before);
                CliResult r = Cli(CheckpointsKit.DefaultArgs(outPath));
                Assert.True(r.Exit == 3, "existing --out: exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.Equal(string.Empty, r.Stdout);
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), "existing --out: stderr empty");
                Assert.Equal(before, File.ReadAllBytes(outPath));
            }

            // An --out path whose parent directory does not exist.
            using (var tmp = new CheckpointsKit.TempDir())
            {
                string outPath = Path.Combine(tmp.Path, "missing", "a");
                CliResult r = Cli(CheckpointsKit.DefaultArgs(outPath));
                Assert.True(r.Exit == 3, "missing parent: exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.Equal(string.Empty, r.Stdout);
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), "missing parent: stderr empty");
                Assert.False(File.Exists(outPath), "missing parent: created " + outPath);
                AssertNoFiles(tmp.Path);
            }

            // A --bundle directory that does not exist.
            using (var tmp = new CheckpointsKit.TempDir())
            {
                string outPath = tmp.File("a");
                string noBundle = tmp.File("no-bundle");
                CliResult r = Cli("checkpoints", "--bundle", noBundle, "--content", content, "--days", "1", "--out", outPath);
                Assert.True(r.Exit == 3, "missing bundle: exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.Equal(string.Empty, r.Stdout);
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), "missing bundle: stderr empty");
                AssertNoFiles(tmp.Path);
            }

            // A bundle listing sim.schedule without its schedule.csv.
            using (var tmp = new CheckpointsKit.TempDir())
            {
                string partial = tmp.File("partial");
                Directory.CreateDirectory(partial);
                foreach (string name in new[] { "bundle.json", "world.fixture", "flow.fixture" })
                {
                    File.WriteAllBytes(Path.Combine(partial, name), KillGateKit.Fixture(bundle + "/" + name));
                }

                string outPath = tmp.File("a");
                CliResult r = Cli("checkpoints", "--bundle", partial, "--content", content, "--days", "1", "--out", outPath);
                Assert.True(r.Exit == 3, "no schedule.csv: exit " + r.Exit + ", stderr: " + r.Stderr);
                Assert.Equal(string.Empty, r.Stdout);
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), "no schedule.csv: stderr empty");
                Assert.False(File.Exists(outPath), "no schedule.csv: created " + outPath);
            }
        }
    }
}
