using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// 08 §8.11a construction and the §8.5 registration rules (Q-014 A2, A7),
    /// plus §8.6's Subscribe rules. Exceptions from calls made outside a tick
    /// are not wrapped (§8.5a), so each asserts the exact BCL type named by
    /// 07 "Error handling".
    /// </summary>
    public sealed class BuilderTests
    {
        private static readonly SimEventHandler<Ping> NoopPing = (in EventEnvelope env, in Ping evt, in TickContext ctx) => { };

        [Fact]
        public void test_builder_create_with_null_config_member_throws_argument_null()
        {
            var sink = new RecordingCheckpointSink();
            var log = new NullLog();
            var content = new EmptyContentIndex();

            Assert.Throws<ArgumentNullException>(() => SimHostFactory.CreateBuilder(new SimHostConfig(1UL, null!, sink, log)));
            Assert.Throws<ArgumentNullException>(() => SimHostFactory.CreateBuilder(new SimHostConfig(1UL, content, null!, log)));
            Assert.Throws<ArgumentNullException>(() => SimHostFactory.CreateBuilder(new SimHostConfig(1UL, content, sink, null!)));
            Assert.Throws<ArgumentNullException>(() => SimHostFactory.CreateBuilder(default(SimHostConfig)));
        }

        [Fact]
        public void test_builder_services_are_available_before_build()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            SystemServices s = b.Services;
            Assert.NotNull(s.Events);
            Assert.NotNull(s.Ids);
            Assert.NotNull(s.Content);
            Assert.NotNull(s.Commands);
        }

        [Fact]
        public void test_builder_register_null_throws_argument_null()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            Assert.Throws<ArgumentNullException>(() => b.Register(null!));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(65535)]
        public void test_builder_register_illegal_id_throws_argument_exception(int id)
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            Assert.Throws<ArgumentException>(() => b.Register(new ProbeSystem((ushort)id)));
        }

        [Fact]
        public void test_builder_register_every_legal_position_ascending_succeeds()
        {
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder b = Harness.Builder(sink);
            var expected = new ulong[13];
            int n = 0;
            for (ushort id = 1; id <= 14; id++)
            {
                if (id == 8)
                {
                    continue;
                }

                ulong h = 0xC0DE_0000UL + id;
                b.Register(new ProbeSystem(id) { HashOverride = () => h });
                expected[n++] = h;
            }

            ISimHost host = b.Build();
            host.Step(1);

            Checkpoint cp = Assert.Single(sink.Recorded);
            Assert.Equal(expected, cp.SystemHashes);
        }

        [Fact]
        public void test_builder_register_with_gaps_succeeds()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink, new ProbeSystem(2), new ProbeSystem(9), new ProbeSystem(14));
            host.Step(1);
            Assert.Equal(3, Assert.Single(sink.Recorded).SystemHashes.Length);
        }

        [Fact]
        public void test_builder_register_same_id_twice_throws_argument_exception()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Register(new ProbeSystem(3));
            Assert.Throws<ArgumentException>(() => b.Register(new ProbeSystem(3)));
        }

        [Fact]
        public void test_builder_register_descending_throws_argument_exception()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Register(new ProbeSystem(5));
            Assert.Throws<ArgumentException>(() => b.Register(new ProbeSystem(3)));
            Assert.Throws<ArgumentException>(() => b.Register(new ProbeSystem(1)));
        }

        [Fact]
        public void test_builder_register_does_not_check_name()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink, new ProbeSystem(1, string.Empty), new ProbeSystem(2, "dup"), new ProbeSystem(3, "dup"));
            host.Step(1);
            Assert.Equal(3, Assert.Single(sink.Recorded).SystemHashes.Length);
        }

        [Fact]
        public void test_builder_register_after_build_throws_invalid_operation()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Register(new ProbeSystem(1));
            b.Build();
            Assert.Throws<InvalidOperationException>(() => b.Register(new ProbeSystem(2)));
        }

        [Fact]
        public void test_builder_build_twice_throws_invalid_operation()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Build();
            Assert.Throws<InvalidOperationException>(() => b.Build());
        }

        [Fact]
        public void test_builder_build_returns_host_at_tick_zero_without_checkpoint()
        {
            var sink = new RecordingCheckpointSink();
            var probe = new ProbeSystem(1);
            ISimHost host = Harness.Build(sink, probe);
            Assert.Equal(0UL, host.CurrentTick);
            Assert.Empty(sink.Recorded);
            Assert.Equal(0, probe.TickCalls);
        }

        [Fact]
        public void test_builder_subscribe_after_build_throws_invalid_operation()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            b.Register(new ProbeSystem(2));
            b.Build();
            Assert.Throws<InvalidOperationException>(() => bus.Subscribe(new SystemId(2), NoopPing));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(15)]
        [InlineData(65535)]
        public void test_builder_subscribe_illegal_subscriber_throws_argument_exception(int id)
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            Assert.Throws<ArgumentException>(() => b.Services.Events.Subscribe(new SystemId((ushort)id), NoopPing));
        }

        [Fact]
        public void test_builder_subscribe_null_handler_throws_argument_null()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            Assert.Throws<ArgumentNullException>(() => b.Services.Events.Subscribe<Ping>(new SystemId(2), null!));
        }

        [Fact]
        public void test_builder_subscribe_second_handler_for_same_subscriber_and_type_throws_argument_exception()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            bus.Subscribe(new SystemId(2), NoopPing);
            Assert.Throws<ArgumentException>(() => bus.Subscribe(new SystemId(2), NoopPing));
            Assert.Throws<ArgumentException>(() => bus.Subscribe<Ping>(new SystemId(2), (in EventEnvelope e, in Ping p, in TickContext c) => { }));
        }

        [Fact]
        public void test_builder_subscribe_other_type_or_other_subscriber_is_allowed()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IEventBus bus = b.Services.Events;
            bus.Subscribe(new SystemId(2), NoopPing);
            bus.Subscribe<Pong>(new SystemId(2), (in EventEnvelope e, in Pong p, in TickContext c) => { });
            bus.Subscribe(new SystemId(3), NoopPing);
            b.Register(new ProbeSystem(2));
            b.Register(new ProbeSystem(3));
            ISimHost host = b.Build();
            host.Step(1);
            Assert.Equal(1UL, host.CurrentTick);
        }

        [Fact]
        public void test_builder_subscribe_before_register_is_allowed()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe(new SystemId(6), NoopPing);
            b.Register(new ProbeSystem(6));
            ISimHost host = b.Build();
            Assert.Equal(0UL, host.CurrentTick);
        }

        [Fact]
        public void test_builder_build_with_unregistered_subscriber_throws_invalid_operation()
        {
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            b.Services.Events.Subscribe(new SystemId(6), NoopPing);
            b.Register(new ProbeSystem(5));
            Assert.Throws<InvalidOperationException>(() => b.Build());
        }
    }
}
