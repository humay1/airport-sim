using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// The event bus, 08 §8.6 as amended by Q-014 (A4), with the envelope of
    /// 10 §10.2: Publish queues and fills Id/Tick/Source/Cause, FIFO by
    /// Sequence (restarting each tick), cascade passes, handlers in registry
    /// order of the subscriber, the 8-pass and 4096-publish invariants.
    /// </summary>
    public sealed class BusTests
    {
        private static SimInvariantException StepExpectingWrap(ISimHost host, uint ticks)
        {
            return Assert.Throws<SimInvariantException>(() => host.Step(ticks));
        }

        [Fact]
        public void test_bus_publish_outside_a_tick_throws_invalid_operation()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(1), EventRef.None));

            b.Register(new ProbeSystem(1));
            ISimHost host = b.Build();
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(1), EventRef.None));
            host.Step(1);
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping(1), EventRef.None));
        }

        [Fact]
        public void test_bus_publish_returns_tick_and_sequence_restarting_each_tick()
        {
            var ids = new List<EventId>();
            var probe = new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        ids.Add(ctx.Events.Publish(new Ping(i), EventRef.None));
                    }
                },
            };
            Harness.Build(new RecordingCheckpointSink(), probe).Step(2);

            Assert.Equal(
                new[]
                {
                    new EventId(0, 0), new EventId(0, 1), new EventId(0, 2),
                    new EventId(1, 0), new EventId(1, 1), new EventId(1, 2),
                },
                ids);
        }

        [Fact]
        public void test_bus_envelope_carries_id_tick_source_and_cause()
        {
            var published = new List<EventId>();
            var received = new List<string>();
            var expected = new List<string>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(5), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
                received.Add(Harness.DescribeEnvelope("ping", env, evt.Value)));
            b.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    EventId root = ctx.Events.Publish(new Ping(10), EventRef.None);
                    EventId child = ctx.Events.Publish(new Ping(11), new EventRef(root, true));
                    published.Add(root);
                    published.Add(child);
                },
            });
            b.Register(new ProbeSystem(5));
            ISimHost host = b.Build();

            host.Step(1);
            host.Step(1);

            Assert.Equal(new[] { new EventId(0, 0), new EventId(0, 1), new EventId(1, 0), new EventId(1, 1) }, published);
            Assert.Equal(
                new[]
                {
                    "ping t=0 id=0.0 src=2 cause=- v=10",
                    "ping t=0 id=0.1 src=2 cause=0.0 v=11",
                    "ping t=1 id=1.0 src=2 cause=- v=10",
                    "ping t=1 id=1.1 src=2 cause=1.0 v=11",
                },
                received);
        }

        [Fact]
        public void test_bus_sequence_is_shared_across_publishers_and_dispatched_fifo()
        {
            var received = new List<string>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(12), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
                received.Add($"{env.Id.Sequence}:{env.Source.Value}:{evt.Value}"));
            b.Services.Events.Subscribe<Pong>(new SystemId(12), (in EventEnvelope env, in Pong evt, in TickContext ctx) =>
                received.Add($"{env.Id.Sequence}:{env.Source.Value}:pong{evt.Value}"));
            TickAction twoEach = (ProbeSystem self, in TickContext ctx) =>
            {
                ctx.Events.Publish(new Ping(self.Id.Value * 10), EventRef.None);
                ctx.Events.Publish(new Pong(self.Id.Value * 10 + 1), EventRef.None);
            };
            b.Register(new ProbeSystem(1) { OnTick = twoEach });
            b.Register(new ProbeSystem(3) { OnTick = twoEach });
            b.Register(new ProbeSystem(12));
            b.Build().Step(1);

            Assert.Equal(new[] { "0:1:10", "1:1:pong11", "2:3:30", "3:3:pong31" }, received);
        }

        [Fact]
        public void test_bus_handlers_run_in_registry_order_not_subscription_order()
        {
            var order = new List<string>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            foreach (ushort sub in new ushort[] { 9, 2, 13, 5 })
            {
                ushort s = sub;
                bus.Subscribe<Ping>(new SystemId(s), (in EventEnvelope env, in Ping evt, in TickContext ctx) => order.Add($"{evt.Value}@{s}"));
            }

            b.Register(new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ctx.Events.Publish(new Ping(1), EventRef.None);
                    ctx.Events.Publish(new Ping(2), EventRef.None);
                },
            });
            b.Register(new ProbeSystem(2));
            b.Register(new ProbeSystem(5));
            b.Register(new ProbeSystem(9));
            b.Register(new ProbeSystem(13));
            b.Build().Step(1);

            Assert.Equal(new[] { "1@2", "1@5", "1@9", "1@13", "2@2", "2@5", "2@9", "2@13" }, order);
        }

        [Fact]
        public void test_bus_only_subscribers_of_the_type_receive_it()
        {
            int pings = 0;
            int pongs = 0;
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(3), (in EventEnvelope env, in Ping evt, in TickContext ctx) => pings++);
            b.Services.Events.Subscribe<Pong>(new SystemId(4), (in EventEnvelope env, in Pong evt, in TickContext ctx) => pongs++);
            b.Register(new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ctx.Events.Publish(new Ping(0), EventRef.None);
                    ctx.Events.Publish(new Ping(0), EventRef.None);
                    ctx.Events.Publish(new Pong(0), EventRef.None);
                },
            });
            b.Register(new ProbeSystem(3));
            b.Register(new ProbeSystem(4));
            b.Build().Step(5);

            Assert.Equal(10, pings);
            Assert.Equal(5, pongs);
        }

        [Fact]
        public void test_bus_handler_publish_has_subscriber_as_source_and_dispatches_same_tick()
        {
            var received = new List<string>();
            var handlerTicks = new List<ulong>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            bus.Subscribe<Ping>(new SystemId(6), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                handlerTicks.Add(ctx.Tick);
                ctx.Events.Publish(new Pong(evt.Value + 1), new EventRef(env.Id, true));
            });
            bus.Subscribe<Pong>(new SystemId(10), (in EventEnvelope env, in Pong evt, in TickContext ctx) =>
            {
                handlerTicks.Add(ctx.Tick);
                received.Add(Harness.DescribeEnvelope("pong", env, evt.Value));
            });
            b.Register(new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == 4)
                    {
                        ctx.Events.Publish(new Ping(40), EventRef.None);
                    }
                },
            });
            b.Register(new ProbeSystem(6));
            b.Register(new ProbeSystem(10));
            b.Build().Step(6);

            Assert.Equal(new[] { "pong t=4 id=4.1 src=6 cause=4.0 v=41" }, received);
            Assert.Equal(new ulong[] { 4, 4 }, handlerTicks);
        }

        [Fact]
        public void test_bus_cascade_dispatches_pass_by_pass()
        {
            // Phase 2 publishes A and B (pass 1). A's handler publishes C and
            // B's publishes D (pass 2). C's handler publishes E (pass 3).
            var order = new List<string>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(7), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                order.Add($"{(char)evt.Value}={env.Id.Sequence}");
                char next = evt.Value == 'A' ? 'C' : evt.Value == 'B' ? 'D' : evt.Value == 'C' ? 'E' : '\0';
                if (next != '\0')
                {
                    ctx.Events.Publish(new Ping(next), new EventRef(env.Id, true));
                }
            });
            b.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ctx.Events.Publish(new Ping('A'), EventRef.None);
                    ctx.Events.Publish(new Ping('B'), EventRef.None);
                },
            });
            b.Register(new ProbeSystem(7));
            b.Build().Step(1);

            Assert.Equal(new[] { "A=0", "B=1", "C=2", "D=3", "E=4" }, order);
        }

        private static ISimHost BuildChain(int depth, ulong startTick, List<int> handled)
        {
            // Each handled Ping(v) republishes Ping(v + 1) while v < depth, so
            // Ping(v) is dispatched in pass v.
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(3), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                handled.Add(evt.Value);
                if (evt.Value < depth)
                {
                    ctx.Events.Publish(new Ping(evt.Value + 1), new EventRef(env.Id, true));
                }
            });
            b.Register(new ProbeSystem(3)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == startTick)
                    {
                        ctx.Events.Publish(new Ping(1), EventRef.None);
                    }
                },
            });
            return b.Build();
        }

        [Fact]
        public void test_bus_cascade_of_max_passes_completes()
        {
            var handled = new List<int>();
            ISimHost host = BuildChain(SimConstants.MAX_EVENT_CASCADE_PASSES, 2, handled);

            host.Step(5);

            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, handled);
            Assert.Equal(5UL, host.CurrentTick);
        }

        [Fact]
        public void test_bus_publish_during_last_pass_throws_wrapped_invariant()
        {
            var handled = new List<int>();
            ISimHost host = BuildChain(SimConstants.MAX_EVENT_CASCADE_PASSES + 1, 2, handled);

            SimInvariantException outer = StepExpectingWrap(host, 5);

            Assert.Equal(2UL, outer.Tick);
            Assert.True(outer.HasWorldHash);
            SimInvariantException inner = Assert.IsType<SimInvariantException>(outer.InnerException);
            Assert.Equal(2UL, inner.Tick);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, handled);
        }

        [Fact]
        public void test_bus_max_events_per_tick_is_allowed_every_tick()
        {
            int handled = 0;
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(9), (in EventEnvelope env, in Ping evt, in TickContext ctx) => handled++);
            b.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    for (int i = 0; i < SimConstants.MAX_EVENTS_PER_TICK; i++)
                    {
                        ctx.Events.Publish(new Ping(i), EventRef.None);
                    }
                },
            });
            b.Register(new ProbeSystem(9));
            ISimHost host = b.Build();

            host.Step(3);

            Assert.Equal(3 * 4096, handled);
            Assert.Equal(3UL, host.CurrentTick);
        }

        [Fact]
        public void test_bus_publish_number_4097_in_a_tick_throws_invariant()
        {
            int thrownAt = -1;
            Exception? thrown = null;
            var probe = new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    int count = ctx.Tick == 1 ? 4097 : 10;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            ctx.Events.Publish(new Ping(i), EventRef.None);
                        }
                        catch (Exception e)
                        {
                            thrownAt = i;
                            thrown = e;
                            throw;
                        }
                    }
                },
            };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            SimInvariantException outer = StepExpectingWrap(host, 3);

            Assert.Equal(4096, thrownAt);
            SimInvariantException limit = Assert.IsType<SimInvariantException>(thrown);
            Assert.Equal(1UL, limit.Tick);
            Assert.Same(limit, outer.InnerException);
            Assert.Equal(1UL, outer.Tick);
        }

        [Theory]
        [InlineData(96, false)]
        [InlineData(97, true)]
        public void test_bus_event_limit_counts_publishes_from_every_phase(int fromHandler, bool expectThrow)
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(4), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                if (env.Id.Sequence == 0)
                {
                    for (int i = 0; i < fromHandler; i++)
                    {
                        ctx.Events.Publish(new Pong(i), new EventRef(env.Id, true));
                    }
                }
            });
            b.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    for (int i = 0; i < 4000; i++)
                    {
                        ctx.Events.Publish(new Ping(i), EventRef.None);
                    }
                },
            });
            b.Register(new ProbeSystem(4));
            ISimHost host = b.Build();

            if (expectThrow)
            {
                SimInvariantException outer = StepExpectingWrap(host, 2);
                Assert.Equal(0UL, outer.Tick);
                Assert.IsType<SimInvariantException>(outer.InnerException);
            }
            else
            {
                host.Step(2);
                Assert.Equal(2UL, host.CurrentTick);
            }
        }

        [Fact]
        public void test_bus_events_are_not_redelivered_on_later_ticks()
        {
            var seen = new List<ulong>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(9), (in EventEnvelope env, in Ping evt, in TickContext ctx) => seen.Add(env.Tick));
            b.Register(new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == 0)
                    {
                        ctx.Events.Publish(new Ping(0), EventRef.None);
                    }
                },
            });
            b.Register(new ProbeSystem(9));
            b.Build().Step(4);

            Assert.Equal(new ulong[] { 0 }, seen);
        }
    }
}
