using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.App.Host
{
    /// <summary>
    /// The headless checkpoint run and its dump, version 1. Spec: 16 §16.8. It
    /// composes the bundle, submits no command (Q-071), steps
    /// <c>TICKS_PER_SIM_DAY</c> exactly <c>Days</c> times, and has no presentation.
    /// Every failure is a return value of 3 with one line on the error stream (Q-120).
    /// </summary>
    internal sealed class HeadlessRun : IHeadlessRun
    {
        private const uint MaxDays = 298261;

        private readonly ISimComposer _composer;

        internal HeadlessRun(ISimComposer composer)
        {
            _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        }

        public int Run(IScenarioBundle bundle, in CheckpointRunRequest request)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (request.Days < 1 || request.Days > MaxDays)
            {
                throw new ArgumentOutOfRangeException(nameof(request), "Days is outside 1 to 298261");
            }

            if (string.IsNullOrEmpty(request.OutputPath))
            {
                throw new ArgumentOutOfRangeException(nameof(request), "OutputPath is null or empty");
            }

            string path = request.OutputPath;

            // Stage 1: the output path.
            string? failure = CheckOutputPath(path);
            if (failure != null)
            {
                return Fail(failure);
            }

            // Stage 2: composition.
            ulong seed;
            ComposedSim sim;
            var sink = new RecordingSink();
            try
            {
                seed = SimComposer.ReadBundleJson(bundle, out HashSet<string> _);
                sim = _composer.Compose(bundle, sink);
            }
            catch (Exception e)
            {
                return Fail("FAIL checkpoints load " + e.GetType().Name + ": " + e.Message);
            }

            // Stage 3: the run.
            byte[] dump;
            try
            {
                uint day = checked((uint)SimConstants.TICKS_PER_SIM_DAY);
                for (uint d = 0; d < request.Days; d++)
                {
                    sim.Host.Step(day);
                }

                dump = Render(seed, in sim, sink);
            }
            catch (Exception e)
            {
                return Fail("FAIL checkpoints run " + e.GetType().Name + ": " + e.Message);
            }

            // Stage 4: a new file, never overwriting one.
            try
            {
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(dump, 0, dump.Length);
                }
            }
            catch (Exception e)
            {
                return Fail("FAIL checkpoints write " + path + " " + e.GetType().Name + ": " + e.Message);
            }

            return 0;
        }

        /// <summary>Stage 1: null when fine, else the failure line.</summary>
        private static string? CheckOutputPath(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return "FAIL checkpoints output-exists " + path;
                }

                string? parent = Path.GetDirectoryName(Path.GetFullPath(path));
                if (parent != null && !Directory.Exists(parent))
                {
                    return "FAIL checkpoints output-no-parent " + path;
                }

                return null;
            }
            catch (Exception)
            {
                return "FAIL checkpoints output-no-parent " + path;
            }
        }

        /// <summary>One line on the error stream, plain ASCII: anything outside U+0020 to U+007E becomes '?'.</summary>
        private static int Fail(string line)
        {
            var ascii = new StringBuilder(line.Length);
            foreach (char c in line)
            {
                ascii.Append(c >= ' ' && c <= '~' ? c : '?');
            }

            Console.Error.WriteLine(ascii.ToString());
            return 3;
        }

        /// <summary>
        /// UTF-8 without a BOM, LF line endings, a final newline, single spaces,
        /// decimal ticks and 16 lowercase hexadecimal digits per hash (16 §16.8).
        /// </summary>
        private static byte[] Render(ulong seed, in ComposedSim sim, RecordingSink sink)
        {
            var text = new StringBuilder();
            text.Append("airport-sim-checkpoints 1\n");
            text.Append("seed ").Append(seed.ToString(CultureInfo.InvariantCulture)).Append('\n');

            text.Append("systems");
            ISimSystem?[] registered = { sim.World, sim.Schedule, sim.Airside, sim.Flow, sim.Turnaround, sim.Delay };
            foreach (ISimSystem? system in registered)
            {
                if (system != null)
                {
                    text.Append(' ').Append(system.Name);
                }
            }

            text.Append('\n');

            foreach (Checkpoint cp in sink.Checkpoints)
            {
                text.Append(cp.Tick.ToString(CultureInfo.InvariantCulture));
                text.Append(' ').Append(cp.WorldHash.ToString("x16", CultureInfo.InvariantCulture));
                foreach (ulong hash in cp.SystemHashes)
                {
                    text.Append(' ').Append(hash.ToString("x16", CultureInfo.InvariantCulture));
                }

                text.Append('\n');
            }

            return new UTF8Encoding(false).GetBytes(text.ToString());
        }

        /// <summary>Records every checkpoint (08 §8.9), copying the hash array.</summary>
        private sealed class RecordingSink : ICheckpointSink
        {
            internal readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();

            public void Record(in Checkpoint cp)
            {
                Checkpoints.Add(new Checkpoint(cp.Tick, cp.WorldHash, cp.CoreHash, (ulong[])cp.SystemHashes.Clone()));
            }
        }
    }
}
