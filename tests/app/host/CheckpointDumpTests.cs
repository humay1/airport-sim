using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. The checkpoint dump, version 1 (16 §16.8), as IHeadlessRun writes
    /// it, compared byte for byte with the test's own rendering of the
    /// reference composition (Kit). Byte identity with a dump the test renders
    /// itself is what checks the format.
    /// </summary>
    public sealed class CheckpointDumpTests
    {
        internal static byte[] RunToFile(MemoryBundle bundle, string content, uint days, TempDir tmp, string name = "dump")
        {
            IHeadlessRun run = HostFactory.CreateHeadlessRun(HostFactory.CreateSimComposer(B.Content(content)));
            string path = tmp.File(name);
            int exit = run.Run(bundle, new CheckpointRunRequest(days, path));
            Assert.True(exit == 0, "IHeadlessRun.Run returned " + exit);
            Assert.True(File.Exists(path), "IHeadlessRun.Run wrote no dump at " + path);
            return File.ReadAllBytes(path);
        }

        /// <summary>16 §16.8: header lines, then one line per checkpoint at ticks 0, 600, ...</summary>
        internal static void AssertShape(byte[] dump, string systemsLine, int systems, int checkpoints)
        {
            Assert.True(dump.Length >= 3 && !(dump[0] == 0xEF && dump[1] == 0xBB && dump[2] == 0xBF), "the dump starts with a BOM");
            string text = new UTF8Encoding(false, true).GetString(dump);
            Assert.StartsWith("airport-sim-checkpoints 1\nseed 12345\n" + systemsLine + "\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text);
            Assert.EndsWith("\n", text);
            string[] lines = text.Substring(0, text.Length - 1).Split('\n');
            Assert.Equal(3 + checkpoints, lines.Length);
            var line = new Regex("^(0|[1-9][0-9]*)( [0-9a-f]{16}){" + (1 + systems).ToString(CultureInfo.InvariantCulture) + "}$");
            for (int i = 3; i < lines.Length; i++)
            {
                Assert.Matches(line, lines[i]);
                Assert.StartsWith(((i - 3) * 600).ToString(CultureInfo.InvariantCulture) + " ", lines[i], StringComparison.Ordinal);
            }
        }

        [Fact]
        public void test_checkpoint_dump_format_is_byte_exact()
        {
            using var tmp = new TempDir();
            byte[] dump = RunToFile(B.Phase0Bundle(), B.Phase0Content, 1, tmp);

            KitRun kit = Kit.Compose(B.Phase0Bundle(), B.Phase0Content, B.Seed, B.Phase0Systems);
            kit.Host.Step(B.Day);
            Assert.Equal(24, kit.Sink.Ticks.Count);
            Dumps.AssertBytesEqual(kit.Dump(B.Seed), dump, "IHeadlessRun's Phase 0 dump");
            AssertShape(dump, "systems sim.world sim.schedule sim.flow", 3, 24);

            // The six systems' dump has the same shape, systems in registry order.
            byte[] six = RunToFile(B.Phase1Bundle(), B.Phase1Content, 1, tmp, "six");
            AssertShape(six, "systems sim.world sim.schedule sim.airside sim.flow sim.turnaround sim.delay", 6, 24);
        }
    }
}
