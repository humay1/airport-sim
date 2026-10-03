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
        /// Two runs, the second "camera parked" on the lowest-id gate (§19.2d), must
        /// compare equal.
        /// </summary>
        public static GateResult Promotion(IContentIndex content, SimComposer compose, ulong seed, uint ticks)
        {
            return CompareTwoRuns(content, compose, seed, ticks, "determinism_promotion", promoteSecondRun: true);
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

        /// <summary>
        /// The pure budget statistic of §19.4 (Q-058): <c>03</c> "Budget tests: window and
        /// arithmetic" (Q-045) with B = 6000 and n = TICKS_PER_SIM_DAY. Each raw sample
        /// is rounded up to whole microseconds and capped at C = B x n + 1; the mean
        /// passes iff the sum is at most B x n and is reported rounded up; p99 is the
        /// nearest rank. Runs nothing, reads no clock, does not modify
        /// <paramref name="samples"/>.
        /// </summary>
        public static GateResult BudgetFromSamples(IReadOnlyList<long> samples, long frequency)
        {
            const long B = 6000;
            const long N = (long)SimConstants.TICKS_PER_SIM_DAY;
            const long C = B * N + 1;
            const long MaxFrequency = long.MaxValue / (C + 1);

            if (samples is null)
            {
                throw new ArgumentNullException(nameof(samples));
            }
            if (samples.Count != N)
            {
                throw new ArgumentOutOfRangeException(nameof(samples), samples.Count, "samples must hold exactly TICKS_PER_SIM_DAY entries");
            }
            if (frequency <= 0 || frequency > MaxFrequency)
            {
                throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "frequency is outside 03's bound");
            }

            long guard = (long.MaxValue - frequency + 1) / 1_000_000L;
            long[] u = new long[N];
            long sum = 0;
            for (int i = 0; i < u.Length; i++)
            {
                long d = samples[i];
                if (d < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(samples), d, "samples must not be negative");
                }

                long us = d > guard ? C : Math.Min((d * 1_000_000L + frequency - 1) / frequency, C);
                u[i] = us;
                sum += us;
            }

            Array.Sort(u);
            long p99 = u[(99 * N + 99) / 100 - 1];
            long meanUs = (sum + N - 1) / N;
            bool passed = sum <= B * N && p99 <= 2 * B;
            return new GateResult(passed, (passed ? "PASS" : "FAIL") + " budget ticks=" + N.ToString(CultureInfo.InvariantCulture) +
                " mean_us=" + meanUs.ToString(CultureInfo.InvariantCulture) +
                " p99_us=" + p99.ToString(CultureInfo.InvariantCulture));
        }

        private static GateResult CompareTwoRuns(IContentIndex content, SimComposer compose, ulong seed, uint ticks, string gate, bool promoteSecondRun = false)
        {
            ValidateCommon(content, compose, ticks);
            RunOutcome a = HarnessRunner.RunFull(content, compose, seed, ticks);
            RunOutcome b = promoteSecondRun
                ? HarnessRunner.RunPromoted(content, compose, seed, ticks)
                : HarnessRunner.RunFull(content, compose, seed, ticks);

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
