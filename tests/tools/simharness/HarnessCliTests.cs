using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-006. HarnessCli.Run in process, against 19-interfaces-harness.md §19.3 and
    /// §19.4 (Q-026): the exact invocations ci/run-checks.sh uses, exit codes
    /// 0/1/2/3, and the one-line stdout formats. The CLI composition registers no
    /// systems (§19.2), so exact hashes follow from 08 §8.9 and the NoOp script.
    /// </summary>
    public sealed class HarnessCliTests
    {
        private static void AssertOneLfLine(CliResult r)
        {
            Assert.EndsWith("\n", r.Stdout);
            Assert.DoesNotContain("\r", r.Stdout);
            Assert.Equal(1, r.Stdout.Split('\n').Length - 1);
        }

        // ------------------------------------------------------------ passing invocations

        [Fact]
        public void test_harness_cli_determinism_passes_with_exact_line()
        {
            CliResult r = Cli("determinism", "--days", "1", "--seed", "12345");
            Assert.Equal(0, r.Exit);
            Assert.Equal("PASS determinism_same_process ticks=14400 checkpoints=24 final="
                + Hex16(EmptyCompositionFinalHash(TicksPerDay)) + "\n", r.Stdout);
            AssertOneLfLine(r);
        }

        [Fact]
        public void test_harness_cli_determinism_two_days_counts_ticks_and_checkpoints()
        {
            CliResult r = Cli("determinism", "--days", "2", "--seed", "7");
            Assert.Equal(0, r.Exit);
            Assert.Equal("PASS determinism_same_process ticks=28800 checkpoints=48 final="
                + Hex16(EmptyCompositionFinalHash(2 * TicksPerDay)) + "\n", r.Stdout);
        }

        [Fact]
        public void test_harness_cli_hash_only_prints_final_hash_line()
        {
            CliResult r = Cli("determinism", "--days", "1", "--seed", "12345", "--hash-only");
            Assert.Equal(0, r.Exit);
            Assert.Equal(Hex16(EmptyCompositionFinalHash(TicksPerDay)) + "\n", r.Stdout);
            AssertOneLfLine(r);
        }

        [Fact]
        public void test_harness_cli_hash_only_same_seed_identical_output()
        {
            // determinism_cross_process diffs two --hash-only outputs byte for byte.
            CliResult a = Cli("determinism", "--days", "1", "--seed", "12345", "--hash-only");
            CliResult b = Cli("determinism", "--days", "1", "--seed", "12345", "--hash-only");
            Assert.Equal(0, a.Exit);
            Assert.Equal(0, b.Exit);
            Assert.Equal(a.Stdout, b.Stdout);
        }

        [Fact]
        public void test_harness_cli_hash_only_matches_final_hash_gate()
        {
            CliResult r = Cli("determinism", "--days", "1", "--seed", "99", "--hash-only");
            string gate = HarnessGates.FinalHash(new EmptyContent(), NoSystems, 99UL, TicksPerDay);
            Assert.Equal(gate + "\n", r.Stdout);
        }

        [Fact]
        public void test_harness_cli_flags_in_any_order_give_same_output()
        {
            CliResult a = Cli("determinism", "--days", "1", "--seed", "3", "--hash-only");
            CliResult b = Cli("determinism", "--hash-only", "--seed", "3", "--days", "1");
            CliResult c = Cli("determinism", "--seed", "3", "--hash-only", "--days", "1");
            Assert.Equal(0, b.Exit);
            Assert.Equal(a.Stdout, b.Stdout);
            Assert.Equal(a.Stdout, c.Stdout);

            CliResult d = Cli("saveload", "--save-at", "500", "--ticks", "1000");
            Assert.Equal(0, d.Exit);
            Assert.Equal(Cli("saveload", "--ticks", "1000", "--save-at", "500").Stdout, d.Stdout);
        }

        [Fact]
        public void test_harness_cli_saveload_passes_with_exact_line()
        {
            CliResult r = Cli("saveload", "--ticks", "1000", "--save-at", "500");
            Assert.Equal(0, r.Exit);
            Assert.Equal("PASS determinism_save_load ticks=1000 checkpoints=2 final="
                + Hex16(EmptyCompositionFinalHash(1000)) + "\n", r.Stdout);
            AssertOneLfLine(r);
        }

        [Fact]
        public void test_harness_cli_promotion_passes_with_exact_line()
        {
            CliResult r = Cli("promotion", "--days", "1");
            Assert.Equal(0, r.Exit);
            Assert.Equal("PASS determinism_promotion ticks=14400 checkpoints=24 final="
                + Hex16(EmptyCompositionFinalHash(TicksPerDay)) + "\n", r.Stdout);
            AssertOneLfLine(r);
        }

        [Fact]
        public void test_harness_cli_leading_zero_values_are_decimal()
        {
            CliResult a = Cli("determinism", "--days", "01", "--seed", "0012345", "--hash-only");
            CliResult b = Cli("determinism", "--days", "1", "--seed", "12345", "--hash-only");
            Assert.Equal(0, a.Exit);
            Assert.Equal(b.Stdout, a.Stdout);
        }

        [Fact]
        public void test_harness_cli_seed_accepts_full_uint64_range()
        {
            CliResult r = Cli("determinism", "--days", "1", "--seed", "18446744073709551615", "--hash-only");
            Assert.Equal(0, r.Exit);
            Assert.Equal(Hex16(EmptyCompositionFinalHash(TicksPerDay)) + "\n", r.Stdout);
        }

        // ------------------------------------------------------------ budget

        [Fact]
        [Trait("Category", "Budget")]
        public void test_harness_cli_budget_max_prints_budget_line_consistent_with_exit()
        {
            CliResult r = Cli("budget", "--tier", "max");
            Assert.True(r.Exit == 0 || r.Exit == 1, "budget exits 0 or 1, got " + r.Exit + ": " + r.Stderr);
            AssertOneLfLine(r);
            Match m = Regex.Match(r.Stdout, "^(PASS|FAIL) budget ticks=14400 mean_us=([0-9]+) p99_us=([0-9]+)\n$");
            Assert.True(m.Success, "bad budget line: " + r.Stdout);
            long mean = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            long p99 = long.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            bool within = mean <= 6000 && p99 <= 12000;
            Assert.Equal(within ? "PASS" : "FAIL", m.Groups[1].Value);
            Assert.Equal(within ? 0 : 1, r.Exit);
            Assert.True(p99 >= 0 && mean >= 0);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_harness_cli_budget_core_only_day_passes()
        {
            // At T-006 the CLI composition is core alone (§19.4), far inside 6 ms/tick.
            CliResult r = Cli("budget", "--tier", "max");
            Assert.Equal(0, r.Exit);
            Assert.StartsWith("PASS budget ticks=14400 ", r.Stdout);
        }

        // ------------------------------------------------------------ usage errors

        [Theory]
        [InlineData("bogus")]
        [InlineData("DETERMINISM", "--days", "1", "--seed", "1")]
        [InlineData("checkpoint")]
        [InlineData("determinism")]
        [InlineData("determinism", "--days", "1")]
        [InlineData("determinism", "--seed", "1")]
        [InlineData("determinism", "--hash-only")]
        [InlineData("determinism", "--days")]
        [InlineData("determinism", "--days", "1", "--seed")]
        [InlineData("determinism", "--days", "0", "--seed", "1")]
        [InlineData("determinism", "--days", "-1", "--seed", "1")]
        [InlineData("determinism", "--days", "+1", "--seed", "1")]
        [InlineData("determinism", "--days", "1.0", "--seed", "1")]
        [InlineData("determinism", "--days", "one", "--seed", "1")]
        [InlineData("determinism", "--days", "", "--seed", "1")]
        [InlineData("determinism", "--days", " 1", "--seed", "1")]
        [InlineData("determinism", "--days", "0x1", "--seed", "1")]
        [InlineData("determinism", "--days", "298262", "--seed", "1")]
        [InlineData("determinism", "--days", "4294967296", "--seed", "1")]
        [InlineData("determinism", "--days", "1", "--seed", "18446744073709551616")]
        [InlineData("determinism", "--days", "1", "--seed", "-1")]
        [InlineData("determinism", "--days", "1", "--days", "1", "--seed", "1")]
        [InlineData("determinism", "--days", "1", "--seed", "1", "--seed", "1")]
        [InlineData("determinism", "--days", "1", "--seed", "1", "--hash-only", "--hash-only")]
        [InlineData("determinism", "--days", "1", "--seed", "1", "--bogus")]
        [InlineData("determinism", "--days", "1", "--seed", "1", "extra")]
        [InlineData("determinism", "--days", "1", "--seed", "1", "--ticks", "5")]
        [InlineData("saveload")]
        [InlineData("saveload", "--ticks", "1000")]
        [InlineData("saveload", "--save-at", "500")]
        [InlineData("saveload", "--ticks", "0", "--save-at", "0")]
        [InlineData("saveload", "--ticks", "1000", "--save-at", "0")]
        [InlineData("saveload", "--ticks", "1000", "--save-at", "1000")]
        [InlineData("saveload", "--ticks", "1000", "--save-at", "1001")]
        [InlineData("saveload", "--ticks", "4294967296", "--save-at", "1")]
        [InlineData("saveload", "--ticks", "1000", "--save-at", "500", "--seed", "1")]
        [InlineData("saveload", "--ticks", "1000", "--save-at", "500", "--ticks", "1000")]
        [InlineData("promotion")]
        [InlineData("promotion", "--days", "0")]
        [InlineData("promotion", "--days", "1", "--seed", "1")]
        [InlineData("promotion", "--days", "1", "--hash-only")]
        [InlineData("budget")]
        [InlineData("budget", "--tier")]
        [InlineData("budget", "--tier", "min")]
        [InlineData("budget", "--tier", "MAX")]
        [InlineData("budget", "--tier", "max", "--tier", "max")]
        [InlineData("budget", "--tier", "max", "--days", "1")]
        public void test_harness_cli_usage_error_exits_2_with_empty_stdout(params string[] args)
        {
            CliResult r = Cli(args);
            Assert.Equal(2, r.Exit);
            Assert.Equal("", r.Stdout);
            Assert.False(string.IsNullOrWhiteSpace(r.Stderr), "a usage error writes one message to stderr");
        }

        [Fact]
        public void test_harness_cli_days_overflowing_uint32_ticks_exits_2()
        {
            // 298261 × 14400 fits in uint32 and 298262 × 14400 does not (§19.3). Only the
            // rejected side is exercised here, since the accepted side would run for hours.
            Assert.True(298261UL * TicksPerDay <= uint.MaxValue);
            Assert.True(298262UL * TicksPerDay > uint.MaxValue);
            Assert.Equal(2, Cli("promotion", "--days", "298262").Exit);
        }

        [Fact]
        public void test_harness_cli_saveload_smallest_run_passes_with_exact_line()
        {
            // K = 1, T = 2 is the smallest save point §19.3 allows.
            CliResult r = Cli("saveload", "--ticks", "2", "--save-at", "1");
            Assert.Equal(0, r.Exit);
            Assert.Equal("PASS determinism_save_load ticks=2 checkpoints=1 final="
                + Hex16(EmptyCompositionFinalHash(2)) + "\n", r.Stdout);
        }
    }
}
