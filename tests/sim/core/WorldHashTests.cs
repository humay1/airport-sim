using System;
using Xunit;
using static AirportSim.Sim.Core.Tests.HashTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-004. The exact world hash of 08-interfaces-core.md §8.9 (Q-014, Q-017):
    /// a fresh StateHasher fed the ticks-executed count, then CoreHash, then each
    /// registered system's hash in registry order, all as uint64. T-001's tests may
    /// not pin the value; these do. No command is submitted and no id allocated, so
    /// the core section is the idle one: next sequence 1, 0 pending, 0 counters.
    /// </summary>
    public sealed class WorldHashTests
    {
        private const uint TicksPerDay = (uint)SimConstants.TICKS_PER_SIM_DAY;
        private const ulong HashA = 0x0123456789ABCDEFUL;
        private const ulong HashB = 0xFEDCBA9876543210UL;

        [Fact]
        public void test_world_hash_idle_core_section_oracle_matches_state_hasher()
        {
            var expected = new StateHasher();
            expected.Feed(1UL);
            expected.Feed(0UL);
            expected.Feed(0UL);
            Assert.Equal(expected.Result, IdleCoreHash);
        }

        [Fact]
        public void test_world_hash_after_build_feeds_zero_ticks_core_and_systems()
        {
            ISimHost host = Host(77UL, new Recorder(), new ConstantHashSystem(3, HashA), new ConstantHashSystem(11, HashB));
            Assert.Equal(0UL, host.CurrentTick);
            Assert.Equal(WorldHashOracle(0UL, IdleCoreHash, new[] { HashA, HashB }), host.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_without_systems_is_ticks_then_core_section()
        {
            ISimHost host = Host(0UL, new Recorder());
            Assert.Equal(HashFnv.OfU64s(0UL, IdleCoreHash), host.WorldStateHash());
            host.Step(1);
            Assert.Equal(HashFnv.OfU64s(1UL, IdleCoreHash), host.WorldStateHash());
            host.Step(599);
            Assert.Equal(HashFnv.OfU64s(600UL, IdleCoreHash), host.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_feeds_ticks_executed_after_each_step()
        {
            ISimHost host = Host(5UL, new Recorder(), new ConstantHashSystem(3, HashA), new ConstantHashSystem(11, HashB));
            uint[] chunks = { 1, 2, 597, 1, 1000 };
            ulong executed = 0;
            foreach (uint chunk in chunks)
            {
                host.Step(chunk);
                executed += chunk;
                Assert.Equal(executed, host.CurrentTick);
                Assert.Equal(WorldHashOracle(executed, IdleCoreHash, new[] { HashA, HashB }), host.WorldStateHash());
            }
        }

        [Fact]
        public void test_world_hash_folds_systems_in_registry_order()
        {
            ISimHost ab = Host(1UL, new Recorder(), new ConstantHashSystem(3, HashA), new ConstantHashSystem(11, HashB));
            ISimHost ba = Host(1UL, new Recorder(), new ConstantHashSystem(3, HashB), new ConstantHashSystem(11, HashA));
            Assert.Equal(WorldHashOracle(0UL, IdleCoreHash, new[] { HashA, HashB }), ab.WorldStateHash());
            Assert.Equal(WorldHashOracle(0UL, IdleCoreHash, new[] { HashB, HashA }), ba.WorldStateHash());
            Assert.NotEqual(ab.WorldStateHash(), ba.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_master_seed_enters_only_through_state()
        {
            // The seed is not in the fold (§8.9). Systems that ignore RNG give equal
            // world hashes under different seeds.
            ISimHost a = Host(1UL, new Recorder(), new ConstantHashSystem(4, HashA));
            ISimHost b = Host(ulong.MaxValue, new Recorder(), new ConstantHashSystem(4, HashA));
            a.Step(10);
            b.Step(10);
            Assert.Equal(a.WorldStateHash(), b.WorldStateHash());
            Assert.Equal(WorldHashOracle(10UL, IdleCoreHash, new[] { HashA }), a.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_headless_day_checkpoints_match_oracle()
        {
            // A full day with the sorted-feed pattern system and a constant one. Each
            // checkpoint at tick t carries: CoreHash = idle core section; SystemHashes =
            // the model's hash after t + 1 ticks, then the constant; WorldHash = the fold
            // with t + 1 ticks executed.
            var sink = new Recorder();
            ISimHost host = Host(2026UL, sink, new SortedFeedSystem(6), new ConstantHashSystem(12, HashB));
            host.Step(TicksPerDay);

            var model = new SortedFeedModel();
            ulong executed = 0;
            Assert.Equal(24, sink.Recorded.Count);
            for (int i = 0; i < sink.Recorded.Count; i++)
            {
                Checkpoint cp = sink.Recorded[i];
                Assert.Equal((ulong)i * SimConstants.HASH_CHECKPOINT_TICKS, cp.Tick);
                while (executed <= cp.Tick)
                {
                    model.RunTick(executed);
                    executed++;
                }
                Assert.Equal(IdleCoreHash, cp.CoreHash);
                Assert.Equal(new[] { model.Hash(), HashB }, cp.SystemHashes);
                Assert.Equal(WorldHashOracle(cp.Tick + 1, cp.CoreHash, cp.SystemHashes), cp.WorldHash);
            }

            while (executed < TicksPerDay)
            {
                model.RunTick(executed);
                executed++;
            }
            Assert.Equal(WorldHashOracle(TicksPerDay, IdleCoreHash, new[] { model.Hash(), HashB }), host.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_headless_day_same_seed_identical_checkpoints()
        {
            var first = new Recorder();
            var second = new Recorder();
            ISimHost a = Host(99UL, first, new SortedFeedSystem(6), new ConstantHashSystem(12, HashA));
            ISimHost b = Host(99UL, second, new SortedFeedSystem(6), new ConstantHashSystem(12, HashA));
            a.Step(TicksPerDay);
            b.Step(TicksPerDay);
            AssertSameCheckpoints(first.Recorded, second.Recorded);
            Assert.Equal(a.WorldStateHash(), b.WorldStateHash());
        }

        [Fact]
        public void test_world_hash_chunked_steps_match_single_day_step()
        {
            var whole = new Recorder();
            ISimHost one = Host(3UL, whole, new SortedFeedSystem(6));
            one.Step(TicksPerDay);

            const ulong seed = 0x5EED0004C0000001UL;
            var gen = new HashGen(seed);
            var chunked = new Recorder();
            ISimHost two = Host(3UL, chunked, new SortedFeedSystem(6));
            ulong remaining = TicksPerDay;
            while (remaining > 0)
            {
                uint chunk = (uint)(1 + gen.Below(1500));
                if (chunk > remaining) chunk = (uint)remaining;
                two.Step(chunk);
                remaining -= chunk;
            }

            AssertSameCheckpoints(whole.Recorded, chunked.Recorded);
            Assert.Equal(one.WorldStateHash(), two.WorldStateHash());
        }
    }
}
