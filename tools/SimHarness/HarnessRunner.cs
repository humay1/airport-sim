using System;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The "one run" and "comparing two runs" machinery behind every
    /// <see cref="HarnessGates"/> member. Spec: 19-interfaces-harness.md §19.1, §19.2
    /// (Q-025, Q-026, Q-027).
    /// </summary>
    internal static class HarnessRunner
    {
        /// <summary>
        /// Builds one host: a fresh builder, <paramref name="compose"/> called exactly
        /// once, then <c>Build()</c>. The composer must not call <c>Build</c> itself
        /// (§19.1).
        /// </summary>
        internal static ISimHost BuildOne(IContentIndex content, SimComposer compose, ulong seed, ICheckpointSink checkpoints)
        {
            var config = new SimHostConfig(seed, content, checkpoints, new NullSimLog());
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            compose(builder);
            return builder.Build();
        }

        /// <summary>
        /// Submits the §19.2 command script: one <c>NoOp</c> per tick <c>t</c> with
        /// <c>1 &lt;= t &lt; ticks</c> and <c>t % 100 == 0</c>, ascending. Throws
        /// <see cref="InvalidOperationException"/> if a submit is rejected.
        /// </summary>
        internal static void SubmitScript(ISimHost host, uint ticks)
        {
            for (ulong t = 100; t < ticks; t += 100)
            {
                var cmd = new Command(t, SimConstants.PLAYER_LOCAL, CommandKind.NoOp, Array.Empty<byte>());
                if (!host.TrySubmit(in cmd, out CommandRejection reason))
                {
                    throw new InvalidOperationException(
                        "harness: the scripted NoOp at tick " + t.ToString(CultureInfo.InvariantCulture) +
                        " was rejected (" + reason.ToString() + ")");
                }
            }
        }

        /// <summary>One run, in full: build, script, step every tick, then the final hash.</summary>
        internal static RunOutcome RunFull(IContentIndex content, SimComposer compose, ulong seed, uint ticks)
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = BuildOne(content, compose, seed, sink);
            SubmitScript(host, ticks);
            host.Step(ticks);
            return new RunOutcome(sink.ToArray(), host.WorldStateHash());
        }

        /// <summary>
        /// The §19.2 run comparison: checkpoint by checkpoint, in order, then by final
        /// hash. Returns false and the first difference's tick and <c>&lt;where&gt;</c>
        /// (<c>count</c>, <c>core</c>, <c>system:&lt;j&gt;</c>, <c>world</c> or
        /// <c>final</c>) on the first disagreement.
        /// </summary>
        internal static bool TryCompare(in RunOutcome a, in RunOutcome b, uint ticks, out ulong tick, out string where)
        {
            Checkpoint[] ca = a.Checkpoints;
            Checkpoint[] cb = b.Checkpoints;
            int shared = Math.Min(ca.Length, cb.Length);
            for (int i = 0; i < shared; i++)
            {
                Checkpoint x = ca[i];
                Checkpoint y = cb[i];
                if (x.CoreHash != y.CoreHash)
                {
                    tick = x.Tick;
                    where = "core";
                    return false;
                }

                int sharedSystems = Math.Min(x.SystemHashes.Length, y.SystemHashes.Length);
                for (int j = 0; j < sharedSystems; j++)
                {
                    if (x.SystemHashes[j] != y.SystemHashes[j])
                    {
                        tick = x.Tick;
                        where = "system:" + j.ToString(CultureInfo.InvariantCulture);
                        return false;
                    }
                }

                if (x.SystemHashes.Length != y.SystemHashes.Length)
                {
                    tick = x.Tick;
                    where = "system:" + sharedSystems.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                if (x.WorldHash != y.WorldHash)
                {
                    tick = x.Tick;
                    where = "world";
                    return false;
                }
            }

            if (ca.Length != cb.Length)
            {
                Checkpoint firstOnlyInOne = ca.Length > cb.Length ? ca[shared] : cb[shared];
                tick = firstOnlyInOne.Tick;
                where = "count";
                return false;
            }

            if (a.FinalHash != b.FinalHash)
            {
                tick = ticks;
                where = "final";
                return false;
            }

            tick = 0;
            where = string.Empty;
            return true;
        }

        internal static string Hex16(ulong v) => v.ToString("x16", CultureInfo.InvariantCulture);
    }
}
