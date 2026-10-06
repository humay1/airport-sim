using System;
using System.Collections.Generic;
using Xunit;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. IHostCommandLine.TryParse (16 §16.8, Q-114): true exactly when the
    /// ordinal token -airportsim-checkpoints occurs once and the two arguments
    /// after it are a valid days value (ASCII digits, no sign, no leading zero,
    /// 1 to 298 261) and a non-empty output path. Every other argument is
    /// ignored wherever it is. Otherwise false with request = default, and it
    /// throws nothing.
    /// </summary>
    public sealed class CommandLineTests
    {
        private const string Token = "-airportsim-checkpoints";

        private static readonly string[] Engine = { "-batchmode", "-nographics", "-logFile", "-" };

        private static string Show(IReadOnlyList<string?> args)
        {
            var parts = new List<string>();
            foreach (string? a in args)
            {
                parts.Add(a == null ? "<null>" : "'" + a + "'");
            }

            return "[" + string.Join(", ", parts) + "]";
        }

        private static void AssertParses(IHostCommandLine cli, string[] args, uint days, string path)
        {
            bool ok = false;
            CheckpointRunRequest request = default;
            Exception? e = Record.Exception(() => ok = cli.TryParse(args, out request));
            Assert.True(e == null, Show(args) + " threw " + e);
            Assert.True(ok, Show(args) + " was rejected");
            Assert.True(request.Days == days, Show(args) + ": Days " + request.Days + ", expected " + days);
            Assert.True(string.Equals(path, request.OutputPath, StringComparison.Ordinal), Show(args) + ": OutputPath '" + request.OutputPath + "', expected '" + path + "'");
        }

        private static void AssertRejects(IHostCommandLine cli, string?[] args)
        {
            bool ok = true;
            CheckpointRunRequest request = new CheckpointRunRequest(7, "sentinel");
            Exception? e = Record.Exception(() => ok = cli.TryParse(args!, out request));
            Assert.True(e == null, Show(args) + " threw " + e);
            Assert.False(ok, Show(args) + " was accepted");
            Assert.True(request.Days == 0 && request.OutputPath == null, Show(args) + ": request is not default after false");
        }

        private static string[] Join(params string[][] parts)
        {
            var list = new List<string>();
            foreach (string[] p in parts)
            {
                list.AddRange(p);
            }

            return list.ToArray();
        }

        [Fact]
        public void test_command_line_parses_checkpoint_run_and_rejects_others()
        {
            IHostCommandLine cli = HostFactory.CreateCommandLine();

            // The three tokens alone, and the bounds of <days> (19 §19.3's --days).
            AssertParses(cli, new[] { Token, "1", "out.txt" }, 1, "out.txt");
            AssertParses(cli, new[] { Token, "298261", "/tmp/dump" }, 298261, "/tmp/dump");
            AssertParses(cli, new[] { Token, "42", "C:/a b/c d.txt" }, 42, "C:/a b/c d.txt");

            // The engine's own arguments (-batchmode -nographics -logFile -) are
            // ignored wherever they sit around the three tokens (Q-114).
            string[] run = { Token, "1", "dump.txt" };
            AssertParses(cli, Join(Engine, run), 1, "dump.txt");
            AssertParses(cli, Join(run, Engine), 1, "dump.txt");
            AssertParses(cli, Join(new[] { "-batchmode", "-nographics" }, run, new[] { "-logFile", "-" }), 1, "dump.txt");
            AssertParses(cli, Join(new[] { "-logFile", "-" }, run, new[] { "-batchmode" }, new[] { "-nographics" }), 1, "dump.txt");
            AssertParses(cli, Join(new[] { "-batchmode", "-nographics", "-logFile" }, new[] { "-", Token, "3", "p" }), 3, "p");

            // <outputPath> is any non-empty string, even one that looks like a flag.
            AssertParses(cli, Join(new[] { "-batchmode", Token, "2", "-" }), 2, "-");

            // Rejected: the token absent, or not exactly the ordinal token.
            AssertRejects(cli, new string[0]);
            AssertRejects(cli, Engine);
            AssertRejects(cli, Join(Engine, new[] { "1", "dump.txt" }));
            AssertRejects(cli, new[] { "--airportsim-checkpoints", "1", "p" });
            AssertRejects(cli, new[] { "-AirportSim-Checkpoints", "1", "p" });
            AssertRejects(cli, new[] { "-airportsim-checkpoints ", "1", "p" });
            AssertRejects(cli, new[] { "-airportsim-checkpoints=1", "p" });

            // Rejected: the token more than once, even with valid values each time.
            AssertRejects(cli, new[] { Token, "1", "p", Token, "1", "q" });
            AssertRejects(cli, new[] { Token, Token, "p" });
            AssertRejects(cli, Join(Engine, new[] { Token, "1", "p" }, new[] { Token }));

            // Rejected: values missing.
            AssertRejects(cli, new[] { Token });
            AssertRejects(cli, new[] { Token, "1" });
            AssertRejects(cli, Join(Engine, new[] { Token, "1" }));

            // Rejected: <days> not ASCII digits without sign or leading zero, or out of 1 .. 298 261.
            foreach (string days in new[] { "0", "298262", "01", "+1", "-1", "1.0", "1e3", "1,000", " 1", "1 ", "", "abc", "\u0661", "\uFF11", "4294967297", "99999999999999999999" })
            {
                AssertRejects(cli, new[] { Token, days, "p" });
            }

            // Rejected: <outputPath> empty.
            AssertRejects(cli, new[] { Token, "1", "" });
            AssertRejects(cli, Join(new[] { Token, "1", "" }, Engine));
        }

        [Fact]
        public void test_command_line_null_arguments_return_false_without_throwing()
        {
            // 16 §16.8: "In every other case ... it returns false and request is
            // default. It throws nothing."
            IHostCommandLine cli = HostFactory.CreateCommandLine();
            bool ok = true;
            CheckpointRunRequest request = new CheckpointRunRequest(7, "sentinel");
            Exception? e = Record.Exception(() => ok = cli.TryParse(null!, out request));
            Assert.True(e == null, "a null list threw " + e);
            Assert.False(ok);
            Assert.True(request.Days == 0 && request.OutputPath == null, "request is not default after a null list");

            AssertRejects(cli, new string?[] { Token, null, "p" });
            AssertRejects(cli, new string?[] { Token, "1", null });

            // A null elsewhere is not the token or one of its values: it is ignored.
            AssertParses(cli, new string[] { null!, Token, "1", "p", null! }, 1, "p");
        }
    }
}
