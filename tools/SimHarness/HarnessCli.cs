using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The <c>ci/run-checks.sh</c> entry point: parses argv, runs the named gate, and
    /// writes exactly the §19.3 stdout line. Spec: 19-interfaces-harness.md §19.1, §19.3,
    /// §19.4 (Q-025, Q-026).
    /// </summary>
    public static class HarnessCli
    {
        private const uint TicksPerDay = (uint)SimConstants.TICKS_PER_SIM_DAY;

        // The largest --days value whose ticks (D x TICKS_PER_SIM_DAY) still fits a uint32.
        private const ulong MaxDays = (ulong)uint.MaxValue / SimConstants.TICKS_PER_SIM_DAY;

        // saveload/promotion/budget run with this fixed seed (§19.3); only determinism takes --seed.
        private const ulong FixedSeed = 12345UL;

        private const long BudgetMeanCeilingUs = 6000;
        private const long BudgetP99CeilingUs = 12000;

        /// <summary>
        /// Runs one invocation. A call with no arguments keeps T-001's original
        /// behaviour; it is not a gate.
        /// </summary>
        public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
        {
            if (args is null)
            {
                throw new ArgumentNullException(nameof(args));
            }
            if (stdout is null)
            {
                throw new ArgumentNullException(nameof(stdout));
            }
            if (stderr is null)
            {
                throw new ArgumentNullException(nameof(stderr));
            }

            try
            {
                if (args.Count == 0)
                {
                    return RunLegacyDefault(stdout);
                }

                switch (args[0])
                {
                    case "determinism":
                        return RunDeterminism(args, stdout);
                    case "saveload":
                        return RunSaveLoad(args, stdout);
                    case "promotion":
                        return RunPromotion(args, stdout);
                    case "budget":
                        return RunBudget(args, stdout);
                    default:
                        stderr.WriteLine("tools.simharness: unknown subcommand '" + args[0] + "'");
                        return 2;
                }
            }
            catch (UsageException ex)
            {
                stderr.WriteLine("tools.simharness: usage error: " + ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                stderr.WriteLine(ex.ToString());
                return 3;
            }
        }

        // -------------------------------------------------------------- subcommands

        private static int RunDeterminism(IReadOnlyList<string> args, TextWriter stdout)
        {
            var spec = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["--days"] = true,
                ["--seed"] = true,
                ["--hash-only"] = false,
            };
            ParseFlags(args, spec, out Dictionary<string, string> values, out HashSet<string> present);

            uint ticks = ParseDaysAsTicks(RequireValue(values, "--days"));
            ulong seed = ParseUnsignedDecimal(RequireValue(values, "--seed"), "--seed");
            bool hashOnly = present.Contains("--hash-only");

            var content = new EmptyContentIndex();
            if (hashOnly)
            {
                string hash = HarnessGates.FinalHash(content, NoSystems, seed, ticks);
                stdout.Write(hash + "\n");
                return 0;
            }

            GateResult r = HarnessGates.SameProcess(content, NoSystems, seed, ticks);
            stdout.Write(r.Report + "\n");
            return r.Passed ? 0 : 1;
        }

        private static int RunSaveLoad(IReadOnlyList<string> args, TextWriter stdout)
        {
            var spec = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["--ticks"] = true,
                ["--save-at"] = true,
            };
            ParseFlags(args, spec, out Dictionary<string, string> values, out _);

            ulong rawTicks = ParseUnsignedDecimal(RequireValue(values, "--ticks"), "--ticks");
            if (rawTicks == 0 || rawTicks > uint.MaxValue)
            {
                throw new UsageException("--ticks must be a positive value that fits a uint32");
            }
            uint ticks = (uint)rawTicks;

            ulong rawSaveAt = ParseUnsignedDecimal(RequireValue(values, "--save-at"), "--save-at");
            if (rawSaveAt == 0 || rawSaveAt >= ticks)
            {
                throw new UsageException("--save-at must satisfy 0 < save-at < ticks");
            }
            uint saveAt = (uint)rawSaveAt;

            var content = new EmptyContentIndex();
            GateResult r = HarnessGates.SaveLoad(content, NoSystems, FixedSeed, ticks, saveAt);
            stdout.Write(r.Report + "\n");
            return r.Passed ? 0 : 1;
        }

        private static int RunPromotion(IReadOnlyList<string> args, TextWriter stdout)
        {
            var spec = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["--days"] = true,
            };
            ParseFlags(args, spec, out Dictionary<string, string> values, out _);

            uint ticks = ParseDaysAsTicks(RequireValue(values, "--days"));

            var content = new EmptyContentIndex();
            GateResult r = HarnessGates.Promotion(content, NoSystems, FixedSeed, ticks);
            stdout.Write(r.Report + "\n");
            return r.Passed ? 0 : 1;
        }

        private static int RunBudget(IReadOnlyList<string> args, TextWriter stdout)
        {
            var spec = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["--tier"] = true,
            };
            ParseFlags(args, spec, out Dictionary<string, string> values, out _);

            string tier = RequireValue(values, "--tier");
            if (!string.Equals(tier, "max", StringComparison.Ordinal))
            {
                throw new UsageException("--tier must be 'max'");
            }

            var content = new EmptyContentIndex();
            var checkpoints = new NullCheckpointSink();
            ISimHost host = HarnessRunner.BuildOne(content, NoSystems, FixedSeed, checkpoints);

            uint ticks = TicksPerDay;
            long[] samplesUs = new long[ticks];
            long frequency = Stopwatch.Frequency;
            for (uint i = 0; i < ticks; i++)
            {
                long start = Stopwatch.GetTimestamp();
                host.Step(1);
                long end = Stopwatch.GetTimestamp();
                samplesUs[i] = ((end - start) * 1_000_000L) / frequency;
            }

            long sum = 0;
            for (int i = 0; i < samplesUs.Length; i++)
            {
                sum += samplesUs[i];
            }
            long meanUs = sum / samplesUs.Length;

            long[] sorted = (long[])samplesUs.Clone();
            Array.Sort(sorted);
            long n = sorted.Length;
            long p99Index = ((99 * n) + 99) / 100 - 1;
            long p99Us = sorted[p99Index];

            bool passed = meanUs <= BudgetMeanCeilingUs && p99Us <= BudgetP99CeilingUs;
            string line = (passed ? "PASS" : "FAIL") + " budget ticks=" + ticks.ToString(CultureInfo.InvariantCulture) +
                " mean_us=" + meanUs.ToString(CultureInfo.InvariantCulture) +
                " p99_us=" + p99Us.ToString(CultureInfo.InvariantCulture);
            stdout.Write(line + "\n");
            return passed ? 0 : 1;
        }

        private static int RunLegacyDefault(TextWriter stdout)
        {
            var log = new NullSimLog();
            var checkpoints = new NullCheckpointSink();
            var content = new EmptyContentIndex();

            var config = new SimHostConfig(masterSeed: 1, content: content, checkpoints: checkpoints, log: log);
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            ISimHost host = builder.Build();

            host.Step(checked((uint)SimConstants.TICKS_PER_SIM_DAY));

            stdout.Write("tick=" + host.CurrentTick.ToString(CultureInfo.InvariantCulture) + "\n");
            stdout.Write("hash=" + host.WorldStateHash().ToString("x16", CultureInfo.InvariantCulture) + "\n");
            return 0;
        }

        private static void NoSystems(ISimHostBuilder builder)
        {
        }

        // -------------------------------------------------------------- argument parsing

        private static uint ParseDaysAsTicks(string text)
        {
            ulong days = ParseUnsignedDecimal(text, "--days");
            if (days == 0 || days > MaxDays)
            {
                throw new UsageException("--days must be positive and its ticks must fit a uint32");
            }
            return (uint)(days * TicksPerDay);
        }

        private static ulong ParseUnsignedDecimal(string text, string flag)
        {
            if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            {
                throw new UsageException(flag + " must be an unsigned decimal integer");
            }
            return value;
        }

        private static string RequireValue(IReadOnlyDictionary<string, string> values, string flag)
        {
            if (!values.TryGetValue(flag, out string? value))
            {
                throw new UsageException("missing required flag " + flag);
            }
            return value;
        }

        /// <summary>
        /// Parses <c>args[1..]</c> against a flag spec (flag name to "needs a value").
        /// Flags may come in any order; each may be given at most once; anything not a
        /// recognised flag (including a bare positional token) is a usage error.
        /// </summary>
        private static void ParseFlags(
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, bool> spec,
            out Dictionary<string, string> values,
            out HashSet<string> present)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            present = new HashSet<string>(StringComparer.Ordinal);

            int i = 1;
            while (i < args.Count)
            {
                string token = args[i];
                if (!spec.TryGetValue(token, out bool needsValue))
                {
                    throw new UsageException("unknown or misplaced token '" + token + "'");
                }
                if (values.ContainsKey(token) || present.Contains(token))
                {
                    throw new UsageException("flag '" + token + "' given more than once");
                }

                if (needsValue)
                {
                    if (i + 1 >= args.Count)
                    {
                        throw new UsageException("flag '" + token + "' needs a value");
                    }
                    values[token] = args[i + 1];
                    i += 2;
                }
                else
                {
                    present.Add(token);
                    i += 1;
                }
            }
        }

        private sealed class UsageException : Exception
        {
            internal UsageException(string message)
                : base(message)
            {
            }
        }
    }
}
