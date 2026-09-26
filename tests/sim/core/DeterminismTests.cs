using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// 02-determinism.md and 08 §8.2 "Chunking is invisible": equal configs
    /// and equal systems give equal hashes, checkpoints, events and log lines,
    /// however Step is chunked. Q-014 A8: replay on a fresh host stands in for
    /// save/load, which is sim.save's. 08 §8.10: a null and a capturing log
    /// sink give identical hashes.
    /// </summary>
    public sealed class DeterminismTests
    {
        private sealed class Run
        {
            public Run(RichFixture f, CapturingLog log)
            {
                Checkpoints = f.Sink.Describe();
                Trace = f.Trace;
                Log = log.Lines;
                FinalHash = f.Host.WorldStateHash();
                Tick = f.Host.CurrentTick;
            }

            public List<string> Checkpoints { get; }

            public List<string> Trace { get; }

            public List<string> Log { get; }

            public ulong FinalHash { get; }

            public ulong Tick { get; }
        }

        private static Run RunSingle(uint ticks)
        {
            var log = new CapturingLog();
            var f = new RichFixture(log);
            f.Host.Step(ticks);
            return new Run(f, log);
        }

        private static Run RunChunked(IEnumerable<uint> chunks)
        {
            var log = new CapturingLog();
            var f = new RichFixture(log);
            foreach (uint c in chunks)
            {
                f.Host.Step(c);
            }

            return new Run(f, log);
        }

        private static void AssertSameRun(Run expected, Run actual)
        {
            Assert.Equal(expected.Tick, actual.Tick);
            Assert.Equal(expected.Checkpoints, actual.Checkpoints);
            Assert.Equal(expected.Trace, actual.Trace);
            Assert.Equal(expected.Log, actual.Log);
            Assert.Equal(expected.FinalHash, actual.FinalHash);
        }

        [Fact]
        public void test_determinism_same_config_twice_gives_identical_day()
        {
            Run a = RunSingle(14400);
            Run b = RunSingle(14400);

            Assert.Equal(24, a.Checkpoints.Count);
            Assert.NotEmpty(a.Log);
            AssertSameRun(a, b);
        }

        [Fact]
        public void test_determinism_chunked_steps_match_single_step_property()
        {
            const ulong seed = 0xC4_0A1CUL;
            Run whole = RunSingle(14400);
            var rng = new SplitMix64(seed);
            for (int i = 0; i < 4; i++)
            {
                var chunks = new List<uint>();
                ulong total = 0;
                while (total < 14400UL)
                {
                    uint c = Harness.NextChunk(rng, 14400UL - total, 1500);
                    chunks.Add(c);
                    total += c;
                }

                Run chunked = RunChunked(chunks);
                Assert.True(
                    whole.FinalHash == chunked.FinalHash && whole.Checkpoints.Count == chunked.Checkpoints.Count,
                    $"seed {seed:X}, iteration {i}: chunking [{string.Join(",", chunks)}] changed the run");
                AssertSameRun(whole, chunked);
            }
        }

        [Fact]
        public void test_determinism_split_at_checkpoint_boundaries_is_invisible()
        {
            Run whole = RunSingle(1801);
            AssertSameRun(whole, RunChunked(new uint[] { 600, 600, 601 }));
            AssertSameRun(whole, RunChunked(new uint[] { 599, 1, 1, 599, 601 }));
            AssertSameRun(whole, RunChunked(new uint[] { 1, 0, 1799, 0, 1 }));
        }

        [Fact]
        public void test_determinism_single_tick_steps_match_one_long_step()
        {
            Run whole = RunSingle(1300);
            var ones = new uint[1300];
            for (int i = 0; i < ones.Length; i++)
            {
                ones[i] = 1;
            }

            AssertSameRun(whole, RunChunked(ones));
        }

        [Fact]
        public void test_determinism_replay_on_fresh_host_reproduces_two_days()
        {
            // Q-014 A8: a replay from equal config with a different chunking
            // is the T-001 stand-in for a save/load round trip.
            Run original = RunChunked(new uint[] { 14400, 14400 });
            Run replay = RunChunked(new uint[] { 7777, 6623, 1, 14399 });

            Assert.Equal(48, original.Checkpoints.Count);
            AssertSameRun(original, replay);
        }

        [Fact]
        public void test_determinism_log_sink_does_not_change_outcomes()
        {
            var capturing = new CapturingLog();
            var withLog = new RichFixture(capturing);
            var withoutLog = new RichFixture(new NullLog());

            withLog.Host.Step(14400);
            withoutLog.Host.Step(14400);

            Assert.NotEmpty(capturing.Lines);
            Assert.Equal(withLog.Sink.Describe(), withoutLog.Sink.Describe());
            Assert.Equal(withLog.Host.WorldStateHash(), withoutLog.Host.WorldStateHash());
            Assert.Equal(withLog.Trace, withoutLog.Trace);
        }
    }
}
