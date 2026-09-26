using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static AirportSim.Sim.Core.Tests.HashTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-004. The per-system hashing discipline of 08-interfaces-core.md §8.9 and
    /// 02-determinism.md rule 5: a system feeds its state in a declared, stable
    /// order, sorting unordered collections by a stable key, and never feeds derived
    /// values. <see cref="SortedFeedSystem"/> is the pattern later modules copy.
    /// </summary>
    public sealed class SystemHashTests
    {
        [Fact]
        public void test_system_hash_sorted_feed_is_independent_of_insertion_order()
        {
            const ulong seed = 0x5EED0004B0000001UL;
            var gen = new HashGen(seed);
            for (int i = 0; i < 100; i++)
            {
                int n = 1 + gen.Below(60);
                var entries = new KeyValuePair<ulong, long>[n];
                for (int k = 0; k < n; k++)
                {
                    entries[k] = new KeyValuePair<ulong, long>(gen.Next(), unchecked((long)gen.Next()));
                }

                var forward = new SortedFeedSystem(6);
                foreach (var e in entries) forward.Set(e.Key, e.Value);
                var backward = new SortedFeedSystem(6);
                for (int k = n - 1; k >= 0; k--) backward.Set(entries[k].Key, entries[k].Value);

                if (forward.ComputeStateHash() != backward.ComputeStateHash())
                {
                    Assert.Fail(At(seed, i) + ": insertion order changed the hash");
                }
            }
        }

        [Fact]
        public void test_system_hash_sorted_feed_ignores_enumeration_order_after_removal()
        {
            // Removing and re-adding reorders a Dictionary's enumeration; the fixture
            // proves that, then proves the sorted feed does not see it.
            var a = new SortedFeedSystem(6);
            var b = new SortedFeedSystem(6);
            for (ulong k = 1; k <= 8; k++)
            {
                a.Set(k, (long)k * 10);
                b.Set(k, (long)k * 10);
            }
            b.Remove(2);
            b.Remove(5);
            b.Set(2, 20);
            b.Set(5, 50);

            Assert.NotEqual(a.RawEnumerationOrder.ToArray(), b.RawEnumerationOrder.ToArray());
            Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        }

        [Fact]
        public void test_system_hash_sorted_feed_matches_declared_order_oracle()
        {
            var system = new SortedFeedSystem(6);
            system.Set(30, -3);
            system.Set(10, 1);
            system.Set(20, long.MinValue);
            ulong expected = new HashFnv()
                .U64(3)
                .U64(10).I64(1)
                .U64(20).I64(long.MinValue)
                .U64(30).I64(-3)
                .Result;
            Assert.Equal(expected, system.ComputeStateHash());
        }

        [Fact]
        public void test_system_hash_changes_with_any_fed_value()
        {
            var baseline = new SortedFeedSystem(6);
            baseline.Set(1, 100);
            baseline.Set(2, 200);
            ulong h = baseline.ComputeStateHash();

            var value = new SortedFeedSystem(6);
            value.Set(1, 100);
            value.Set(2, 201);
            var key = new SortedFeedSystem(6);
            key.Set(1, 100);
            key.Set(3, 200);
            var extra = new SortedFeedSystem(6);
            extra.Set(1, 100);
            extra.Set(2, 200);
            extra.Set(4, 0);

            Assert.NotEqual(h, value.ComputeStateHash());
            Assert.NotEqual(h, key.ComputeStateHash());
            Assert.NotEqual(h, extra.ComputeStateHash());
        }

        [Fact]
        public void test_system_hash_count_prefix_separates_empty_from_zero_entry()
        {
            // The declared order starts with the count, so an empty map and a map
            // holding (0, 0) cannot collide by concatenation.
            var empty = new SortedFeedSystem(6);
            var zero = new SortedFeedSystem(6);
            zero.Set(0, 0);
            Assert.Equal(HashFnv.OfU64s(0UL), empty.ComputeStateHash());
            Assert.NotEqual(empty.ComputeStateHash(), zero.ComputeStateHash());
        }

        [Fact]
        public void test_system_hash_derived_cache_is_not_fed()
        {
            // Equal state reached by different histories hashes equal, and the hash is
            // exactly the declared feed: the cached total is derived and not fed (§8.9).
            var direct = new SortedFeedSystem(6);
            direct.Set(7, 70);
            var winding = new SortedFeedSystem(6);
            winding.Set(7, 10);
            winding.Set(8, 5);
            winding.Remove(8);
            winding.Set(7, 70);

            Assert.Equal(direct.CachedTotal, winding.CachedTotal);
            Assert.Equal(new HashFnv().U64(1).U64(7).I64(70).Result, winding.ComputeStateHash());
            Assert.Equal(direct.ComputeStateHash(), winding.ComputeStateHash());
        }

        [Fact]
        public void test_system_hash_rebuilt_from_saved_entries_round_trips()
        {
            // Save/load stand-in: a system's plain data written out as (key, value)
            // pairs and read back into a fresh system, in shuffled order, hashes equal.
            var source = new SortedFeedSystem(6);
            for (ulong t = 0; t < 500; t++)
            {
                source.Tick(new TickContext(t, null!, null!, null!, null!, null!));
            }
            ulong before = source.ComputeStateHash();

            var saved = new List<KeyValuePair<ulong, long>>(source.Export());
            saved.Reverse();
            var loaded = new SortedFeedSystem(6);
            foreach (var e in saved) loaded.Set(e.Key, e.Value);

            Assert.Equal(before, loaded.ComputeStateHash());
            Assert.Equal(source.CachedTotal, loaded.CachedTotal);
        }
    }
}
