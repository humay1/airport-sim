using System;
using System.Diagnostics;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// sim.core's budget, 0.25 ms/tick (03-module-map.md "Performance", task
    /// T-001), measured per 07 L11 with Stopwatch timestamps in long
    /// arithmetic. 07 "Performance": allocation in the per-tick hot path is a
    /// rejection criterion. Checkpoint ticks are excluded from the allocation
    /// check, because 08 §8.9 requires a fresh SystemHashes array there.
    /// </summary>
    public sealed class BudgetTests
    {
        private const long BudgetMicrosPerTick = 250;

        private static long ElapsedMicros(long start, long end)
        {
            return (end - start) * 1_000_000L / Stopwatch.Frequency;
        }

        /// <summary>
        /// Three probes that publish, cascade, dispatch and allocate ids every
        /// tick without allocating themselves.
        /// </summary>
        private static ISimHost BuildBusyHost(ICheckpointSink sink)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(Harness.Config(sink, new NullLog()));
            IIdAllocator ids = b.Services.Ids;
            var source = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    for (int i = 0; i < 8; i++)
                    {
                        ctx.Events.Publish(new Ping(i), EventRef.None);
                    }

                    self.Mix(ids.Next(self.Id).Value);
                },
            };
            var relay = new ProbeSystem(5);
            var observer = new ProbeSystem(9);
            b.Services.Events.Subscribe<Ping>(relay.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                relay.Mix(env.Id.Sequence);
                if (evt.Value == 0)
                {
                    ctx.Events.Publish(new Pong(1), new EventRef(env.Id, true));
                }
            });
            b.Services.Events.Subscribe<Ping>(observer.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) => observer.Mix((ulong)evt.Value));
            b.Services.Events.Subscribe<Pong>(observer.Id, (in EventEnvelope env, in Pong evt, in TickContext ctx) => observer.Mix(env.Cause.Id.Sequence));
            b.Register(source);
            b.Register(relay);
            b.Register(observer);
            return b.Build();
        }

        private static long AllocatedDuringNonCheckpointTicks(ISimHost host)
        {
            // Warm up through two checkpoints so every queue and cache reaches
            // its steady-state size, then measure ticks 601..1199.
            host.Step(600);
            host.Step(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            host.Step(599);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(1200UL, host.CurrentTick);
            return after - before;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_step_with_no_systems_allocates_nothing()
        {
            ISimHost host = Harness.Build(new CountingCheckpointSink());
            Assert.Equal(0L, AllocatedDuringNonCheckpointTicks(host));
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_step_with_events_and_ids_allocates_nothing_in_steady_state()
        {
            ISimHost host = BuildBusyHost(new CountingCheckpointSink());
            Assert.Equal(0L, AllocatedDuringNonCheckpointTicks(host));
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_day_with_no_systems_within_quarter_ms_per_tick()
        {
            Harness.Build(new CountingCheckpointSink()).Step(14400);

            var sink = new CountingCheckpointSink();
            ISimHost host = Harness.Build(sink);
            long start = Stopwatch.GetTimestamp();
            host.Step(14400);
            long end = Stopwatch.GetTimestamp();

            Assert.Equal(24, sink.Count);
            long micros = ElapsedMicros(start, end);
            Assert.True(micros <= 14400L * BudgetMicrosPerTick, $"empty day took {micros} us, budget {14400L * BudgetMicrosPerTick} us");
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_budget_day_with_busy_probes_within_quarter_ms_per_tick()
        {
            BuildBusyHost(new CountingCheckpointSink()).Step(14400);

            var sink = new CountingCheckpointSink();
            ISimHost host = BuildBusyHost(sink);
            long start = Stopwatch.GetTimestamp();
            host.Step(14400);
            long end = Stopwatch.GetTimestamp();

            Assert.Equal(24, sink.Count);
            long micros = ElapsedMicros(start, end);
            Assert.True(micros <= 14400L * BudgetMicrosPerTick, $"busy day took {micros} us, budget {14400L * BudgetMicrosPerTick} us");
        }
    }
}
