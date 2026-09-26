using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// ISimHost (08 §8.5) with the tick numbering of §8.2 (Q-014 A1), the
    /// headless day of T-001, and T-001's TrySubmit (Q-014 A3: UnknownKind).
    /// </summary>
    public sealed class HostTests
    {
        [Fact]
        public void test_host_first_step_executes_tick_zero()
        {
            var ticks = new List<ulong>();
            var probe = new ProbeSystem(1) { OnTick = (ProbeSystem self, in TickContext ctx) => ticks.Add(ctx.Tick) };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            host.Step(1);

            Assert.Equal(new[] { 0UL }, ticks);
            Assert.Equal(1UL, host.CurrentTick);
        }

        [Fact]
        public void test_host_step_executes_consecutive_ticks_in_order()
        {
            var ticks = new List<ulong>();
            var probe = new ProbeSystem(1) { OnTick = (ProbeSystem self, in TickContext ctx) => ticks.Add(ctx.Tick) };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            host.Step(3);
            Assert.Equal(3UL, host.CurrentTick);
            host.Step(4);

            Assert.Equal(new ulong[] { 0, 1, 2, 3, 4, 5, 6 }, ticks);
            Assert.Equal(7UL, host.CurrentTick);
        }

        [Fact]
        public void test_host_step_zero_does_nothing()
        {
            var sink = new RecordingCheckpointSink();
            var probe = new ProbeSystem(1) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick + 1UL) };
            ISimHost host = Harness.Build(sink, probe);

            host.Step(0);
            Assert.Equal(0UL, host.CurrentTick);
            Assert.Equal(0, probe.TickCalls);
            Assert.Empty(sink.Recorded);

            host.Step(2);
            ulong before = host.WorldStateHash();
            int checkpoints = sink.Recorded.Count;

            host.Step(0);

            Assert.Equal(2UL, host.CurrentTick);
            Assert.Equal(2, probe.TickCalls);
            Assert.Equal(checkpoints, sink.Recorded.Count);
            Assert.Equal(before, host.WorldStateHash());
        }

        [Fact]
        public void test_host_tick_context_tick_equals_clock_current_tick()
        {
            var mismatches = new List<string>();
            var probe = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Clock.CurrentTick != ctx.Tick)
                    {
                        mismatches.Add($"ctx.Tick={ctx.Tick} clock={ctx.Clock.CurrentTick}");
                    }
                },
            };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);
            host.Step(1234);
            Assert.Empty(mismatches);
            Assert.Equal(1234, probe.TickCalls);
        }

        [Fact]
        public void test_host_tick_context_carries_config_content_and_log()
        {
            var content = new EmptyContentIndex();
            var log = new CapturingLog();
            IContentIndex? seenContent = null;
            ISimLog? seenLog = null;
            var probe = new ProbeSystem(3)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    seenContent = ctx.Content;
                    seenLog = ctx.Log;
                },
            };
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(9UL, content, new RecordingCheckpointSink(), log));
            b.Register(probe);
            b.Build().Step(1);

            Assert.Same(content, seenContent);
            Assert.Same(log, seenLog);
        }

        [Fact]
        public void test_host_systems_tick_once_per_tick_in_registry_order()
        {
            var journal = new List<string>();
            TickAction record = (ProbeSystem self, in TickContext ctx) => journal.Add($"{ctx.Tick}:{self.Id.Value}");
            ISimHost host = Harness.Build(
                new RecordingCheckpointSink(),
                new ProbeSystem(2) { OnTick = record },
                new ProbeSystem(5) { OnTick = record },
                new ProbeSystem(11) { OnTick = record });

            host.Step(2);

            Assert.Equal(new[] { "0:2", "0:5", "0:11", "1:2", "1:5", "1:11" }, journal);
        }

        [Fact]
        public void test_host_headless_day_with_no_systems_runs_to_completion()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink);

            host.Step((uint)SimConstants.TICKS_PER_SIM_DAY);

            Assert.Equal(14400UL, host.CurrentTick);
            Assert.Equal(24, sink.Recorded.Count);
            Assert.Equal(Enumerable.Range(0, 24).Select(i => (ulong)i * 600UL), sink.Recorded.Select(c => c.Tick));
            Assert.All(sink.Recorded, c => Assert.Empty(c.SystemHashes));
        }

        [Fact]
        public void test_host_step_one_day_executes_exactly_day_zero()
        {
            var ticks = new List<ulong>();
            var days = new HashSet<uint>();
            var probe = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ticks.Add(ctx.Tick);
                    days.Add(ctx.Clock.DayIndex);
                },
            };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            host.Step((uint)SimConstants.TICKS_PER_SIM_DAY);

            Assert.Equal(14400, ticks.Count);
            Assert.Equal(0UL, ticks[0]);
            Assert.Equal(14399UL, ticks[ticks.Count - 1]);
            Assert.Equal(new uint[] { 0 }, days);
        }

        [Fact]
        public void test_host_headless_day_with_probe_fixture_runs_to_completion()
        {
            var log = new CapturingLog();
            var f = new RichFixture(log);

            f.Host.Step(14400);

            Assert.Equal(14400UL, f.Host.CurrentTick);
            Assert.Equal(24, f.Sink.Recorded.Count);
            Assert.All(f.Sink.Recorded, c => Assert.Equal(3, c.SystemHashes.Length));
            Assert.Equal(14400, f.Source.TickCalls);
            Assert.Equal(14400, f.Relay.TickCalls);
            Assert.Equal(14400, f.Observer.TickCalls);

            int pongTicks = Enumerable.Range(0, 14400).Count(t => t % 7 == 3);
            Assert.Equal(14400, f.Trace.Count(s => s.StartsWith("relay.ping ", StringComparison.Ordinal)));
            Assert.Equal(14400, f.Trace.Count(s => s.StartsWith("observer.ping ", StringComparison.Ordinal)));
            Assert.Equal(pongTicks, f.Trace.Count(s => s.StartsWith("observer.pong ", StringComparison.Ordinal)));

            // Tick 3: the Ping is the only phase-2 event (sequence 0, source 1);
            // the relay's Pong is published in pass 1 (sequence 1, source 4, cause 3.0).
            Assert.Contains("relay.ping t=3 id=3.0 src=1 cause=- v=3", f.Trace);
            Assert.Contains("observer.pong t=3 id=3.1 src=4 cause=3.0 v=30", f.Trace);

            Assert.Equal(144 + pongTicks, log.Lines.Count);

            // At the tick-13 800 checkpoint: owner 1 allocated on every tick
            // divisible by 50 (277 times), owner 7 once per Pong so far.
            ulong pongsByLastCheckpoint = (ulong)Enumerable.Range(0, 13801).Count(t => t % 7 == 3);
            Assert.Equal(FnvOracle.CoreHashT001((1, 277UL), (7, pongsByLastCheckpoint)), f.Sink.Recorded[23].CoreHash);
            Assert.Equal(f.Sink.Recorded[23].WorldHash, FnvOracle.WorldHash(
                13801UL,
                f.Sink.Recorded[23].CoreHash,
                f.Sink.Recorded[23].SystemHashes));
        }

        [Fact]
        public void test_host_try_submit_unknown_kind_is_rejected_without_effect()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink);
            host.Step(1);
            ulong before = host.WorldStateHash();

            var cmd = new Command(5UL, new PlayerId(0), (CommandKind)0x7FFF, Array.Empty<byte>());
            bool accepted = host.TrySubmit(cmd, out CommandRejection reason);

            Assert.False(accepted);
            Assert.Equal(CommandRejection.UnknownKind, reason);
            Assert.Equal(1UL, host.CurrentTick);
            Assert.Equal(before, host.WorldStateHash());
        }

        [Fact]
        public void test_host_world_state_hash_feeds_ticks_executed_and_changes_with_tick()
        {
            ISimHost host = Harness.Build(new RecordingCheckpointSink());
            ulong core = FnvOracle.CoreHashT001();

            Assert.Equal(FnvOracle.WorldHash(0UL, core), host.WorldStateHash());
            host.Step(1);
            ulong h1 = host.WorldStateHash();
            host.Step(1);
            ulong h2 = host.WorldStateHash();

            Assert.Equal(FnvOracle.WorldHash(1UL, core), h1);
            Assert.Equal(FnvOracle.WorldHash(2UL, core), h2);
            Assert.NotEqual(h1, h2);
        }

        [Fact]
        public void test_host_world_state_hash_is_a_pure_read()
        {
            var probe = new ProbeSystem(4) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick) };
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink, probe);
            host.Step(10);

            ulong a = host.WorldStateHash();
            ulong b = host.WorldStateHash();

            Assert.Equal(a, b);
            Assert.Equal(10UL, host.CurrentTick);
            Assert.Equal(10, probe.TickCalls);
            Assert.Equal(FnvOracle.WorldHash(10UL, FnvOracle.CoreHashT001(), probe.ComputeStateHash()), a);
        }
    }
}
