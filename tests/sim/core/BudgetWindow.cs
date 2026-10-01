using System;
using System.Diagnostics;
using AirportSim.Sim.Core;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// 03-module-map.md "Budget tests: window and arithmetic" (Q-044, Q-045)
    /// for sim.core. sim.core has no Tick, so its sample is one
    /// ISimHost.Step(1) call, checkpoint ticks included ("Measured"). Every
    /// timed Budget test in this project measures through here.
    /// </summary>
    internal static class BudgetWindow
    {
        /// <summary>sim.core's budget, 0.25 ms/tick (03 "Performance"), in whole microseconds.</summary>
        public const long CoreBudgetMicros = 250;

        /// <summary>Q-044: exactly TICKS_PER_SIM_DAY consecutive ticks, one sample per tick.</summary>
        public const int Ticks = (int)SimConstants.TICKS_PER_SIM_DAY;

        /// <summary>
        /// Steps the host one tick at a time for one window and returns each
        /// tick's raw Stopwatch difference. The storage is allocated before the
        /// measured loop. Any warm-up is the caller's and is not sampled.
        /// </summary>
        public static long[] StepWindow(ISimHost host)
        {
            long[] raw = new long[Ticks];
            for (int i = 0; i < raw.Length; i++)
            {
                long start = Stopwatch.GetTimestamp();
                host.Step(1);
                raw[i] = Stopwatch.GetTimestamp() - start;
            }

            return raw;
        }

        /// <summary>
        /// Q-045 steps 2 to 5 in long arithmetic: round each sample up to whole
        /// microseconds with the guard and the cap C = B x n + 1, pass the mean
        /// iff sum(u) &lt;= B x n and p99 (nearest rank) iff p99 &lt;= 2 x B.
        /// The rounded-up mean and the p99 go to the test output on every run,
        /// before either assertion.
        /// </summary>
        public static void AssertWithin(long[] raw, long budgetMicros, string subject, ITestOutputHelper output)
        {
            long n = raw.Length;
            Assert.Equal((long)Ticks, n);

            long f = Stopwatch.Frequency;
            long cap = budgetMicros * n + 1;
            long guard = (long.MaxValue - f + 1) / 1_000_000;
            long[] u = new long[n];
            long sum = 0;
            for (long i = 0; i < n; i++)
            {
                long d = raw[i];
                u[i] = d > guard ? cap : Math.Min((d * 1_000_000 + f - 1) / f, cap);
                sum += u[i];
            }

            Array.Sort(u);
            long p99 = u[(99 * n + 99) / 100 - 1];
            string report = subject + " over " + n + " ticks: mean " + ((sum + n - 1) / n) + " us, p99 " + p99
                + " us, max " + u[n - 1] + " us; budget " + budgetMicros + " us, Stopwatch.Frequency " + f;
            output.WriteLine(report);

            Assert.True(sum <= budgetMicros * n, "mean over budget. " + report);
            Assert.True(p99 <= 2 * budgetMicros, "p99 over " + (2 * budgetMicros) + " us. " + report);
        }
    }
}
