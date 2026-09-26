using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// IIdAllocator, 08 §8.4 "Allocation rule" (Q-017): per-owner counters
    /// from 0, Next returns (owner &lt;&lt; 48) | counter after incrementing,
    /// illegal owners throw ArgumentException. Callable during construction
    /// and phases 1-3.
    /// </summary>
    public sealed class IdsTests
    {
        private static IIdAllocator NewAllocator()
        {
            return Harness.Builder(new RecordingCheckpointSink()).Services.Ids;
        }

        private static ulong Expected(ushort owner, ulong counter)
        {
            return ((ulong)owner << 48) | counter;
        }

        [Fact]
        public void test_ids_first_allocation_is_counter_one_under_owner()
        {
            IIdAllocator ids = NewAllocator();
            Assert.Equal(Expected(1, 1), ids.Next(new SystemId(1)).Value);
            Assert.Equal(Expected(14, 1), ids.Next(new SystemId(14)).Value);
            Assert.Equal(Expected(7, 1), ids.Next(new SystemId(7)).Value);
        }

        [Fact]
        public void test_ids_counter_increments_per_owner()
        {
            IIdAllocator ids = NewAllocator();
            for (ulong k = 1; k <= 100; k++)
            {
                Assert.Equal(Expected(3, k), ids.Next(new SystemId(3)).Value);
            }
        }

        [Fact]
        public void test_ids_owners_count_independently()
        {
            IIdAllocator ids = NewAllocator();
            Assert.Equal(Expected(3, 1), ids.Next(new SystemId(3)).Value);
            Assert.Equal(Expected(5, 1), ids.Next(new SystemId(5)).Value);
            Assert.Equal(Expected(3, 2), ids.Next(new SystemId(3)).Value);
            Assert.Equal(Expected(5, 2), ids.Next(new SystemId(5)).Value);
            Assert.Equal(Expected(2, 1), ids.Next(new SystemId(2)).Value);
        }

        [Fact]
        public void test_ids_allocators_of_separate_builders_are_independent()
        {
            IIdAllocator a = NewAllocator();
            IIdAllocator b = NewAllocator();
            a.Next(new SystemId(4));
            a.Next(new SystemId(4));
            Assert.Equal(Expected(4, 1), b.Next(new SystemId(4)).Value);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(8)]
        [InlineData(15)]
        [InlineData(65535)]
        public void test_ids_illegal_owner_throws_argument_exception(int owner)
        {
            IIdAllocator ids = NewAllocator();
            Assert.Throws<ArgumentException>(() => ids.Next(new SystemId((ushort)owner)));
        }

        [Fact]
        public void test_ids_never_collide_and_never_zero_property()
        {
            const ulong seed = 0x1D5_A110CUL;
            ushort[] legal = { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14 };
            var rng = new SplitMix64(seed);
            IIdAllocator ids = NewAllocator();
            var counts = new ulong[15];
            var seen = new HashSet<ulong>();
            for (int i = 0; i < 20000; i++)
            {
                ushort owner = legal[rng.Next() % (ulong)legal.Length];
                ulong v = ids.Next(new SystemId(owner)).Value;
                counts[owner]++;
                Assert.True(v != 0UL, $"seed {seed:X}, iteration {i}: EntityId(0) allocated");
                Assert.True(seen.Add(v), $"seed {seed:X}, iteration {i}: duplicate id {v:X16}");
                Assert.True(v == Expected(owner, counts[owner]), $"seed {seed:X}, iteration {i}: owner {owner} got {v:X16}, expected {Expected(owner, counts[owner]):X16}");
            }
        }

        [Fact]
        public void test_ids_counters_continue_across_construction_update_and_dispatch()
        {
            var got = new List<ulong>();
            ISimHostBuilder b = Harness.Builder(new RecordingCheckpointSink());
            IIdAllocator ids = b.Services.Ids;
            got.Add(ids.Next(new SystemId(6)).Value);
            b.Services.Events.Subscribe<Ping>(new SystemId(6), (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
                got.Add(ids.Next(new SystemId(6)).Value));
            b.Register(new ProbeSystem(6)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    got.Add(ids.Next(self.Id).Value);
                    ctx.Events.Publish(new Ping(0), EventRef.None);
                },
            });
            ISimHost host = b.Build();

            host.Step(2);

            Assert.Equal(new[] { Expected(6, 1), Expected(6, 2), Expected(6, 3), Expected(6, 4), Expected(6, 5) }, got);
        }

        [Fact]
        public void test_ids_owner_need_not_be_registered()
        {
            IIdAllocator ids = NewAllocator();
            Assert.Equal(Expected(10, 1), ids.Next(new SystemId(10)).Value);
        }
    }
}
