using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The four determinism gates <c>ci/run-checks.sh</c> invokes through
    /// <see cref="HarnessCli"/>. Spec: 19-interfaces-harness.md §19.1, §19.2 (Q-025,
    /// Q-026, Q-027).
    /// </summary>
    public static class HarnessGates
    {
        /// <summary>Two runs, in one process, must compare equal.</summary>
        public static GateResult SameProcess(IContentIndex content, SimComposer compose, ulong seed, uint ticks)
        {
            return CompareTwoRuns(content, compose, seed, ticks, "determinism_same_process");
        }

        /// <summary>
        /// Two runs, the second "camera parked", must compare equal. Vacuous until
        /// T-010 gives it something to promote (§19.2) — this task does not stub the
        /// comparison.
        /// </summary>
        public static GateResult Promotion(IContentIndex content, SimComposer compose, ulong seed, uint ticks)
        {
            return CompareTwoRuns(content, compose, seed, ticks, "determinism_promotion");
        }

        /// <summary>
        /// The interim save/load replay (Q-027): U runs whole; A runs to
        /// <paramref name="saveAt"/> and its log becomes the "save"; B replays that log
        /// from a fresh run and must match A at <paramref name="saveAt"/>, then match U
        /// at <paramref name="ticks"/>.
        /// </summary>
        public static GateResult SaveLoad(IContentIndex content, SimComposer compose, ulong seed, uint ticks, uint saveAt)
        {
            ValidateCommon(content, compose, ticks);
            if (saveAt == 0 || saveAt >= ticks)
            {
                throw new ArgumentOutOfRangeException(nameof(saveAt), saveAt, "saveAt must satisfy 0 < saveAt < ticks");
            }

            RunOutcome u = HarnessRunner.RunFull(content, compose, seed, ticks);

            var sinkA = new RecordingCheckpointSink();
            ISimHost hostA = HarnessRunner.BuildOne(content, compose, seed, sinkA);
            HarnessRunner.SubmitScript(hostA, ticks);
            hostA.Step(saveAt);
            ulong hashAAtSave = hostA.WorldStateHash();
            IReadOnlyList<Command> log = hostA.CommandLogSince(0);

            var sinkB = new RecordingCheckpointSink();
            ISimHost hostB = HarnessRunner.BuildOne(content, compose, seed, sinkB);
            for (int i = 0; i < log.Count; i++)
            {
                Command logged = log[i];
                var cmd = new Command(logged.Tick, logged.Issuer, logged.Kind, logged.Payload);
                if (!hostB.TrySubmit(in cmd, out CommandRejection reason))
                {
                    throw new InvalidOperationException(
                        "harness: replaying the saved command at tick " +
                        logged.Tick.ToString(CultureInfo.InvariantCulture) + " was rejected (" + reason + ")");
                }
            }

            hostB.Step(saveAt);
            ulong hashBAtSave = hostB.WorldStateHash();
            if (hashAAtSave != hashBAtSave)
            {
                return new GateResult(false, "FAIL determinism_save_load tick=" +
                    saveAt.ToString(CultureInfo.InvariantCulture) + " at=reload");
            }

            hostB.Step(ticks - saveAt);
            var b = new RunOutcome(sinkB.ToArray(), hostB.WorldStateHash());

            if (HarnessRunner.TryCompare(in u, in b, ticks, out ulong tick, out string where))
            {
                return new GateResult(true, "PASS determinism_save_load ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                    " checkpoints=" + u.Checkpoints.Length.ToString(CultureInfo.InvariantCulture) +
                    " final=" + HarnessRunner.Hex16(u.FinalHash));
            }

            return new GateResult(false, "FAIL determinism_save_load tick=" +
                tick.ToString(CultureInfo.InvariantCulture) + " at=" + where);
        }

        /// <summary>One run's final <c>WorldStateHash()</c>, as 16 lowercase hex digits.</summary>
        public static string FinalHash(IContentIndex content, SimComposer compose, ulong seed, uint ticks)
        {
            ValidateCommon(content, compose, ticks);
            RunOutcome r = HarnessRunner.RunFull(content, compose, seed, ticks);
            return HarnessRunner.Hex16(r.FinalHash);
        }

        private static GateResult CompareTwoRuns(IContentIndex content, SimComposer compose, ulong seed, uint ticks, string gate)
        {
            ValidateCommon(content, compose, ticks);
            RunOutcome a = HarnessRunner.RunFull(content, compose, seed, ticks);
            RunOutcome b = HarnessRunner.RunFull(content, compose, seed, ticks);

            if (HarnessRunner.TryCompare(in a, in b, ticks, out ulong tick, out string where))
            {
                return new GateResult(true, "PASS " + gate + " ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                    " checkpoints=" + a.Checkpoints.Length.ToString(CultureInfo.InvariantCulture) +
                    " final=" + HarnessRunner.Hex16(a.FinalHash));
            }

            return new GateResult(false, "FAIL " + gate + " tick=" + tick.ToString(CultureInfo.InvariantCulture) + " at=" + where);
        }

        private static void ValidateCommon(IContentIndex content, SimComposer compose, uint ticks)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (compose is null)
            {
                throw new ArgumentNullException(nameof(compose));
            }
            if (ticks == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "ticks must be at least 1");
            }
        }
    }
}
