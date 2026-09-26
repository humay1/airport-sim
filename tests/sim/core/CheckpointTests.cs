using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Phase 4 and the world hash, 08 §8.9 "Tick fed, cadence and contents"
    /// (Q-014) and "Encoding, the concrete hasher and the core section"
    /// (Q-017). At T-001 scope the core section is sequence 1, no pending
    /// commands, plus any non-zero id counters.
    /// </summary>
    public sealed class CheckpointTests
    {
        [Fact]
        public void test_checkpoint_none_recorded_at_build()
        {
            var sink = new RecordingCheckpointSink();
            Harness.Build(sink, new ProbeSystem(1));
            Assert.Empty(sink.Recorded);
        }

        [Fact]
        public void test_checkpoint_recorded_on_ticks_divisible_by_600()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink);

            host.Step(1);
            Assert.Equal(new[] { 0UL }, sink.Recorded.Select(c => c.Tick));

            host.Step(599);
            Assert.Single(sink.Recorded);

            host.Step(1);
            Assert.Equal(new[] { 0UL, 600UL }, sink.Recorded.Select(c => c.Tick));
        }

        [Fact]
        public void test_checkpoint_two_days_record_48_at_hour_boundaries()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink);

            host.Step(28800);

            Assert.Equal(Enumerable.Range(0, 48).Select(i => (ulong)i * 600UL), sink.Recorded.Select(c => c.Tick));
        }

        [Fact]
        public void test_checkpoint_world_hash_equals_world_state_hash_after_its_step()
        {
            var sink = new RecordingCheckpointSink();
            var probe = new ProbeSystem(5) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick * 3UL + 1UL) };
            ISimHost host = Harness.Build(sink, probe);

            host.Step(1);
            Assert.Equal(host.WorldStateHash(), sink.Recorded[0].WorldHash);

            host.Step(599);
            host.Step(1);
            Assert.Equal(2, sink.Recorded.Count);
            Assert.Equal(host.WorldStateHash(), sink.Recorded[1].WorldHash);
            Assert.NotEqual(sink.Recorded[0].WorldHash, sink.Recorded[1].WorldHash);
        }

        [Fact]
        public void test_checkpoint_world_hash_is_ticks_executed_then_core_then_systems()
        {
            var sink = new RecordingCheckpointSink();
            var a = new ProbeSystem(3) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick) };
            var b = new ProbeSystem(10) { OnTick = (ProbeSystem self, in TickContext ctx) => self.Mix(ctx.Tick ^ 0xFFUL) };
            ISimHost host = Harness.Build(sink, a, b);

            var expected = new List<ulong>();
            for (int k = 0; k < 3; k++)
            {
                host.Step(k == 0 ? 1U : 600U);
                expected.Add(FnvOracle.WorldHash(host.CurrentTick, FnvOracle.CoreHashT001(), a.ComputeStateHash(), b.ComputeStateHash()));
            }

            Assert.Equal(expected, sink.Recorded.Select(c => c.WorldHash));
            Assert.Equal(new[] { 0UL, 600UL, 1200UL }, sink.Recorded.Select(c => c.Tick));
        }

        [Fact]
        public void test_checkpoint_core_hash_at_t001_scope_is_sequence_one_no_pending_no_counters()
        {
            var sink = new RecordingCheckpointSink();
            ISimHost host = Harness.Build(sink, new ProbeSystem(2));
            host.Step(601);
            ulong expected = FnvOracle.CoreHashT001();
            Assert.All(sink.Recorded, c => Assert.Equal(expected, c.CoreHash));
        }

        [Fact]
        public void test_checkpoint_core_hash_feeds_id_counters_in_ascending_owner_order()
        {
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder b = Harness.Builder(sink);
            IIdAllocator ids = b.Services.Ids;
            ids.Next(new SystemId(5));
            ids.Next(new SystemId(2));
            ids.Next(new SystemId(5));
            ISimHost host = b.Build();

            host.Step(1);

            ulong expected = FnvOracle.CoreHashT001((2, 1UL), (5, 2UL));
            Assert.Equal(expected, Assert.Single(sink.Recorded).CoreHash);
            Assert.Equal(FnvOracle.WorldHash(1UL, expected), host.WorldStateHash());
        }

        [Fact]
        public void test_checkpoint_core_hash_tracks_allocations_made_during_ticks()
        {
            var sink = new RecordingCheckpointSink();
            ISimHostBuilder b = Harness.Builder(sink);
            IIdAllocator ids = b.Services.Ids;
            b.Register(new ProbeSystem(1) { OnTick = (ProbeSystem self, in TickContext ctx) => ids.Next(self.Id) });
            ISimHost host = b.Build();

            host.Step(601);

            Assert.Equal(FnvOracle.CoreHashT001((1, 1UL)), sink.Recorded[0].CoreHash);
            Assert.Equal(FnvOracle.CoreHashT001((1, 601UL)), sink.Recorded[1].CoreHash);
            Assert.NotEqual(sink.Recorded[0].CoreHash, sink.Recorded[1].CoreHash);
        }

        [Fact]
        public void test_checkpoint_system_hashes_follow_registry_order_and_values()
        {
            var sink = new RecordingCheckpointSink();
            var p3 = new ProbeSystem(3);
            var p9 = new ProbeSystem(9);
            var p12 = new ProbeSystem(12);
            p3.HashOverride = () => 0x3000UL + (ulong)p3.TickCalls;
            p9.HashOverride = () => 0x9000UL + (ulong)p9.TickCalls;
            p12.HashOverride = () => 0xC000UL + (ulong)p12.TickCalls;
            ISimHost host = Harness.Build(sink, p3, p9, p12);

            host.Step(601);

            Assert.Equal(new[] { 0x3001UL, 0x9001UL, 0xC001UL }, sink.Recorded[0].SystemHashes);
            Assert.Equal(new[] { 0x3000UL + 601UL, 0x9000UL + 601UL, 0xC000UL + 601UL }, sink.Recorded[1].SystemHashes);
        }

        [Fact]
        public void test_checkpoint_system_hashes_empty_with_no_systems()
        {
            var sink = new RecordingCheckpointSink();
            Harness.Build(sink).Step(1);
            Checkpoint cp = Assert.Single(sink.Recorded);
            Assert.NotNull(cp.SystemHashes);
            Assert.Empty(cp.SystemHashes);
        }

        [Fact]
        public void test_checkpoint_system_hashes_array_is_fresh_and_owned_by_sink()
        {
            var sink = new RecordingCheckpointSink();
            var probe = new ProbeSystem(4) { HashOverride = () => 0xABCDUL };
            ISimHost host = Harness.Build(sink, probe);

            host.Step(1);
            ulong[] first = sink.Recorded[0].SystemHashes;
            first[0] = 0xDEADUL;
            ulong hashAfterTamper = host.WorldStateHash();
            host.Step(600);

            ulong[] second = sink.Recorded[1].SystemHashes;
            Assert.NotSame(first, second);
            Assert.Equal(new[] { 0xABCDUL }, second);
            Assert.Equal(new[] { 0xDEADUL }, first);
            Assert.Equal(FnvOracle.WorldHash(1UL, FnvOracle.CoreHashT001(), 0xABCDUL), hashAfterTamper);
        }

        [Fact]
        public void test_checkpoint_world_hash_changes_with_any_system_hash()
        {
            RecordingCheckpointSink Run(ulong hashOfSecond)
            {
                var sink = new RecordingCheckpointSink();
                Harness.Build(
                    sink,
                    new ProbeSystem(2) { HashOverride = () => 7UL },
                    new ProbeSystem(13) { HashOverride = () => hashOfSecond }).Step(1);
                return sink;
            }

            Checkpoint x = Assert.Single(Run(100UL).Recorded);
            Checkpoint y = Assert.Single(Run(101UL).Recorded);

            Assert.Equal(x.CoreHash, y.CoreHash);
            Assert.NotEqual(x.WorldHash, y.WorldHash);
            Assert.Equal(new[] { 7UL, 100UL }, x.SystemHashes);
            Assert.Equal(new[] { 7UL, 101UL }, y.SystemHashes);
        }
    }
}
