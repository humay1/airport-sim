using System;
using Xunit;
using static AirportSim.Sim.Core.Tests.RngTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-002. IRandomStream against 08-interfaces-core.md §8.8, "Exact reference"
    /// (Q-019). The golden vectors in the spec are the oracle. The reference
    /// model in RngTestSupport extends them to property loops and is itself
    /// re-checked against the spec vectors inside the golden tests.
    /// </summary>
    public sealed class RandomStreamTests
    {
        // Row 1 of the §8.8 golden table: MasterSeed 0, "sim.flow.showup".
        private const ulong Row1Out0 = 0x4D8ADDC1EA523EA8UL;
        private const ulong Row1Out1 = 0x881053D9C83E81ECUL;
        private const ulong Row1Out2 = 0x9F943EEE723DAD43UL;
        private const ulong Row1Out3 = 0x41FB063846C7BC01UL;

        private static IRandomStream Row1()
        {
            return FreshStream(0UL, "sim.flow.showup");
        }

        // ------------------------------------------------------------ golden vectors

        [Theory]
        [InlineData(0UL, "sim.flow.showup", 0x80AA6E48500EF830UL,
            0x4D8ADDC1EA523EA8UL, 0x881053D9C83E81ECUL, 0x9F943EEE723DAD43UL, 0x41FB063846C7BC01UL)]
        [InlineData(12345UL, "sim.schedule.jitter", 0x09F750F58CE1F6D5UL,
            0x38C30AB4838B2ECEUL, 0x20E490881273F31AUL, 0x160AF506335E076AUL, 0x3A5487F2EF5EDE58UL)]
        [InlineData(ulong.MaxValue, "sim.airside.taxi", 0xC6381A17FBE07F29UL,
            0xBAF003FC5983A4B7UL, 0x54E24678B7DA92B8UL, 0xC6C76D95A0A72034UL, 0xF635BA852278C41CUL)]
        public void test_random_stream_golden_vector_matches_first_four_outputs(
            ulong masterSeed, string name, ulong nameHash, ulong o0, ulong o1, ulong o2, ulong o3)
        {
            // The oracle must agree with the spec table, or the property loops below mean nothing.
            Assert.Equal(nameHash, Fnv1a64(System.Text.Encoding.UTF8.GetBytes(name)));
            var reference = new ReferenceStream(masterSeed, name);
            Assert.Equal(new[] { o0, o1, o2, o3 },
                new[] { reference.NextUInt64(), reference.NextUInt64(), reference.NextUInt64(), reference.NextUInt64() });

            IRandomStream stream = FreshStream(masterSeed, name);
            ulong[] got = { stream.NextUInt64(), stream.NextUInt64(), stream.NextUInt64(), stream.NextUInt64() };
            Assert.Equal(new[] { o0, o1, o2, o3 }, got);
        }

        [Fact]
        public void test_random_stream_next_int_golden_seed_zero_returns_3_5_6_2()
        {
            IRandomStream stream = Row1();
            int[] got = { stream.NextInt(0, 10), stream.NextInt(0, 10), stream.NextInt(0, 10), stream.NextInt(0, 10) };
            Assert.Equal(new[] { 3, 5, 6, 2 }, got);
        }

        [Fact]
        public void test_random_stream_next_fx01_golden_seed_zero_returns_top_32_bits()
        {
            IRandomStream stream = Row1();
            Fx a = stream.NextFx01();
            Fx b = stream.NextFx01();
            Assert.Equal(1300946369L, a.Raw);
            Assert.Equal(2282771417L, b.Raw);
            // After two draws the third output is the third golden word.
            Assert.Equal(Row1Out2, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_reference_model_agrees_across_seeds_and_operations()
        {
            // The xoshiro core check printed in §8.8, then a mixed-operation
            // sweep over random seeds and names, bit for bit.
            var check = new ReferenceStream(1UL, 2UL, 3UL, 4UL);
            Assert.Equal(new ulong[] { 11520UL, 0UL, 1509978240UL, 1215971899390074240UL },
                new[] { check.NextUInt64(), check.NextUInt64(), check.NextUInt64(), check.NextUInt64() });

            const ulong seed = 0x5EED0002A0000001UL;
            var gen = new SplitMix64(seed);
            for (int i = 0; i < 300; i++)
            {
                ulong master = gen.Next();
                string name = Names[gen.Below(Names.Length)];
                var expected = new ReferenceStream(master, name);
                IRandomStream actual = FreshStream(master, name);
                if (actual.ComputeStateHash() != expected.ComputeStateHash())
                {
                    Assert.Fail(At(seed, i) + ": fresh stream hash differs for " + name + " master " + Hex(master));
                }
                for (int op = 0; op < 40; op++)
                {
                    int kind = gen.Below(5);
                    switch (kind)
                    {
                        case 0:
                        {
                            ulong e = expected.NextUInt64();
                            ulong g = actual.NextUInt64();
                            if (e != g) Assert.Fail(At(seed, i) + ", op " + op + ": NextUInt64 " + Hex(g) + " != " + Hex(e));
                            break;
                        }
                        case 1:
                        {
                            int lo = unchecked((int)(uint)gen.Next());
                            int hi = unchecked((int)(uint)gen.Next());
                            if (lo == hi) hi = lo == int.MaxValue ? hi : hi + 1;
                            if (lo == hi) lo--;
                            if (lo > hi) { int tmp = lo; lo = hi; hi = tmp; }
                            int e = expected.NextInt(lo, hi);
                            int g = actual.NextInt(lo, hi);
                            if (e != g) Assert.Fail(At(seed, i) + ", op " + op + ": NextInt(" + lo + ", " + hi + ") " + g + " != " + e);
                            break;
                        }
                        case 2:
                        {
                            long e = expected.NextFx01Raw();
                            long g = actual.NextFx01().Raw;
                            if (e != g) Assert.Fail(At(seed, i) + ", op " + op + ": NextFx01 raw " + g + " != " + e);
                            break;
                        }
                        case 3:
                        {
                            long p = unchecked((long)gen.Next()) >> gen.Below(40);
                            bool e = expected.Chance(p);
                            bool g = actual.Chance(Fx.FromRaw(p));
                            if (e != g) Assert.Fail(At(seed, i) + ", op " + op + ": Chance(raw " + p + ") " + g + " != " + e);
                            break;
                        }
                        default:
                        {
                            int n = gen.Below(12);
                            int[] a = new int[n];
                            int[] b = new int[n];
                            for (int k = 0; k < n; k++) { a[k] = k; b[k] = k; }
                            expected.Shuffle<int>(a);
                            actual.Shuffle<int>(b);
                            if (!a.AsSpan().SequenceEqual(b)) Assert.Fail(At(seed, i) + ", op " + op + ": Shuffle of " + n + " differs");
                            break;
                        }
                    }
                }
                if (actual.ComputeStateHash() != expected.ComputeStateHash())
                {
                    Assert.Fail(At(seed, i) + ": stream hash differs after the operation sequence");
                }
            }
        }

        // ------------------------------------------------------------ NextInt

        [Theory]
        [InlineData(0, 0)]
        [InlineData(5, 5)]
        [InlineData(10, 9)]
        [InlineData(int.MaxValue, int.MinValue)]
        [InlineData(int.MaxValue, int.MaxValue)]
        [InlineData(int.MinValue, int.MinValue)]
        public void test_random_stream_next_int_min_not_below_max_throws_argument_out_of_range(int min, int max)
        {
            IRandomStream stream = Row1();
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(min, max));
        }

        [Fact]
        public void test_random_stream_next_int_invalid_bounds_consume_no_draw()
        {
            IRandomStream stream = Row1();
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(3, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.NextInt(4, -4));
            Assert.Equal(Row1Out0, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_next_int_unit_range_returns_min_and_consumes_one_draw()
        {
            IRandomStream stream = Row1();
            Assert.Equal(-7, stream.NextInt(-7, -6));
            Assert.Equal(Row1Out1, stream.NextUInt64());
            Assert.Equal(int.MaxValue - 1, stream.NextInt(int.MaxValue - 1, int.MaxValue));
            Assert.Equal(Row1Out3, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_next_int_extreme_bounds_match_reference()
        {
            // range = (uint32)(max - min) must be computed without overflow for the widest bounds.
            var expected = new ReferenceStream(0UL, "sim.flow.showup");
            IRandomStream actual = Row1();
            int[][] bounds =
            {
                new[] { int.MinValue, int.MaxValue },
                new[] { int.MinValue, 0 },
                new[] { -1, int.MaxValue },
                new[] { int.MinValue, int.MinValue + 1 },
                new[] { int.MaxValue - 1, int.MaxValue },
            };
            for (int round = 0; round < 50; round++)
            {
                foreach (int[] b in bounds)
                {
                    int e = expected.NextInt(b[0], b[1]);
                    int g = actual.NextInt(b[0], b[1]);
                    Assert.Equal(e, g);
                    Assert.InRange(g, b[0], b[1] - 1);
                }
            }
            Assert.Equal(expected.NextUInt64(), actual.NextUInt64());
        }

        [Fact]
        public void test_random_stream_next_int_rejection_path_matches_reference()
        {
            // range = 2^31 + 1 rejects about half of all draws (§8.8 Lemire loop).
            const int min = int.MinValue;
            const int max = 1;
            var expected = new ReferenceStream(12345UL, "sim.schedule.jitter");
            IRandomStream actual = FreshStream(12345UL, "sim.schedule.jitter");
            for (int i = 0; i < 2000; i++)
            {
                int e = expected.NextInt(min, max);
                int g = actual.NextInt(min, max);
                if (e != g) Assert.Fail("iteration " + i + ": NextInt(int.MinValue, 1) " + g + " != " + e);
            }
            Assert.True(expected.Rejections > 500, "the fixture must exercise the rejection loop");
            // The number of draws consumed must match too.
            Assert.Equal(expected.NextUInt64(), actual.NextUInt64());
            Assert.Equal(expected.ComputeStateHash(), actual.ComputeStateHash());
        }

        [Fact]
        public void test_random_stream_next_int_results_within_bounds()
        {
            const ulong seed = 0x5EED0002A0000002UL;
            var gen = new SplitMix64(seed);
            IRandomStream stream = FreshStream(0x0123456789ABCDEFUL, "sim.core.a");
            for (int i = 0; i < 20000; i++)
            {
                int lo = unchecked((int)(uint)gen.Next()) >> gen.Below(32);
                int width = 1 + gen.Below(1 << gen.Below(31));
                long hiWide = (long)lo + width;
                int hi = hiWide > int.MaxValue ? int.MaxValue : (int)hiWide;
                if (hi <= lo) continue;
                int v = stream.NextInt(lo, hi);
                if (v < lo || v >= hi) Assert.Fail(At(seed, i) + ": NextInt(" + lo + ", " + hi + ") returned " + v);
            }
        }

        [Fact]
        public void test_random_stream_next_int_equal_ranges_consume_equal_draws()
        {
            // "equal bounds consume equal draws": the offset of the bounds never changes the draws taken.
            const ulong seed = 0x5EED0002A0000003UL;
            var gen = new SplitMix64(seed);
            IRandomStream a = FreshStream(77UL, "sim.delay.x9");
            IRandomStream b = FreshStream(77UL, "sim.delay.x9");
            for (int i = 0; i < 5000; i++)
            {
                int range = 1 + gen.Below(int.MaxValue);
                int offsetB = -gen.Below(int.MaxValue - range + 1);
                int va = a.NextInt(0, range);
                int vb = b.NextInt(offsetB, offsetB + range);
                if (vb - offsetB != va) Assert.Fail(At(seed, i) + ": offset bounds changed the result");
            }
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
            Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        }

        [Fact]
        public void test_random_stream_next_int_large_range_is_unbiased_across_thirds()
        {
            // range = 3 * 2^30. A modulo reduction would put half of all results
            // in the first third; the unbiased method puts a third in each.
            const int min = int.MinValue;
            const int max = 1 << 30;
            const long third = 1L << 30;
            IRandomStream stream = FreshStream(2026UL, "sim.flow.showup");
            int[] counts = new int[3];
            const int n = 30000;
            for (int i = 0; i < n; i++)
            {
                long offset = (long)stream.NextInt(min, max) - min;
                counts[(int)(offset / third)]++;
            }
            foreach (int c in counts)
            {
                Assert.InRange(c, n / 3 - 900, n / 3 + 900);
            }
        }

        [Fact]
        public void test_random_stream_next_int_small_range_frequencies_within_tolerance()
        {
            IRandomStream stream = FreshStream(99UL, "sim.airside.taxi");
            int[] counts = new int[6];
            const int n = 60000;
            for (int i = 0; i < n; i++)
            {
                counts[stream.NextInt(0, 6)]++;
            }
            foreach (int c in counts)
            {
                // Expected 10000 each; sigma is about 91. 500 is more than 5 sigma.
                Assert.InRange(c, 9500, 10500);
            }
        }

        // ------------------------------------------------------------ NextFx01

        [Fact]
        public void test_random_stream_next_fx01_raw_in_unit_interval()
        {
            IRandomStream stream = FreshStream(ulong.MaxValue, "sim.airside.taxi");
            IRandomStream twin = FreshStream(ulong.MaxValue, "sim.airside.taxi");
            for (int i = 0; i < 20000; i++)
            {
                long raw = stream.NextFx01().Raw;
                if (raw < 0 || raw >= (1L << 32)) Assert.Fail("iteration " + i + ": NextFx01 raw " + raw + " outside [0, 2^32)");
                long top = (long)(twin.NextUInt64() >> 32);
                if (raw != top) Assert.Fail("iteration " + i + ": NextFx01 is not the top 32 bits of one draw");
            }
        }

        // ------------------------------------------------------------ Chance

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(1L << 31)]
        [InlineData(1L << 32)]
        [InlineData(1L << 33)]
        [InlineData(long.MaxValue)]
        public void test_random_stream_chance_any_probability_consumes_exactly_one_draw(long probabilityRaw)
        {
            IRandomStream stream = Row1();
            stream.Chance(Fx.FromRaw(probabilityRaw));
            Assert.Equal(Row1Out1, stream.NextUInt64());
            stream.Chance(Fx.FromRaw(probabilityRaw));
            Assert.Equal(Row1Out3, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_chance_non_positive_probability_returns_false()
        {
            IRandomStream stream = FreshStream(5UL, "sim.core.a");
            for (int i = 0; i < 5000; i++)
            {
                Assert.False(stream.Chance(Fx.Zero));
                Assert.False(stream.Chance(Fx.FromRaw(-1L)));
                Assert.False(stream.Chance(Fx.MinValue));
            }
        }

        [Fact]
        public void test_random_stream_chance_probability_at_least_one_returns_true()
        {
            IRandomStream stream = FreshStream(6UL, "sim.core.a");
            for (int i = 0; i < 5000; i++)
            {
                Assert.True(stream.Chance(Fx.One));
                Assert.True(stream.Chance(Fx.FromInt(3)));
                Assert.True(stream.Chance(Fx.MaxValue));
            }
        }

        [Fact]
        public void test_random_stream_chance_equals_fx01_less_than_probability()
        {
            const ulong seed = 0x5EED0002A0000004UL;
            var gen = new SplitMix64(seed);
            IRandomStream a = FreshStream(31UL, "sim.baggage.belt_0");
            IRandomStream b = FreshStream(31UL, "sim.baggage.belt_0");
            int trues = 0;
            for (int i = 0; i < 20000; i++)
            {
                long p = (long)(gen.Next() >> 32);
                bool got = a.Chance(Fx.FromRaw(p));
                bool expected = b.NextFx01().Raw < p;
                if (got != expected) Assert.Fail(At(seed, i) + ": Chance(raw " + p + ") != (NextFx01 < p)");
                if (got) trues++;
            }
            Assert.InRange(trues, 9000, 11000);
        }

        // ------------------------------------------------------------ Shuffle

        [Fact]
        public void test_random_stream_shuffle_matches_descending_fisher_yates()
        {
            IRandomStream stream = Row1();
            IRandomStream twin = Row1();
            int[] items = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            int[] expected = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            for (int i = expected.Length - 1; i >= 1; i--)
            {
                int j = twin.NextInt(0, i + 1);
                int tmp = expected[i];
                expected[i] = expected[j];
                expected[j] = tmp;
            }
            stream.Shuffle<int>(items);
            Assert.Equal(expected, items);
            // The first swap is items[9] with items[NextInt(0, 10)] = items[3] (the §8.8 NextInt golden).
            Assert.Equal(3, items[9]);
            Assert.Equal(twin.NextUInt64(), stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_shuffle_empty_or_single_draws_nothing()
        {
            IRandomStream stream = Row1();
            stream.Shuffle<int>(Span<int>.Empty);
            int[] one = { 42 };
            stream.Shuffle<int>(one);
            Assert.Equal(42, one[0]);
            Assert.Equal(Row1Out0, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_shuffle_two_items_draws_once()
        {
            IRandomStream stream = Row1();
            var reference = new ReferenceStream(0UL, "sim.flow.showup");
            int[] items = { 10, 20 };
            int[] expected = { 10, 20 };
            stream.Shuffle<int>(items);
            reference.Shuffle<int>(expected);
            Assert.Equal(expected, items);
            Assert.Equal(Row1Out1, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_shuffle_produces_permutation()
        {
            const ulong seed = 0x5EED0002A0000005UL;
            var gen = new SplitMix64(seed);
            IRandomStream stream = FreshStream(8UL, "sim.q.0");
            for (int i = 0; i < 500; i++)
            {
                int n = gen.Below(64);
                int[] items = new int[n];
                for (int k = 0; k < n; k++) items[k] = k * 3 + 1;
                stream.Shuffle<int>(items);
                bool[] seen = new bool[n];
                foreach (int v in items)
                {
                    int k = (v - 1) / 3;
                    if ((v - 1) % 3 != 0 || k < 0 || k >= n || seen[k]) Assert.Fail(At(seed, i) + ": Shuffle is not a permutation");
                    seen[k] = true;
                }
            }
        }

        [Fact]
        public void test_random_stream_shuffle_reference_elements_follow_same_permutation()
        {
            IRandomStream a = FreshStream(4UL, "sim.turnaround.job_order");
            IRandomStream b = FreshStream(4UL, "sim.turnaround.job_order");
            int[] ints = new int[17];
            string[] strings = new string[17];
            for (int k = 0; k < 17; k++)
            {
                ints[k] = k;
                strings[k] = "s" + k.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            a.Shuffle<int>(ints);
            b.Shuffle<string>(strings);
            for (int k = 0; k < 17; k++)
            {
                Assert.Equal("s" + ints[k].ToString(System.Globalization.CultureInfo.InvariantCulture), strings[k]);
            }
            Assert.NotEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }, ints);
        }

        // ------------------------------------------------------------ state hash

        [Fact]
        public void test_random_stream_state_hash_fresh_equals_fnv_of_seeded_state()
        {
            foreach (string name in Names)
            {
                foreach (ulong master in new[] { 0UL, 1UL, 12345UL, ulong.MaxValue })
                {
                    Assert.Equal(new ReferenceStream(master, name).ComputeStateHash(),
                        FreshStream(master, name).ComputeStateHash());
                }
            }
        }

        [Fact]
        public void test_random_stream_state_hash_does_not_advance_stream()
        {
            IRandomStream stream = Row1();
            ulong h1 = stream.ComputeStateHash();
            ulong h2 = stream.ComputeStateHash();
            Assert.Equal(h1, h2);
            Assert.Equal(Row1Out0, stream.NextUInt64());
            Assert.Equal(stream.ComputeStateHash(), stream.ComputeStateHash());
            Assert.Equal(Row1Out1, stream.NextUInt64());
        }

        [Fact]
        public void test_random_stream_state_hash_tracks_every_draw()
        {
            var reference = new ReferenceStream(12345UL, "sim.schedule.jitter");
            IRandomStream stream = FreshStream(12345UL, "sim.schedule.jitter");
            ulong previous = stream.ComputeStateHash();
            Assert.Equal(reference.ComputeStateHash(), previous);
            for (int i = 0; i < 200; i++)
            {
                stream.NextUInt64();
                reference.NextUInt64();
                ulong h = stream.ComputeStateHash();
                Assert.Equal(reference.ComputeStateHash(), h);
                Assert.NotEqual(previous, h);
                previous = h;
            }
        }

        // ------------------------------------------------------------ budget

        [Fact]
        [Trait("Category", "Budget")]
        public void test_random_stream_hot_path_draws_allocate_zero_bytes()
        {
            // 07 "Performance" and T-002's budget: no allocation in NextUInt64,
            // NextInt, NextFx01 or Chance.
            IRandomStream stream = FreshStream(1UL, "sim.flow.showup");
            Fx half = Fx.FromRaw(1L << 31);
            ulong sink = 0;
            for (int i = 0; i < 1000; i++)
            {
                sink ^= stream.NextUInt64();
                sink ^= (ulong)stream.NextInt(0, 7);
                sink ^= (ulong)stream.NextInt(int.MinValue, 1);
                sink ^= (ulong)stream.NextFx01().Raw;
                sink ^= stream.Chance(half) ? 1UL : 0UL;
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++)
            {
                sink ^= stream.NextUInt64();
                sink ^= (ulong)stream.NextInt(0, 7);
                sink ^= (ulong)stream.NextInt(int.MinValue, 1);
                sink ^= (ulong)stream.NextFx01().Raw;
                sink ^= stream.Chance(half) ? 1UL : 0UL;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
            Assert.NotEqual(0UL, sink);
        }
    }
}
