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
    /// </summary>
    internal sealed class HeadlessRun : IHeadlessRun
    {
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

            if (string.IsNullOrEmpty(request.OutputPath))
            {
                return 1;
            }

            try
            {
                // The dump header names the seed, which ISimHost does not expose: read it from the same bundle.json.
                ulong seed = SimComposer.ReadBundleJson(bundle, out HashSet<string> _);
                var sink = new RecordingSink();
                ComposedSim sim = _composer.Compose(bundle, sink);

                uint day = checked((uint)SimConstants.TICKS_PER_SIM_DAY);
                for (uint d = 0; d < request.Days; d++)
                {
                    sim.Host.Step(day);
                }

                File.WriteAllBytes(request.OutputPath, Render(seed, in sim, sink));
                return 0;
            }
            catch (FormatException)
            {
                return 1;
            }
            catch (IOException)
            {
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                return 1;
            }
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
