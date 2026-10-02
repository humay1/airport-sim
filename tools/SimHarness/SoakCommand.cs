using System;
using System.Globalization;
using System.IO;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The <c>soak</c> subcommand: one run over the soak fixture set, its checkpoint dump
    /// compared with a golden or written to a new file. Spec: 19-interfaces-harness.md
    /// §19.2b, §19.3 (Q-057).
    /// </summary>
    internal static class SoakCommand
    {
        private const ulong Seed = 12345UL;

        /// <summary>
        /// Runs the gate. Exactly one of <paramref name="goldenPath"/> and
        /// <paramref name="outPath"/> is non-null; the CLI has already decided usage.
        /// Returns 0 or 1; a harness error is an exception, which the CLI maps to exit 3.
        /// </summary>
        internal static int Run(uint days, string? goldenPath, string? outPath, TextWriter stdout, TextWriter stderr)
        {
            string? root = null;
            string? goldenFile = goldenPath == null ? null : ResolvePath(goldenPath, ref root);
            string? outFile = outPath == null ? null : ResolvePath(outPath, ref root);

            // The path checks come before the run, so a bad path costs no run.
            byte[]? golden = null;
            if (goldenFile != null)
            {
                golden = File.ReadAllBytes(goldenFile);
            }
            if (outFile != null)
            {
                if (File.Exists(outFile) || Directory.Exists(outFile))
                {
                    throw new InvalidOperationException("soak: --out " + outFile + " already exists");
                }
                string? parent = Path.GetDirectoryName(outFile);
                if (parent != null && !Directory.Exists(parent))
                {
                    throw new InvalidOperationException("soak: --out " + outFile + ": parent directory " + parent + " does not exist");
                }
            }

            Phase0Composition composition = Phase0Composition.Load(Phase0Composition.Soak);
            uint ticks = checked(days * (uint)SimConstants.TICKS_PER_SIM_DAY);
            RunOutcome run = HarnessRunner.RunFull(composition.Content, composition.Compose, Seed, ticks);
            byte[] dump = CheckpointsCommand.Render(Seed, composition.RegisteredNames, run.Checkpoints);
            string final = HarnessRunner.Hex16(run.FinalHash);

            if (golden != null)
            {
                int line = FirstDifferingLine(dump, golden);
                if (line != 0)
                {
                    stderr.WriteLine("tools.simharness: soak: dump differs from " + goldenFile + " at line " + line.ToString(CultureInfo.InvariantCulture));
                    stdout.Write("FAIL soak line=" + line.ToString(CultureInfo.InvariantCulture) + "\n");
                    return 1;
                }
                stdout.Write(
                    "PASS soak ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                    " checkpoints=" + run.Checkpoints.Length.ToString(CultureInfo.InvariantCulture) +
                    " final=" + final + "\n");
                return 0;
            }

            using (var file = new FileStream(outFile!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(dump, 0, dump.Length);
            }
            stdout.Write(
                "WROTE soak ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                " checkpoints=" + run.Checkpoints.Length.ToString(CultureInfo.InvariantCulture) +
                " final=" + final + "\n");
            return 0;
        }

        /// <summary>
        /// §19.2b: 0 if the two are byte-identical; otherwise <c>L</c>, 1 plus the number of LF
        /// bytes in <paramref name="dump"/> before the first differing offset (or before the
        /// shorter length, if one is a prefix of the other).
        /// </summary>
        private static int FirstDifferingLine(byte[] dump, byte[] golden)
        {
            int shared = Math.Min(dump.Length, golden.Length);
            int o = 0;
            while (o < shared && dump[o] == golden[o])
            {
                o++;
            }
            if (o == dump.Length && o == golden.Length)
            {
                return 0;
            }
            int line = 1;
            for (int i = 0; i < o; i++)
            {
                if (dump[i] == (byte)'\n')
                {
                    line++;
                }
            }
            return line;
        }

        /// <summary>
        /// §19.2b "Paths": a fully qualified path is used as given; any other is a
        /// <c>/</c>-separated repository-relative path joined to the root.
        /// </summary>
        private static string ResolvePath(string path, ref string? root)
        {
            if (Path.IsPathFullyQualified(path))
            {
                return path;
            }
            root ??= Phase0Composition.FindRoot();
            return Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
