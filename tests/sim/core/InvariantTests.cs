using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Host exception wrapping, 08 §8.5a (Q-014 A9) and 07 "Error handling":
    /// any exception escaping phases 1-4 of tick t leaves Step as a new
    /// SimInvariantException with Tick = t, InnerException = the escaping
    /// exception, and the world hash when it can be computed. Exactly one
    /// wrap. Afterwards Step, TrySubmit and WorldStateHash throw
    /// InvalidOperationException. Q-024: the wrapped WorldHash feeds t (ticks
    /// completed, not t + 1), CurrentTick stays t, and the partial state of
    /// tick t is hashed as it stands, with nothing rolled back.
    /// </summary>
    public sealed class InvariantTests
    {
        /// <summary>State of a probe that has run Mix(tick) for ticks 0..last.</summary>
        private static ulong MixedThrough(ulong last)
        {
            ulong state = 0UL;
            for (ulong t = 0; t <= last; t++)
            {
                state = ProbeSystem.MixStep(state, t);
            }

            return state;
        }

        private static ProbeSystem Mixing(ushort id)
        {
            return new ProbeSystem(id) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick) };
        }

        private static ProbeSystem ThrowingAt(ushort id, ulong tick, Exception e)
        {
            return new ProbeSystem(id)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    self.Mix(ctx.Tick);
                    if (ctx.Tick == tick)
                    {
                        throw e;
                    }
                },
            };
        }

        [Fact]
        public void test_invariant_exception_in_system_update_is_wrapped_with_tick_and_hash()
        {
            var boom = new InvalidOperationException("probe failure");
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), Mixing(2), ThrowingAt(4, 5, boom), Mixing(7));

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(10));

            Assert.Equal(5UL, e.Tick);
            Assert.Same(boom, e.InnerException);
            Assert.True(e.HasWorldHash);
            Assert.Equal(5UL, host.CurrentTick);

            // Tick 5 is partial: systems 2 and 4 ran it (4 mixed, then threw),
            // system 7 never did. Ticks completed = 5.
            ulong expected = FnvOracle.WorldHash(
                5UL,
                FnvOracle.CoreHashT001(),
                ProbeSystem.HashOf(2, MixedThrough(5)),
                ProbeSystem.HashOf(4, MixedThrough(5)),
                ProbeSystem.HashOf(7, MixedThrough(4)));
            Assert.Equal(expected, e.WorldHash);
        }

        [Fact]
        public void test_invariant_tick_is_the_failing_tick_inside_a_long_step()
        {
            var boom = new ArithmeticException("late failure");
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), ThrowingAt(1, 1234, boom));
            host.Step(1000);

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(14400));

            Assert.Equal(1234UL, e.Tick);
            Assert.Same(boom, e.InnerException);
            Assert.Equal(1234UL, host.CurrentTick);
            Assert.Equal(FnvOracle.WorldHash(1234UL, FnvOracle.CoreHashT001(), ProbeSystem.HashOf(1, MixedThrough(1234))), e.WorldHash);
        }

        [Fact]
        public void test_invariant_module_invariant_exception_is_wrapped_exactly_once()
        {
            var own = new SimInvariantException("module invariant", 3UL);
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), ThrowingAt(5, 3, own));

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(4));

            Assert.NotSame(own, e);
            Assert.Same(own, e.InnerException);
            Assert.Null(own.InnerException);
            Assert.Equal(3UL, e.Tick);
            Assert.True(e.HasWorldHash);
            Assert.False(own.HasWorldHash);
            Assert.Equal(3UL, host.CurrentTick);
            Assert.Equal(FnvOracle.WorldHash(3UL, FnvOracle.CoreHashT001(), ProbeSystem.HashOf(5, MixedThrough(3))), e.WorldHash);
        }

        [Fact]
        public void test_invariant_exception_in_handler_is_wrapped()
        {
            var boom = new FormatException("handler failure");
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            var handlerProbe = new ProbeSystem(11);
            b.Services.Events.Subscribe<Ping>(new SystemId(11), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                handlerProbe.Mix(ctx.Tick);
                if (ctx.Tick == 7)
                {
                    throw boom;
                }
            });
            b.Register(new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    self.Mix(ctx.Tick);
                    ctx.Events.Publish(new Ping(0), EventRef.None);
                },
            });
            b.Register(handlerProbe);
            ISimHost host = b.Build();

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(20));

            Assert.Equal(7UL, e.Tick);
            Assert.Same(boom, e.InnerException);
            Assert.True(e.HasWorldHash);
            Assert.Equal(7UL, host.CurrentTick);

            // Phase 2 and the handler's mix of tick 7 both happened; nothing is rolled back.
            ulong expected = FnvOracle.WorldHash(
                7UL,
                FnvOracle.CoreHashT001(),
                ProbeSystem.HashOf(1, MixedThrough(7)),
                ProbeSystem.HashOf(11, MixedThrough(7)));
            Assert.Equal(expected, e.WorldHash);
        }

        [Fact]
        public void test_invariant_exception_in_checkpoint_sink_is_wrapped()
        {
            var boom = new InvalidOperationException("sink failure");
            var sink = new RecordingCheckpointSink
            {
                OnRecord = cp =>
                {
                    if (cp.Tick == 600)
                    {
                        throw boom;
                    }
                },
            };
            ISimHost host = Harness.Build(sink, Mixing(3));

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(1000));

            Assert.Equal(600UL, e.Tick);
            Assert.Same(boom, e.InnerException);
            Assert.True(e.HasWorldHash);
            Assert.Equal(600UL, host.CurrentTick);

            // Phases 1-3 of tick 600 completed, but the tick did not: 600 is fed, not 601.
            Assert.Equal(FnvOracle.WorldHash(600UL, FnvOracle.CoreHashT001(), ProbeSystem.HashOf(3, MixedThrough(600))), e.WorldHash);
        }

        [Fact]
        public void test_invariant_unhashable_state_leaves_no_world_hash()
        {
            var boom = new NotSupportedException("hash failure");
            var probe = new ProbeSystem(6) { HashOverride = () => throw boom };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            SimInvariantException e = Assert.Throws<SimInvariantException>(() => host.Step(1));

            Assert.Equal(0UL, e.Tick);
            Assert.Same(boom, e.InnerException);
            Assert.False(e.HasWorldHash);
        }

        [Fact]
        public void test_invariant_host_is_unusable_after_a_wrapped_failure()
        {
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), ThrowingAt(2, 0, new InvalidOperationException("x")));
            Assert.Throws<SimInvariantException>(() => host.Step(1));

            var cmd = new Command(10UL, new PlayerId(0), CommandKind.NoOp, Array.Empty<byte>());
            Assert.Throws<InvalidOperationException>(() => host.Step(1));
            Assert.Throws<InvalidOperationException>(() => host.WorldStateHash());
            Assert.Throws<InvalidOperationException>(() => host.TrySubmit(cmd, out CommandRejection _));
        }
    }
}
