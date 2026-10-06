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

        [Fact]
        public void test_headless_run_failure_returns_3_and_writes_no_file()
        {
            // 16 §16.8 "Run's stages and failures" (Q-120): every failure is the
            // return value 3, never an exception; stages 1 to 3 leave no file, an
            // existing OutputPath is never overwritten, and a 3 writes exactly
            // one "FAIL checkpoints " line to Console.Error. Success returns 0
            // and writes nothing.
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
                var cases = new List<(string Label, MemoryBundle Bundle, string Path, string? NewFile)>
                {
                    ("an existing OutputPath file", B.Phase1Bundle(), tmp.File("existing"), null),
                    ("an existing OutputPath directory", B.Phase1Bundle(), tmp.File("dir"), null),
                    ("a missing parent directory", B.Phase1Bundle(), Path.Combine(tmp.File("missing"), "dump"), Path.Combine(tmp.File("missing"), "dump")),
                    ("a bundle with a load failure (no schedule.csv)", B.Phase1Bundle().Remove("schedule.csv"), tmp.File("load"), tmp.File("load")),
                    ("a bundle with a load failure (bad bundle.json)", B.Phase1Bundle().Put("bundle.json", B.Utf8("{")), tmp.File("json"), tmp.File("json")),
                };

                foreach ((string label, MemoryBundle bundle, string path, string? newFile) in cases)
                {
                    errText = new StringWriter();
                    Console.SetError(errText);
                    int code = -1;
                    Exception? e = Record.Exception(() => code = run.Run(bundle, new CheckpointRunRequest(1, path)));
                    Assert.True(e == null, label + ": Run threw " + e);
                    Assert.True(code == 3, label + ": Run returned " + code);
                    // Exactly one line, starting "FAIL checkpoints " (Q-120); its
                    // stage words and detail are not asserted.
                    string err = errText.ToString();
                    string line = err.EndsWith("\r\n", StringComparison.Ordinal) ? err.Substring(0, err.Length - 2)
                        : err.EndsWith("\n", StringComparison.Ordinal) ? err.Substring(0, err.Length - 1) : err;
                    Assert.True(line.Length < err.Length && line.IndexOf('\n') < 0 && line.IndexOf('\r') < 0, label + ": Console.Error is not exactly one line: '" + err + "'");
                    Assert.True(line.StartsWith("FAIL checkpoints ", StringComparison.Ordinal), label + ": the Console.Error line does not start with 'FAIL checkpoints ': '" + err + "'");
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
