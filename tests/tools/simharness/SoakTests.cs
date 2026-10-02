using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-013. The harness `soak` subcommand, in process through HarnessCli.Run,
    /// against 19 §19.2b, §19.3 and §19.7 (Q-057), 16 §16.8's dump format and
    /// 03 "The soak fixture". Every soak path is fully qualified, in a fresh
    /// temporary directory that the test deletes, except one repository-relative
    /// --golden that only reads a fixture. No test writes a repository file, and
    /// none runs 500 days. Every test makes at least one CLI run that must
    /// succeed, so none passes against a harness without `soak` (an unknown
    /// subcommand is exit 2, §19.3) or against one that returns a fixed code.
    /// </summary>
    public sealed class SoakTests
    {
        private const int HeaderLines = 3;

        /// <summary>Checkpoints in one sim-day: ticks 0, 600, ..., 13 800 (08 §8.9).</summary>
        private const int DayCheckpoints = 24;

        private static readonly Regex WroteDay = new Regex("^WROTE soak ticks=14400 checkpoints=24 final=([0-9a-f]{16})\n$");

        /// <summary>A checkpoint line of the soak composition: tick, world hash, four system hashes (16 §16.8).</summary>
        private static readonly Regex CheckpointLine = new Regex("^(0|[1-9][0-9]*)( [0-9a-f]{16}){5}$");

        private readonly ITestOutputHelper _output;

        public SoakTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string[] SoakOut(string days, string path)
        {
            return new[] { "soak", "--days", days, "--out", path };
        }

        private static string[] SoakGolden(string days, string path)
        {
            return new[] { "soak", "--days", days, "--golden", path };
        }

        /// <summary>`soak --days 1 --out <paramref name="path"/>`, which must write its dump. Returns its final hash.</summary>
        private static string WriteDay(string path)
        {
            CliResult r = Cli(SoakOut("1", path));
            Assert.True(r.Exit == 0, "soak --days 1 --out: exit " + r.Exit + ", stderr: " + r.Stderr);
            Match m = WroteDay.Match(r.Stdout);
            Assert.True(m.Success, "soak --days 1 --out printed '" + r.Stdout + "'");
            Assert.True(File.Exists(path), "soak --days 1 --out exited 0 but wrote no " + path);
            return m.Groups[1].Value;
        }

        private static string[] DumpLines(byte[] dump)
        {
            Assert.False(dump.Length >= 3 && dump[0] == 0xEF && dump[1] == 0xBB && dump[2] == 0xBF, "dump starts with a BOM");
            string text = Encoding.UTF8.GetString(dump);
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("\n", text);
            return text.Substring(0, text.Length - 1).Split('\n');
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string what)
        {
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Fail(what + " differ.\nexpected:\n" + Encoding.UTF8.GetString(expected) + "\nactual:\n" + Encoding.UTF8.GetString(actual));
            }
        }

        private static void AssertEntries(string directory, params string[] names)
        {
            string[] actual = Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
            string[] expected = names.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
        }

        // ------------------------------------------------------------ round trip

        /// <summary>
        /// §19.7 "Round trip". `--out` writes a 16 §16.8 dump of one sim-day:
        /// the version line, `seed 12345`, a systems line with the four registered
        /// systems in registry order (§19.2a), and 24 checkpoint lines. `--golden`
        /// on that file passes with the same counts and final hash, and reads the
        /// golden without changing it.
        /// </summary>
        [Fact]
        public void test_soak_out_then_golden_round_trip_passes()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");

            string final = WriteDay(a);
            byte[] dump = File.ReadAllBytes(a);
            string[] lines = DumpLines(dump);

            Assert.Equal(HeaderLines + DayCheckpoints, lines.Length);
            Assert.Equal("airport-sim-checkpoints 1", lines[0]);
            Assert.Equal("seed 12345", lines[1]);
            string[] systems = lines[2].Split(' ');
            Assert.Equal("systems", systems[0]);
            Assert.Equal(5, systems.Length);
            Assert.All(systems, name => Assert.NotEmpty(name));

            for (int i = 0; i < DayCheckpoints; i++)
            {
                string line = lines[HeaderLines + i];
                Assert.Matches(CheckpointLine, line);
                string[] cols = line.Split(' ');
                Assert.Equal((i * 600).ToString(System.Globalization.CultureInfo.InvariantCulture), cols[0]);

                // §19.2a: the boarding stand-in, system 3, hashes 0.
                Assert.Equal("0000000000000000", cols[4]);
            }

            CliResult r = Cli(SoakGolden("1", a));
            Assert.True(r.Exit == 0, "soak --golden on its own dump: exit " + r.Exit + ", stdout '" + r.Stdout + "', stderr: " + r.Stderr);
            Assert.Equal("PASS soak ticks=14400 checkpoints=24 final=" + final + "\n", r.Stdout);
            Assert.Equal(dump, File.ReadAllBytes(a));
            AssertEntries(tmp.Path, "a");
        }

        // ------------------------------------------------------------ equivalence

        /// <summary>
        /// §19.7 "Equivalence": the `--out` run's final equals HarnessGates.FinalHash
        /// of the soak kit, seed 12345, 14 400 ticks, which ties the soak kit to the
        /// CLI. The dump itself equals the kit's own one-day dump (§19.2b "The run"
        /// and "The dump"), built by the test with the same host, composer, NoOp
        /// script and 16 §16.8 rendering, on every line, except for the stand-in's
        /// Name in the systems line. That Name is whatever T-009 merged (§19.2a),
        /// which the spec does not fix, so only its position is checked.
        /// </summary>
        [Fact]
        public void test_soak_dump_matches_soak_kit_and_final_hash_gate()
        {
            string gate = SoakKit.FinalHash(TicksPerDay);
            SoakKit.KitDump kit = SoakKit.RunDump(1);
            Assert.Equal(DayCheckpoints, kit.Checkpoints);
            Assert.Equal(gate, Hex16(kit.Final));

            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            string final = WriteDay(a);
            Assert.Equal(gate, final);

            string[] lines = DumpLines(File.ReadAllBytes(a));
            Assert.Equal(kit.Lines.Length, lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 2)
                {
                    string[] expected = kit.Lines[2].Split(' ');
                    string[] actual = lines[2].Split(' ');
                    Assert.Equal(expected.Length, actual.Length);
                    for (int j = 0; j < expected.Length; j++)
                    {
                        if (j != 3)
                        {
                            Assert.Equal(expected[j], actual[j]);
                        }
                    }

                    continue;
                }

                Assert.True(kit.Lines[i] == lines[i], "line " + (i + 1) + ": expected '" + kit.Lines[i] + "', got '" + lines[i] + "'");
            }
        }

        // ------------------------------------------------------------ reproducible

        /// <summary>
        /// §19.7 "Reproducible": two `--out` runs of one day, to two files, are
        /// byte-identical. The second gives its flags in the other order, which
        /// §19.3 allows.
        /// </summary>
        [Fact]
        public void test_soak_two_out_runs_write_byte_identical_dumps()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            string b = tmp.File("b");

            string finalA = WriteDay(a);

            CliResult r = Cli("soak", "--out", b, "--days", "1");
            Assert.True(r.Exit == 0, "soak --out b --days 1: exit " + r.Exit + ", stderr: " + r.Stderr);
            Assert.Equal("WROTE soak ticks=14400 checkpoints=24 final=" + finalA + "\n", r.Stdout);

            AssertBytesEqual(File.ReadAllBytes(a), File.ReadAllBytes(b), "the two dumps");
            AssertEntries(tmp.Path, "a", "b");
        }

        // ------------------------------------------------------------ differs

        /// <summary>
        /// §19.7 "Differs", with §19.2b's rule: `L` is 1 plus the LF bytes of the
        /// run's dump `R` before the first differing offset `o`, or before the
        /// shorter length when one is a prefix of the other.
        /// <list type="bullet">
        /// <item>One hex digit changed on line 14, a checkpoint line: `o` is on
        /// line 14, so `L` = 14.</item>
        /// <item>The golden cut to its first 20 lines, line 20's LF kept: it is a
        /// prefix of `R`, `o` is its length, and `R` has 20 LFs before it, so
        /// `L` = 21.</item>
        /// <item>The golden with one extra line appended: `R` is a prefix of it,
        /// `o` is `R`'s length, and `R` has all its 27 LFs before it, so
        /// `L` = 28.</item>
        /// </list>
        /// Each exits 1 with exactly that line on stdout, and leaves the golden
        /// unchanged.
        /// </summary>
        [Fact]
        public void test_soak_golden_differing_reports_first_differing_line()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            WriteDay(a);
            byte[] dump = File.ReadAllBytes(a);
            string[] lines = DumpLines(dump);
            Assert.Equal(HeaderLines + DayCheckpoints, lines.Length);

            // Line 14, its last character: the last system hash's last hex digit.
            var changed = (string[])lines.Clone();
            string line14 = changed[13];
            char last = line14[line14.Length - 1];
            changed[13] = line14.Substring(0, line14.Length - 1) + (last == '0' ? '1' : '0');
            AssertDiffers(tmp, "changed", Encoding.UTF8.GetBytes(string.Join("\n", changed) + "\n"), 14);

            byte[] cut = Encoding.UTF8.GetBytes(string.Join("\n", lines.Take(20)) + "\n");
            Assert.True(dump.AsSpan().StartsWith(cut), "the cut golden is not a prefix of the dump");
            AssertDiffers(tmp, "cut", cut, 21);

            byte[] extended = dump.Concat(Encoding.UTF8.GetBytes("999999 0000000000000000\n")).ToArray();
            AssertDiffers(tmp, "extended", extended, 28);
        }

        private static void AssertDiffers(CheckpointsKit.TempDir tmp, string name, byte[] golden, int line)
        {
            string path = tmp.File(name);
            File.WriteAllBytes(path, golden);
            CliResult r = Cli(SoakGolden("1", path));
            Assert.True(r.Exit == 1, name + " golden: exit " + r.Exit + ", stdout '" + r.Stdout + "', stderr: " + r.Stderr);
            Assert.Equal("FAIL soak line=" + line + "\n", r.Stdout);
            Assert.Equal(golden, File.ReadAllBytes(path));
        }

        // ------------------------------------------------------------ paths

        /// <summary>
        /// §19.2b "Paths": a `P` that is not fully qualified is repository-relative,
        /// joined to the AirportSim.sln root, never to the working directory. The
        /// golden named here is the soak schedule fixture, which is readable and is
        /// not a dump. It differs at offset 0 (`f` against `a`), so `L` = 1 and the
        /// exit is 1. A harness that resolved it against the working directory, the
        /// test's bin directory, would not find it, which is exit 3.
        /// </summary>
        [Fact]
        public void test_soak_relative_golden_resolves_from_repository_root()
        {
            byte[] before = KillGateKit.Fixture(SoakKit.SchedulePath);
            Assert.Equal((byte)'f', before[0]);

            CliResult r = Cli(SoakGolden("1", SoakKit.SchedulePath));
            Assert.True(r.Exit == 1, "relative golden: exit " + r.Exit + ", stdout '" + r.Stdout + "', stderr: " + r.Stderr);
            Assert.Equal("FAIL soak line=1\n", r.Stdout);
            Assert.Equal(before, KillGateKit.Fixture(SoakKit.SchedulePath));
        }

        /// <summary>
        /// §19.7 "Path failures" and §19.2b: a `--golden` that does not exist or
        /// cannot be read, an `--out` that already exists, and an `--out` whose
        /// parent directory does not exist are exit 3 with stdout empty. The
        /// existing file is left unchanged, and no file is created. The control
        /// write comes first, so each 3 comes from the case, not from a `soak` that
        /// always fails.
        /// </summary>
        [Fact]
        public void test_soak_path_failures_exit_3()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            WriteDay(a);
            byte[] before = File.ReadAllBytes(a);

            var cases = new List<(string Label, string[] Args)>
            {
                ("missing --golden", SoakGolden("1", tmp.File("missing"))),
                ("directory --golden", SoakGolden("1", tmp.Path)),
                ("existing --out", SoakOut("1", a)),
                ("parentless --out", SoakOut("1", Path.Combine(tmp.Path, "no-dir", "b"))),
            };

            foreach ((string label, string[] args) in cases)
            {
                CliResult r = Cli(args);
                Assert.True(r.Exit == 3, label + ": exit " + r.Exit + ", stdout '" + r.Stdout + "', stderr: " + r.Stderr);
                Assert.True(r.Stdout.Length == 0, label + ": stdout '" + r.Stdout + "'");
                Assert.False(string.IsNullOrWhiteSpace(r.Stderr), label + ": stderr empty");
                Assert.Equal(before, File.ReadAllBytes(a));
                AssertEntries(tmp.Path, "a");
            }
        }

        // ------------------------------------------------------------ usage

        /// <summary>
        /// §19.7 "Usage" and §19.3's usage errors, each exit 2 with stdout empty
        /// and no file created. The control write comes first, and its dump is the
        /// `--golden` of the cases, so a harness that ignored the bad part and ran
        /// would exit 0 there instead.
        /// </summary>
        [Fact]
        public void test_soak_rejects_usage_errors()
        {
            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            WriteDay(a);
            byte[] before = File.ReadAllBytes(a);
            string b = tmp.File("b");

            var cases = new List<(string Label, string[] Args)>
            {
                ("--days only", new[] { "soak", "--days", "1" }),
                ("--golden and --out", new[] { "soak", "--days", "1", "--golden", a, "--out", b }),
                ("--golden empty", SoakGolden("1", "")),
                ("--out empty", SoakOut("1", "")),
                ("--seed added", new[] { "soak", "--days", "1", "--seed", "1", "--golden", a }),
                ("--days missing", new[] { "soak", "--golden", a }),
                ("--days 0", SoakOut("0", b)),
                ("--days 298262", SoakOut("298262", b)),
                ("--days +1", SoakGolden("+1", a)),
                ("--days x", SoakGolden("x", a)),
                ("--days twice", new[] { "soak", "--days", "1", "--days", "1", "--golden", a }),
                ("--golden twice", new[] { "soak", "--days", "1", "--golden", a, "--golden", a }),
                ("unknown flag", new[] { "soak", "--days", "1", "--golden", a, "--hash-only" }),
            };

            foreach ((string label, string[] args) in cases)
            {
                CliResult r = Cli(args);
                Assert.True(r.Exit == 2, label + ": exit " + r.Exit + ", stdout '" + r.Stdout + "', stderr: " + r.Stderr);
                Assert.True(r.Stdout.Length == 0, label + ": stdout '" + r.Stdout + "'");
                Assert.Equal(before, File.ReadAllBytes(a));
                AssertEntries(tmp.Path, "a");
            }

            if (OperatingSystem.IsWindows())
            {
                // Rooted but not fully qualified (§19.2b "Paths"): a usage error.
                foreach (string rooted in new[] { "C:b", "\\b" })
                {
                    string resolved = Path.GetFullPath(rooted);
                    bool existedBefore = File.Exists(resolved);
                    CliResult r = Cli(SoakOut("1", rooted));
                    Assert.True(r.Exit == 2, "--out " + rooted + ": exit " + r.Exit + ", stderr: " + r.Stderr);
                    Assert.Equal(string.Empty, r.Stdout);
                    if (!existedBefore)
                    {
                        Assert.False(File.Exists(resolved), "--out " + rooted + " created " + resolved);
                    }
                }
            }
        }

        // ------------------------------------------------------------ sizing

        /// <summary>Sim-days of the sizing run. Over 10, so the test is Slow by 07 L11a rule (a).</summary>
        private const uint SizingDays = 11;

        /// <summary>
        /// §19.7 "Sizing", 03 "The soak fixture". One run of the soak kit through
        /// HarnessGates.FinalHash, <see cref="SizingDays"/> sim-days, seed 12345,
        /// takes in total less than K × 14 400 × 100 µs, a mean under 0.1 ms per
        /// tick. K × 14 400 × 100 µs is 1.44 × K s, so with elapsed `e` in
        /// timestamp units at frequency `f` the condition e / f &lt; 144 K / 100 is
        /// checked exactly in long arithmetic (07 L11) as 100 e &lt; 144 K f. It
        /// is a whole-run gate, not 03's per-tick statistic.
        ///
        /// <para><b>The sizing derivation.</b> The bound is on cost, and the counts
        /// are fixture sizing (03). The Phase 0 composition, 200 movements and
        /// 20 456 departing passengers a day, took 36 s for its 100-day kill gate in
        /// the slow-tests CI run of main at 2f37b54: about 25 µs per tick, with
        /// the CI job's other tests running alongside. Its cost is O(nodes +
        /// cohorts) per tick (09 §9.10), and cohorts scale with the departures in
        /// flight. The soak set has 1.5 times the movements and 1.57 times the
        /// passengers, so about 40 µs per tick is expected in CI, under half the
        /// 100 µs ceiling. An indicative local Release run of this test measured
        /// 27 µs per tick. The rest is headroom for the systems that later join the soak
        /// (03: every built system is registered), after which, if the mean
        /// passes 0.1 ms, the fixture is shrunk and the gate is not weakened.</para>
        ///
        /// <para><b>Queues drain between banks.</b> security_soak serves 15 a
        /// server-minute, and two servers are open on each of the two lanes: 60 a
        /// minute together. Spread each departure's passengers over its profile's
        /// show-up buckets (business 30 to 120 min, leisure 45 to 180 min before
        /// STD) and serve 60 a minute: the combined backlog peaks at about 70, the
        /// busiest hour brings about 2 950 against 3 600 served, and every bank's
        /// backlog is gone well before the next one opens. Even if one lane took
        /// every passenger at 30 a minute, the backlog would peak near 1 920 and
        /// still be empty by midnight, and capacity_standing 2 500 is above that,
        /// so nothing spills back. So the queues drain between banks on every day,
        /// and at each midnight nothing is left (<see cref="SoakKit.CohortCeiling"/>).</para>
        ///
        /// <para>The load check then holds after the timed run. A `soak --days K`
        /// CLI run follows, and its final must equal the timed run's: this ties the
        /// sized composition to the one the gate runs, across day boundaries, and
        /// makes the test fail without the subcommand.</para>
        /// </summary>
        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")]
        public void test_soak_fixture_sized_under_a_tenth_of_a_millisecond_per_tick()
        {
            const uint ticks = SizingDays * TicksPerDay;
            var content = SoakKit.Content();
            var composer = new SoakKit.SoakComposer();

            long start = Stopwatch.GetTimestamp();
            string final = HarnessGates.FinalHash(content, composer.Compose, SoakKit.Seed, ticks);
            long elapsed = Stopwatch.GetTimestamp() - start;

            _output.WriteLine(SizingDays + " soak days: " + elapsed + " timestamp units at " + Stopwatch.Frequency + " Hz, "
                + (elapsed * 1000000 / Stopwatch.Frequency / ticks) + " us per tick (floored)");
            Assert.True(100L * elapsed < 144L * SizingDays * Stopwatch.Frequency,
                SizingDays + " soak days took " + elapsed + " timestamp units at " + Stopwatch.Frequency
                + " Hz, not under " + SizingDays + " x 14 400 x 100 us");

            Assert.Equal(1, composer.Calls);
            Assert.Matches("^[0-9a-f]{16}$", final);
            SoakKit.AssertRunCarriedLoad(composer, SizingDays);

            using var tmp = new CheckpointsKit.TempDir();
            string a = tmp.File("a");
            CliResult r = Cli(SoakOut(SizingDays.ToString(System.Globalization.CultureInfo.InvariantCulture), a));
            Assert.True(r.Exit == 0, "soak --days " + SizingDays + " --out: exit " + r.Exit + ", stderr: " + r.Stderr);
            uint checkpoints = CheckpointCount(ticks);
            Assert.Equal("WROTE soak ticks=" + ticks + " checkpoints=" + checkpoints + " final=" + final + "\n", r.Stdout);
            Assert.Equal(HeaderLines + (int)checkpoints, DumpLines(File.ReadAllBytes(a)).Length);
        }
    }
}
