using System;
using System.Collections.Generic;
using System.IO;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. IHeadlessRun (16 §16.8): it composes the bundle, submits no
    /// command (Q-071), calls Step(TICKS_PER_SIM_DAY) exactly Days times, and
    /// writes the dump. It has no seam: the tests compose the same bundle with
    /// ISimComposer.Compose themselves, step it, render the dump with their own
    /// code and compare it with Run's file.
    /// </summary>
    public sealed class HeadlessRunTests
    {
        [Fact]
        public void test_headless_run_result_independent_of_step_batch_size()
        {
            using var tmp = new TempDir();
            byte[] run = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp);

            uint day = B.Day;
            var batchings = new List<(string Label, Action<ISimHost> Step)>
            {
                ("14 400 × Step(1)", h => { for (int i = 0; i < 14400; i++) { h.Step(1); } }),
                ("14 × Step(997), then Step(442)", h => { for (int i = 0; i < 14; i++) { h.Step(997); } h.Step(442); }),
                ("one Step(14 400)", h => h.Step(day)),
            };

            Assert.Equal(14400u, day);
            foreach ((string label, Action<ISimHost> step) in batchings)
            {
                var sink = new RecordingSink();
                ComposedSim sim = ComposeTests.Compose(B.Phase1Bundle(), B.Phase1Content, sink);
                step(sim.Host);
                Assert.Equal((ulong)day, sim.Host.CurrentTick);
                Dumps.AssertBytesEqual(Dumps.Render(B.Seed, ComposeTests.Names(sim), sink), run, "Run's file against " + label);
            }
        }

        [Fact]
        public void test_headless_run_steps_days_times_ticks_per_sim_day()
        {
            // Run steps to Days × TICKS_PER_SIM_DAY, so two days record 48
            // checkpoints, and they equal the reference composition's two days.
            using var tmp = new TempDir();
            byte[] dump = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 2, tmp);

            KitRun kit = Kit.Compose(B.Phase1Bundle(), B.Phase1Content, B.Seed, B.RegistryOrder);
            kit.Host.Step(B.Day);
            kit.Host.Step(B.Day);
            Assert.Equal(2UL * SimConstants.TICKS_PER_SIM_DAY, kit.Host.CurrentTick);
            Dumps.AssertBytesEqual(kit.Dump(B.Seed), dump, "Run's two-day dump");
            CheckpointDumpTests.AssertShape(dump, "systems sim.world sim.schedule sim.airside sim.flow sim.turnaround sim.delay", 6, 48);
        }

        /// <summary>16 §16.8: every character outside U+0020 to U+007E is written as '?'.</summary>
        private static string Ascii(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] < ' ' || chars[i] > '~')
                {
                    chars[i] = '?';
                }
            }

            return new string(chars);
        }

        [Fact]
        public void test_headless_run_failure_returns_3_and_writes_no_file()
        {
            // 16 §16.8 "Run's stages and failures" (Q-120): every failure is the
            // return value 3, never an exception; stages 1 to 3 leave no file, an
            // existing OutputPath is never overwritten, and a 3 writes exactly
            // its one failure line to Console.Error (§16.11: output-exists,
            // output-no-parent, load). Success returns 0 and writes nothing.
            using var tmp = new TempDir();
            IHeadlessRun run = HostFactory.CreateHeadlessRun(HostFactory.CreateSimComposer(B.Content(B.Phase1Content)));
            TextWriter oldOut = Console.Out;
            TextWriter oldErr = Console.Error;
            try
            {
                // Control: the same run with a fresh path succeeds, silently.
                var outText = new StringWriter();
                var errText = new StringWriter();
                Console.SetOut(outText);
                Console.SetError(errText);
                int ok = run.Run(B.Phase1Bundle(), new CheckpointRunRequest(1, tmp.File("ok")));
                Assert.Equal(0, ok);
                Assert.True(File.Exists(tmp.File("ok")), "the control run wrote no dump");
                Assert.True(outText.ToString().Length == 0 && errText.ToString().Length == 0, "a successful Run wrote to the console");

                byte[] existing = B.Utf8("not a dump\n");
                File.WriteAllBytes(tmp.File("existing"), existing);
                Directory.CreateDirectory(tmp.File("dir"));
                // §16.8 "The failure line": stage 1 lines are exact; a load line is
                // "load <type>: <message>", the message starting with the bundle
                // file at fault (§16.4 "Load failures"), so only that prefix is pinned.
                string existingPath = tmp.File("existing");
                string dirPath = tmp.File("dir");
                string noParent = Path.Combine(tmp.File("missing"), "dump");
                var cases = new List<(string Label, MemoryBundle Bundle, string Path, string? NewFile, string Line, bool Exact)>
                {
                    ("an existing OutputPath file", B.Phase1Bundle(), existingPath, null, "FAIL checkpoints output-exists " + Ascii(existingPath), true),
                    ("an existing OutputPath directory", B.Phase1Bundle(), dirPath, null, "FAIL checkpoints output-exists " + Ascii(dirPath), true),
                    ("a missing parent directory", B.Phase1Bundle(), noParent, noParent, "FAIL checkpoints output-no-parent " + Ascii(noParent), true),
                    ("a bundle with a load failure (no schedule.csv)", B.Phase1Bundle().Remove("schedule.csv"), tmp.File("load"), tmp.File("load"), "FAIL checkpoints load FormatException: schedule.csv: ", false),
                    ("a bundle with a load failure (bad bundle.json)", B.Phase1Bundle().Put("bundle.json", B.Utf8("{")), tmp.File("json"), tmp.File("json"), "FAIL checkpoints load FormatException: bundle.json: ", false),
                };

                foreach ((string label, MemoryBundle bundle, string path, string? newFile, string expected, bool exact) in cases)
                {
                    outText = new StringWriter();
                    errText = new StringWriter();
                    Console.SetOut(outText);
                    Console.SetError(errText);
                    int code = -1;
                    Exception? e = Record.Exception(() => code = run.Run(bundle, new CheckpointRunRequest(1, path)));
                    Assert.True(e == null, label + ": Run threw " + e);
                    Assert.True(code == 3, label + ": Run returned " + code);

                    // Exactly one Console.Error.WriteLine(line) and nothing else on the console.
                    string err = errText.ToString();
                    Assert.True(outText.ToString().Length == 0, label + ": Run wrote to Console.Out: '" + outText + "'");
                    Assert.True(err.EndsWith(errText.NewLine, StringComparison.Ordinal), label + ": Console.Error is not one WriteLine: '" + err + "'");
                    string line = err.Substring(0, err.Length - errText.NewLine.Length);
                    Assert.True(Ascii(line) == line, label + ": the line is not plain printable ASCII (one line, §16.8): '" + err + "'");
                    if (exact)
                    {
                        Assert.Equal(expected, line);
                    }
                    else
                    {
                        Assert.True(line.StartsWith(expected, StringComparison.Ordinal), label + ": expected a line starting '" + expected + "', got '" + line + "'");
                    }

                    if (newFile != null)
                    {
                        Assert.False(File.Exists(newFile) || Directory.Exists(newFile), label + ": created " + newFile);
                    }
                }

                Assert.Equal(existing, File.ReadAllBytes(tmp.File("existing")));
                Assert.Empty(Directory.GetFileSystemEntries(tmp.File("dir")));
                Assert.False(Directory.Exists(tmp.File("missing")), "Run created the missing parent directory");
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldErr);
            }
        }

        [Fact]
        public void test_headless_run_same_bundle_gives_identical_files()
        {
            // 16 §16.4: a session's checkpoints are a pure function of the bundle
            // and the content, so two runs of one bundle give identical files.
            using var tmp = new TempDir();
            byte[] a = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp, "a");
            byte[] b = CheckpointDumpTests.RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp, "b");
            Dumps.AssertBytesEqual(a, b, "the second run");
        }
    }
}
