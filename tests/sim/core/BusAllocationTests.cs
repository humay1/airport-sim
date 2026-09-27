using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// A payload type that the allocation tests publish for the first time
    /// late in a run, with or without a subscriber.
    /// </summary>
    internal readonly struct Late : ISimEvent
    {
        public Late(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    /// <summary>
    /// 08 §8.6 "Allocation" (Q-035): after Build the bus allocates nothing,
    /// from the first tick, whatever was published before and whatever the
    /// earlier per-tick peaks were. A type with no subscriber stores nothing,
    /// but its Publish still runs every check, consumes a Sequence and returns
    /// its EventId. Allocation is measured over ticks 601..1199, which hold no
    /// checkpoint (08 §8.9 allocates a fresh SystemHashes array there), as in
    /// BudgetTests. Before each measured host, a throwaway host runs the same
    /// code paths once, so JIT and type loading are not charged to the bus;
    /// bus state is per host, so this warms nothing the spec rules on.
    /// </summary>
    public sealed class BusAllocationTests
    {
        private sealed class Counters
        {
            public int Pings;
            public int Pongs;
            public int Lates;
            public int LatesPublished;
        }

        private static long AllocatedDuringTicks601To1199(ISimHost host)
        {
            host.Step(600);
            host.Step(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            host.Step(599);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(1200UL, host.CurrentTick);
            return after - before;
        }

        /// <summary>
        /// Eight Pings a tick, subscribed by probe 9. From <paramref name="lateTick"/>
        /// on, one Late a tick too, which probe 9 subscribes to only when
        /// <paramref name="subscribeLate"/> is set. No handler allocates.
        /// </summary>
        private static ISimHost BuildLateHost(ulong lateTick, bool subscribeLate, Counters c)
        {
            ISimHostBuilder b = Harness.Builder(new CountingCheckpointSink());
            IEventBus bus = b.Services.Events;
            var source = new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    for (int i = 0; i < 8; i++)
                    {
                        ctx.Events.Publish(new Ping(i), EventRef.None);
                    }

                    if (ctx.Tick >= lateTick)
                    {
                        ctx.Events.Publish(new Late(1), EventRef.None);
                        c.LatesPublished++;
                    }
                },
            };
            var sub = new ProbeSystem(9);
            bus.Subscribe<Ping>(sub.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) => c.Pings++);
            if (subscribeLate)
            {
                bus.Subscribe<Late>(sub.Id, (in EventEnvelope env, in Late evt, in TickContext ctx) => c.Lates++);
            }

            b.Register(source);
            b.Register(sub);
            return b.Build();
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_bus_first_publish_of_unsubscribed_type_after_warm_up_allocates_nothing()
        {
            BuildLateHost(1, false, new Counters()).Step(3);

            var c = new Counters();
            ISimHost host = BuildLateHost(900, false, c);

            Assert.Equal(0L, AllocatedDuringTicks601To1199(host));
            Assert.Equal(300, c.LatesPublished);
            Assert.Equal(0, c.Lates);
            Assert.Equal(1200 * 8, c.Pings);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_bus_first_publish_of_subscribed_type_after_warm_up_allocates_nothing()
        {
            BuildLateHost(1, true, new Counters()).Step(3);

            var c = new Counters();
            ISimHost host = BuildLateHost(900, true, c);

            Assert.Equal(0L, AllocatedDuringTicks601To1199(host));
            Assert.Equal(300, c.LatesPublished);
            Assert.Equal(300, c.Lates);
            Assert.Equal(1200 * 8, c.Pings);
        }

        /// <summary>
        /// Eight Pings a tick, except on four peak ticks of exactly
        /// MAX_EVENTS_PER_TICK events each: 4096 Pings from phase 2; 4096
        /// Pongs from phase 2; one Ping whose handler cascades 4095 Pongs;
        /// 2048 Pings and 2048 unsubscribed Lates. Pings and Pongs are
        /// subscribed by probe 9, whose handlers do not allocate.
        /// </summary>
        private static ISimHost BuildPeakHost(ulong first, Counters c)
        {
            int max = SimConstants.MAX_EVENTS_PER_TICK;
            ulong allPings = first;
            ulong allPongs = first + 100;
            ulong cascade = first + 200;
            ulong mixed = first + 250;

            ISimHostBuilder b = Harness.Builder(new CountingCheckpointSink());
            IEventBus bus = b.Services.Events;
            var source = new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == allPings)
                    {
                        for (int i = 0; i < max; i++)
                        {
                            ctx.Events.Publish(new Ping(i), EventRef.None);
                        }
                    }
                    else if (ctx.Tick == allPongs)
                    {
                        for (int i = 0; i < max; i++)
                        {
                            ctx.Events.Publish(new Pong(i), EventRef.None);
                        }
                    }
                    else if (ctx.Tick == cascade)
                    {
                        ctx.Events.Publish(new Ping(-1), EventRef.None);
                    }
                    else if (ctx.Tick == mixed)
                    {
                        for (int i = 0; i < max / 2; i++)
                        {
                            ctx.Events.Publish(new Ping(i), EventRef.None);
                            ctx.Events.Publish(new Late(i), EventRef.None);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            ctx.Events.Publish(new Ping(i), EventRef.None);
                        }
                    }
                },
            };
            var sub = new ProbeSystem(9);
            bus.Subscribe<Ping>(sub.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                c.Pings++;
                if (evt.Value == -1)
                {
                    for (int i = 0; i < max - 1; i++)
                    {
                        ctx.Events.Publish(new Pong(i), new EventRef(env.Id, true));
                    }
                }
            });
            bus.Subscribe<Pong>(sub.Id, (in EventEnvelope env, in Pong evt, in TickContext ctx) => c.Pongs++);
            b.Register(source);
            b.Register(sub);
            return b.Build();
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_bus_new_per_tick_peak_up_to_max_events_per_tick_allocates_nothing()
        {
            BuildPeakHost(1, new Counters()).Step(260);

            // Peaks at ticks 700, 800, 900 and 950, all inside the measured window.
            var c = new Counters();
            ISimHost host = BuildPeakHost(700, c);

            Assert.Equal(0L, AllocatedDuringTicks601To1199(host));
            Assert.Equal((1200 - 4) * 8 + 4096 + 1 + 2048, c.Pings);
            Assert.Equal(4096 + 4095, c.Pongs);
        }

        [Fact]
        public void test_bus_unsubscribed_publish_consumes_sequence_and_returns_event_id()
        {
            // Each tick, phase 2 publishes Ping(1), Late, Ping(2). The Ping(1)
            // handler publishes a Late caused by it, then a Pong caused by that
            // Late. Nothing subscribes to Late.
            var ids = new List<EventId>();
            var received = new List<string>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            bus.Subscribe<Ping>(new SystemId(9), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                received.Add(Harness.DescribeEnvelope("ping", env, evt.Value));
                if (evt.Value == 1)
                {
                    EventId late = ctx.Events.Publish(new Late(evt.Value), new EventRef(env.Id, true));
                    ids.Add(late);
                    ids.Add(ctx.Events.Publish(new Pong(evt.Value), new EventRef(late, true)));
                }
            });
            bus.Subscribe<Pong>(new SystemId(9), (in EventEnvelope env, in Pong evt, in TickContext ctx) =>
                received.Add(Harness.DescribeEnvelope("pong", env, evt.Value)));
            b.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ids.Add(ctx.Events.Publish(new Ping(1), EventRef.None));
                    ids.Add(ctx.Events.Publish(new Late(0), EventRef.None));
                    ids.Add(ctx.Events.Publish(new Ping(2), EventRef.None));
                },
            });
            b.Register(new ProbeSystem(9));
            ISimHost host = b.Build();

            host.Step(2);

            Assert.Equal(
                new[]
                {
                    new EventId(0, 0), new EventId(0, 1), new EventId(0, 2), new EventId(0, 3), new EventId(0, 4),
                    new EventId(1, 0), new EventId(1, 1), new EventId(1, 2), new EventId(1, 3), new EventId(1, 4),
                },
                ids);
            Assert.Equal(
                new[]
                {
                    "ping t=0 id=0.0 src=2 cause=- v=1",
                    "ping t=0 id=0.2 src=2 cause=- v=2",
                    "pong t=0 id=0.4 src=9 cause=0.3 v=1",
                    "ping t=1 id=1.0 src=2 cause=- v=1",
                    "ping t=1 id=1.2 src=2 cause=- v=2",
                    "pong t=1 id=1.4 src=9 cause=1.3 v=1",
                },
                received);
        }

        /// <summary>
        /// Ping(v) handled by probe 3 republishes Ping(v + 1) while v is below
        /// MAX_EVENT_CASCADE_PASSES, so Ping(v) is dispatched in pass v. The
        /// handler of Ping(<paramref name="lateInPass"/>) also publishes an
        /// unsubscribed Late.
        /// </summary>
        private static ISimHost BuildChainWithLate(int lateInPass, List<int> handled)
        {
            int depth = SimConstants.MAX_EVENT_CASCADE_PASSES;
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(3), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                handled.Add(evt.Value);
                if (evt.Value < depth)
                {
                    ctx.Events.Publish(new Ping(evt.Value + 1), new EventRef(env.Id, true));
                }

                if (evt.Value == lateInPass)
                {
                    ctx.Events.Publish(new Late(evt.Value), new EventRef(env.Id, true));
                }
            });
            b.Register(new ProbeSystem(3)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == 2)
                    {
                        ctx.Events.Publish(new Ping(1), EventRef.None);
                    }
                },
            });
            return b.Build();
        }

        [Fact]
        public void test_bus_unsubscribed_publish_still_enforces_phase_cascade_and_limit()
        {
            // Phase: outside phases 1-3 an unsubscribed Publish throws, including
            // from the checkpoint sink in phase 4.
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder pb = Harness.Builder(sink);
            IEventBus bus = pb.Services.Events;
            var phase4 = new List<Type?>();
            sink.OnRecord = cp =>
            {
                try
                {
                    bus.Publish(new Late(4), EventRef.None);
                    phase4.Add(null);
                }
                catch (Exception e)
                {
                    phase4.Add(e.GetType());
                }
            };
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Late(0), EventRef.None));
            pb.Register(new ProbeSystem(1));
            ISimHost phaseHost = pb.Build();
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Late(0), EventRef.None));
            phaseHost.Step(1);
            Assert.Throws<InvalidOperationException>(() => bus.Publish(new Late(0), EventRef.None));
            Assert.Equal(new Type?[] { typeof(InvalidOperationException) }, phase4);

            // Cascade: a Late published during pass 7 is allowed; during the
            // last pass it is a broken invariant, wrapped by the host.
            var handled7 = new List<int>();
            ISimHost ok = BuildChainWithLate(SimConstants.MAX_EVENT_CASCADE_PASSES - 1, handled7);
            ok.Step(5);
            Assert.Equal(5UL, ok.CurrentTick);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, handled7);

            var handled8 = new List<int>();
            ISimHost bad = BuildChainWithLate(SimConstants.MAX_EVENT_CASCADE_PASSES, handled8);
            SimInvariantException cascadeOuter = Assert.Throws<SimInvariantException>(() => bad.Step(5));
            Assert.Equal(2UL, cascadeOuter.Tick);
            Assert.True(cascadeOuter.HasWorldHash);
            SimInvariantException cascadeInner = Assert.IsType<SimInvariantException>(cascadeOuter.InnerException);
            Assert.Equal(2UL, cascadeInner.Tick);
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, handled8);

            // Limit: unsubscribed publishes count toward MAX_EVENTS_PER_TICK.
            // Tick 0 carries 2048 Pings and 2048 Lates. Tick 1 carries 4096
            // Lates, and the 4097th publish, another Late, throws.
            int max = SimConstants.MAX_EVENTS_PER_TICK;
            int pings = 0;
            int thrownAt = -1;
            Exception? thrown = null;
            ISimHostBuilder lb = Harness.Builder(new RecordingCheckpointSink());
            lb.Services.Events.Subscribe<Ping>(new SystemId(9), (in EventEnvelope env, in Ping evt, in TickContext ctx) => pings++);
            lb.Register(new ProbeSystem(2)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == 0)
                    {
                        for (int i = 0; i < max / 2; i++)
                        {
                            ctx.Events.Publish(new Ping(i), EventRef.None);
                            ctx.Events.Publish(new Late(i), EventRef.None);
                        }

                        return;
                    }

                    for (int i = 0; i <= max; i++)
                    {
                        try
                        {
                            ctx.Events.Publish(new Late(i), EventRef.None);
                        }
                        catch (Exception e)
                        {
                            thrownAt = i;
                            thrown = e;
                            throw;
                        }
                    }
                },
            });
            lb.Register(new ProbeSystem(9));
            ISimHost limitHost = lb.Build();

            SimInvariantException limitOuter = Assert.Throws<SimInvariantException>(() => limitHost.Step(3));

            Assert.Equal(max / 2, pings);
            Assert.Equal(max, thrownAt);
            SimInvariantException limit = Assert.IsType<SimInvariantException>(thrown);
            Assert.Equal(1UL, limit.Tick);
            Assert.Same(limit, limitOuter.InnerException);
            Assert.Equal(1UL, limitOuter.Tick);
        }
    }
}
