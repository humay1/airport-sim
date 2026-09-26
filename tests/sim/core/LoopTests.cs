using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// The fixed phase order of 08 §8.5: system update, then event dispatch,
    /// then checkpoint. Phase 1 (command application) has no observable
    /// behaviour at T-001 (Q-014 A3). Q-017 H7 accepts a fixture in which the
    /// correct order and a swapped order give different hashes.
    /// </summary>
    public sealed class LoopTests
    {
        [Fact]
        public void test_loop_phases_run_update_then_dispatch_then_checkpoint()
        {
            var journal = new List<string>();
            var sink = new RecordingCheckpointSink { OnRecord = cp => journal.Add($"checkpoint:{cp.Tick}") };
            ISimHostBuilder b = Harness.Builder(sink);
            TickAction publish = (ProbeSystem self, in TickContext ctx) =>
            {
                journal.Add($"tick:{self.Id.Value}");
                ctx.Events.Publish(new Ping(self.Id.Value), EventRef.None);
            };
            b.Services.Events.Subscribe<Ping>(new SystemId(9), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
                journal.Add($"handle:9:{env.Source.Value}"));
            b.Register(new ProbeSystem(2) { OnTick = publish });
            b.Register(new ProbeSystem(6) { OnTick = publish });
            b.Register(new ProbeSystem(9));
            ISimHost host = b.Build();

            host.Step(2);

            Assert.Equal(
                new[]
                {
                    "tick:2", "tick:6", "handle:9:2", "handle:9:6", "checkpoint:0",
                    "tick:2", "tick:6", "handle:9:2", "handle:9:6",
                },
                journal);
        }

        [Fact]
        public void test_loop_publish_queues_and_never_calls_a_handler_synchronously()
        {
            int handled = 0;
            var handledDuringTick = new List<int>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe<Ping>(new SystemId(3), (in EventEnvelope env, in Ping evt, in TickContext ctx) => handled++);
            b.Register(new ProbeSystem(3)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ctx.Events.Publish(new Ping(1), EventRef.None);
                    handledDuringTick.Add(handled);
                },
            });
            ISimHost host = b.Build();

            host.Step(3);

            Assert.Equal(new[] { 0, 1, 2 }, handledDuringTick);
            Assert.Equal(3, handled);
        }

        [Fact]
        public void test_loop_checkpoint_hashes_state_after_dispatch_not_before()
        {
            // The probe mixes 1 during its update and 2 in its own handler. In
            // the spec's order the checkpoint sees Mix(1) then Mix(2); if the
            // checkpoint ran before dispatch, or dispatch before update, the
            // hash would differ.
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder b = Harness.Builder(sink);
            var probe = new ProbeSystem(3)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    self.Mix(1UL);
                    ctx.Events.Publish(new Ping(0), EventRef.None);
                },
            };
            b.Services.Events.Subscribe<Ping>(probe.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) => probe.Mix(2UL));
            b.Register(probe);
            ISimHost host = b.Build();

            host.Step(1);

            ulong correct = ProbeSystem.HashOf(3, ProbeSystem.MixStep(ProbeSystem.MixStep(0UL, 1UL), 2UL));
            ulong swapped = ProbeSystem.HashOf(3, ProbeSystem.MixStep(ProbeSystem.MixStep(0UL, 2UL), 1UL));
            ulong beforeDispatch = ProbeSystem.HashOf(3, ProbeSystem.MixStep(0UL, 1UL));
            Assert.NotEqual(correct, swapped);
            Assert.NotEqual(correct, beforeDispatch);

            Checkpoint cp = Assert.Single(sink.Recorded);
            Assert.Equal(new[] { correct }, cp.SystemHashes);
        }
    }
}
