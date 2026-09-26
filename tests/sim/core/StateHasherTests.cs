using System;
using Xunit;
using static AirportSim.Sim.Core.Tests.HashTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-004. An independent proof of T-001's StateHasher against
    /// 08-interfaces-core.md §8.9 "Encoding, the concrete hasher and the core
    /// section" (Q-017): the four golden vectors, the byte encoding of every Feed
    /// overload, and that Feed never allocates.
    /// </summary>
    public sealed class StateHasherTests
    {
        private const ulong GoldenFresh = 0xCBF29CE484222325UL;
        private const ulong GoldenZero = 0xA8C7F832281A39C5UL;
        private const ulong GoldenOneMinusOneTrue = 0x9185A69DA7E88AC7UL;
        private const ulong GoldenSpan123 = 0x01EF76D429B11552UL;

        // ------------------------------------------------------------ golden vectors

        [Fact]
        public void test_state_hasher_oracle_reproduces_spec_golden_vectors()
        {
            // The oracle must agree with the spec before it may judge anything else.
            Assert.Equal(GoldenFresh, new FnvOracle().Result);
            Assert.Equal(GoldenZero, new FnvOracle().U64(0UL).Result);
            Assert.Equal(GoldenOneMinusOneTrue, new FnvOracle().U64(1UL).I64(-1L).Bool(true).Result);
            Assert.Equal(GoldenSpan123, new FnvOracle().Span(new byte[] { 1, 2, 3 }).Result);

            var h = new StateHasher();
            Assert.Equal(GoldenFresh, h.Result);
        }

        [Fact]
        public void test_state_hasher_fresh_new_and_default_return_offset_basis()
        {
            var created = new StateHasher();
            StateHasher defaulted = default;
            Assert.Equal(GoldenFresh, created.Result);
            Assert.Equal(GoldenFresh, defaulted.Result);
        }

        [Fact]
        public void test_state_hasher_feed_zero_uint64_matches_golden()
        {
            var h = new StateHasher();
            h.Feed(0UL);
            Assert.Equal(GoldenZero, h.Result);
        }

        [Fact]
        public void test_state_hasher_feed_one_minus_one_true_matches_golden()
        {
            var h = new StateHasher();
            h.Feed(1UL);
            h.Feed(-1L);
            h.Feed(true);
            Assert.Equal(GoldenOneMinusOneTrue, h.Result);
        }

        [Fact]
        public void test_state_hasher_feed_span_1_2_3_matches_golden()
        {
            var h = new StateHasher();
            h.Feed(new ReadOnlySpan<byte>(new byte[] { 1, 2, 3 }));
            Assert.Equal(GoldenSpan123, h.Result);
        }

        [Fact]
        public void test_state_hasher_default_struct_feeds_like_new()
        {
            StateHasher h = default;
            h.Feed(0UL);
            Assert.Equal(GoldenZero, h.Result);
        }

        // ------------------------------------------------------------ encoding

        [Fact]
        public void test_state_hasher_feed_uint64_is_eight_little_endian_bytes()
        {
            const ulong seed = 0x5EED0004A0000001UL;
            var gen = new SplitMix64(seed);
            ulong[] edge = { 0UL, 1UL, 0xFFUL, 0x100UL, 0x0102030405060708UL, 0x8000000000000000UL, ulong.MaxValue };
            for (int i = 0; i < 1000 + edge.Length; i++)
            {
                ulong v = i < edge.Length ? edge[i] : gen.Next();
                var h = new StateHasher();
                h.Feed(v);
                ulong expected = new FnvOracle().U64(v).Result;
                if (h.Result != expected) Assert.Fail(At(seed, i) + ": Feed(uint64 " + v + ") wrong");
            }
        }

        [Fact]
        public void test_state_hasher_feed_int64_is_eight_byte_twos_complement()
        {
            const ulong seed = 0x5EED0004A0000002UL;
            var gen = new SplitMix64(seed);
            long[] edge = { 0L, 1L, -1L, -2L, long.MinValue, long.MaxValue, -0x0102030405060708L };
            for (int i = 0; i < 1000 + edge.Length; i++)
            {
                long v = i < edge.Length ? edge[i] : unchecked((long)gen.Next());
                var h = new StateHasher();
                h.Feed(v);
                if (h.Result != new FnvOracle().I64(v).Result) Assert.Fail(At(seed, i) + ": Feed(int64 " + v + ") wrong");

                // Same bit pattern, same bytes: the signed and unsigned overloads agree.
                var u = new StateHasher();
                u.Feed(unchecked((ulong)v));
                if (u.Result != h.Result) Assert.Fail(At(seed, i) + ": Feed(int64) and Feed(uint64) of one bit pattern differ");
            }
        }

        [Fact]
        public void test_state_hasher_feed_bool_writes_one_byte_zero_or_one()
        {
            var f = new StateHasher();
            f.Feed(false);
            Assert.Equal(new FnvOracle().Byte(0).Result, f.Result);

            var t = new StateHasher();
            t.Feed(true);
            Assert.Equal(new FnvOracle().Byte(1).Result, t.Result);

            // One byte, not eight: a bool is not widened like an integer.
            var wide = new StateHasher();
            wide.Feed(1UL);
            Assert.NotEqual(wide.Result, t.Result);
        }

        [Fact]
        public void test_state_hasher_feed_fx_equals_feed_of_raw()
        {
            const ulong seed = 0x5EED0004A0000003UL;
            var gen = new SplitMix64(seed);
            Fx[] edge = { Fx.Zero, Fx.One, Fx.MinValue, Fx.MaxValue, Fx.FromRaw(-1L), Fx.FromInt(-3) };
            for (int i = 0; i < 500 + edge.Length; i++)
            {
                Fx v = i < edge.Length ? edge[i] : Fx.FromRaw(unchecked((long)gen.Next()));
                var viaFx = new StateHasher();
                viaFx.Feed(in v);
                var viaRaw = new StateHasher();
                viaRaw.Feed(v.Raw);
                if (viaFx.Result != viaRaw.Result) Assert.Fail(At(seed, i) + ": Feed(Fx) differs from Feed(Raw)");
                if (viaFx.Result != new FnvOracle().I64(v.Raw).Result) Assert.Fail(At(seed, i) + ": Feed(Fx) wrong bytes");
            }
        }

        [Fact]
        public void test_state_hasher_feed_span_prefixes_uint64_length()
        {
            const ulong seed = 0x5EED0004A0000004UL;
            var gen = new SplitMix64(seed);
            for (int i = 0; i < 300; i++)
            {
                byte[] bytes = new byte[gen.Below(70)];
                for (int k = 0; k < bytes.Length; k++) bytes[k] = (byte)gen.Next();
                var h = new StateHasher();
                h.Feed(new ReadOnlySpan<byte>(bytes));
                if (h.Result != new FnvOracle().Span(bytes).Result) Assert.Fail(At(seed, i) + ": span of " + bytes.Length + " bytes wrong");
            }
        }

        [Fact]
        public void test_state_hasher_feed_empty_span_writes_zero_length_only()
        {
            var h = new StateHasher();
            h.Feed(ReadOnlySpan<byte>.Empty);
            var zero = new StateHasher();
            zero.Feed(0UL);
            Assert.Equal(zero.Result, h.Result);
            Assert.NotEqual(GoldenFresh, h.Result);
        }

        [Fact]
        public void test_state_hasher_feed_span_split_differently_changes_hash()
        {
            var whole = new StateHasher();
            whole.Feed(new ReadOnlySpan<byte>(new byte[] { 1, 2, 3 }));
            var split = new StateHasher();
            split.Feed(new ReadOnlySpan<byte>(new byte[] { 1, 2 }));
            split.Feed(new ReadOnlySpan<byte>(new byte[] { 3 }));
            var otherSplit = new StateHasher();
            otherSplit.Feed(new ReadOnlySpan<byte>(new byte[] { 1 }));
            otherSplit.Feed(new ReadOnlySpan<byte>(new byte[] { 2, 3 }));

            Assert.NotEqual(whole.Result, split.Result);
            Assert.NotEqual(split.Result, otherSplit.Result);
            Assert.Equal(new FnvOracle().Span(new byte[] { 1, 2 }).Span(new byte[] { 3 }).Result, split.Result);
        }

        [Fact]
        public void test_state_hasher_mixed_feed_sequences_match_oracle()
        {
            const ulong seed = 0x5EED0004A0000005UL;
            var gen = new SplitMix64(seed);
            for (int i = 0; i < 300; i++)
            {
                var h = new StateHasher();
                var o = new FnvOracle();
                int ops = gen.Below(40);
                for (int op = 0; op < ops; op++)
                {
                    switch (gen.Below(5))
                    {
                        case 0:
                        {
                            ulong v = gen.Next();
                            h.Feed(v);
                            o.U64(v);
                            break;
                        }
                        case 1:
                        {
                            long v = unchecked((long)gen.Next());
                            h.Feed(v);
                            o.I64(v);
                            break;
                        }
                        case 2:
                        {
                            Fx v = Fx.FromRaw(unchecked((long)gen.Next()));
                            h.Feed(in v);
                            o.I64(v.Raw);
                            break;
                        }
                        case 3:
                        {
                            bool v = (gen.Next() & 1UL) == 1UL;
                            h.Feed(v);
                            o.Bool(v);
                            break;
                        }
                        default:
                        {
                            byte[] v = new byte[gen.Below(9)];
                            for (int k = 0; k < v.Length; k++) v[k] = (byte)gen.Next();
                            h.Feed(new ReadOnlySpan<byte>(v));
                            o.Span(v);
                            break;
                        }
                    }
                }
                if (h.Result != o.Result) Assert.Fail(At(seed, i) + ": " + ops + " mixed feeds disagree with the oracle");
            }
        }

        // ------------------------------------------------------------ struct semantics

        [Fact]
        public void test_state_hasher_reading_result_does_not_change_state()
        {
            var read = new StateHasher();
            var unread = new StateHasher();
            for (ulong v = 0; v < 50; v++)
            {
                read.Feed(v);
                unread.Feed(v);
                ulong r1 = read.Result;
                ulong r2 = read.Result;
                Assert.Equal(r1, r2);
            }
            Assert.Equal(unread.Result, read.Result);
            Assert.Equal(FnvOracleOfRange(50), read.Result);
        }

        [Fact]
        public void test_state_hasher_copy_is_an_independent_snapshot()
        {
            // A mutable value type: copying it forks the state (08 §8.9, "an exception to 07 L10").
            var original = new StateHasher();
            original.Feed(1UL);
            StateHasher copy = original;
            copy.Feed(2UL);
            Assert.Equal(FnvOracle.OfU64s(1UL), original.Result);
            Assert.Equal(FnvOracle.OfU64s(1UL, 2UL), copy.Result);
        }

        [Fact]
        public void test_state_hasher_through_interface_matches_struct()
        {
            IStateHasher boxed = new StateHasher();
            boxed.Feed(1UL);
            boxed.Feed(-1L);
            boxed.Feed(true);
            Assert.Equal(GoldenOneMinusOneTrue, boxed.Result);
            Fx one = Fx.One;
            boxed.Feed(in one);
            boxed.Feed(new ReadOnlySpan<byte>(new byte[] { 9 }));
            Assert.Equal(new FnvOracle().U64(1UL).I64(-1L).Bool(true).I64(Fx.One.Raw).Span(new byte[] { 9 }).Result, boxed.Result);
        }

        // ------------------------------------------------------------ budget

        [Fact]
        [Trait("Category", "Budget")]
        public void test_state_hasher_feed_allocates_zero_bytes()
        {
            // T-004 budget: IStateHasher.Feed must not allocate.
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            Fx fx = Fx.FromRaw(123456789L);
            ulong sink = 0;
            for (int i = 0; i < 1000; i++)
            {
                sink ^= FeedAll(payload, fx, (ulong)i);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++)
            {
                sink ^= FeedAll(payload, fx, (ulong)i);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
            Assert.NotEqual(0UL, sink);
        }

        private static ulong FeedAll(byte[] payload, Fx fx, ulong i)
        {
            var h = new StateHasher();
            h.Feed(i);
            h.Feed(-(long)i);
            h.Feed(in fx);
            h.Feed((i & 1UL) == 0UL);
            h.Feed(new ReadOnlySpan<byte>(payload));
            h.Feed(ReadOnlySpan<byte>.Empty);
            return h.Result;
        }

        private static ulong FnvOracleOfRange(int n)
        {
            var o = new FnvOracle();
            for (ulong v = 0; v < (ulong)n; v++)
            {
                o.U64(v);
            }
            return o.Result;
        }
    }
}
