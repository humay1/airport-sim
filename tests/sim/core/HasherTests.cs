using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// StateHasher, 08 §8.9 "Encoding, the concrete hasher and the core
    /// section" (Q-017). The four golden vectors are literals from the spec;
    /// everything else is checked against FnvOracle, which is written from the
    /// same section's byte-level definition.
    /// </summary>
    public sealed class HasherTests
    {
        [Fact]
        public void test_hasher_fresh_result_is_offset_basis()
        {
            var fresh = new StateHasher();
            StateHasher dflt = default;
            Assert.Equal(0xCBF29CE484222325UL, fresh.Result);
            Assert.Equal(0xCBF29CE484222325UL, dflt.Result);
        }

        [Fact]
        public void test_hasher_feed_zero_ulong_matches_golden()
        {
            var h = new StateHasher();
            h.Feed(0UL);
            Assert.Equal(0xA8C7F832281A39C5UL, h.Result);
        }

        [Fact]
        public void test_hasher_feed_ulong_long_bool_matches_golden()
        {
            var h = new StateHasher();
            h.Feed(1UL);
            h.Feed(-1L);
            h.Feed(true);
            Assert.Equal(0x9185A69DA7E88AC7UL, h.Result);
        }

        [Fact]
        public void test_hasher_feed_span_matches_golden()
        {
            var h = new StateHasher();
            h.Feed((ReadOnlySpan<byte>)new byte[] { 1, 2, 3 });
            Assert.Equal(0x01EF76D429B11552UL, h.Result);
        }

        [Fact]
        public void test_hasher_default_and_new_hash_identically()
        {
            var a = new StateHasher();
            StateHasher b = default;
            a.Feed(0x0102030405060708UL);
            b.Feed(0x0102030405060708UL);
            Assert.Equal(a.Result, b.Result);
            Assert.Equal(new FnvOracle().U64(0x0102030405060708UL).Result, a.Result);
        }

        [Fact]
        public void test_hasher_reading_result_does_not_change_state()
        {
            var h = new StateHasher();
            h.Feed(42UL);
            ulong r1 = h.Result;
            ulong r2 = h.Result;
            h.Feed(43UL);

            var once = new StateHasher();
            once.Feed(42UL);
            once.Feed(43UL);

            Assert.Equal(r1, r2);
            Assert.Equal(once.Result, h.Result);
        }

        [Fact]
        public void test_hasher_feed_ulong_is_eight_bytes_little_endian()
        {
            var h = new StateHasher();
            h.Feed(0x0102030405060708UL);
            var o = new FnvOracle().Byte(8).Byte(7).Byte(6).Byte(5).Byte(4).Byte(3).Byte(2).Byte(1);
            Assert.Equal(o.Result, h.Result);
        }

        [Fact]
        public void test_hasher_feed_long_is_twos_complement_little_endian()
        {
            var signed = new StateHasher();
            signed.Feed(-2L);
            var unsignedTwin = new StateHasher();
            unsignedTwin.Feed(0xFFFFFFFFFFFFFFFEUL);
            Assert.Equal(unsignedTwin.Result, signed.Result);
            Assert.Equal(new FnvOracle().Byte(0xFE).Byte(0xFF).Byte(0xFF).Byte(0xFF).Byte(0xFF).Byte(0xFF).Byte(0xFF).Byte(0xFF).Result, signed.Result);

            var min = new StateHasher();
            min.Feed(long.MinValue);
            Assert.Equal(new FnvOracle().I64(long.MinValue).Result, min.Result);
        }

        [Fact]
        public void test_hasher_feed_bool_writes_one_byte()
        {
            var f = new StateHasher();
            f.Feed(false);
            var t = new StateHasher();
            t.Feed(true);
            var zero = new StateHasher();
            zero.Feed(0UL);

            Assert.Equal(new FnvOracle().Byte(0).Result, f.Result);
            Assert.Equal(new FnvOracle().Byte(1).Result, t.Result);
            Assert.NotEqual(zero.Result, f.Result);
        }

        [Fact]
        public void test_hasher_feed_fx_feeds_raw()
        {
            long[] raws = { 0L, 1L, -1L, 1L << 32, long.MinValue, long.MaxValue, -6442450944L };
            foreach (long raw in raws)
            {
                Fx v = Fx.FromRaw(raw);
                var viaFx = new StateHasher();
                viaFx.Feed(v);
                var viaRaw = new StateHasher();
                viaRaw.Feed(raw);
                Assert.True(viaRaw.Result == viaFx.Result, $"Feed(Fx.FromRaw({raw})) differs from Feed({raw}L)");
            }
        }

        [Fact]
        public void test_hasher_feed_span_is_length_prefixed()
        {
            var whole = new StateHasher();
            whole.Feed((ReadOnlySpan<byte>)new byte[] { 1, 2, 3 });
            var split = new StateHasher();
            split.Feed((ReadOnlySpan<byte>)new byte[] { 1, 2 });
            split.Feed((ReadOnlySpan<byte>)new byte[] { 3 });
            Assert.NotEqual(whole.Result, split.Result);
            Assert.Equal(new FnvOracle().Span(new byte[] { 1, 2 }).Span(new byte[] { 3 }).Result, split.Result);

            var empty = new StateHasher();
            empty.Feed(ReadOnlySpan<byte>.Empty);
            var zero = new StateHasher();
            zero.Feed(0UL);
            Assert.Equal(zero.Result, empty.Result);
        }

        [Fact]
        public void test_hasher_matches_byte_oracle_property()
        {
            const ulong seed = 0x4A54_4853UL;
            var rng = new SplitMix64(seed);
            for (int i = 0; i < 500; i++)
            {
                var h = new StateHasher();
                var o = new FnvOracle();
                int ops = (int)(rng.Next() % 12UL);
                for (int k = 0; k < ops; k++)
                {
                    ulong r = rng.Next();
                    switch (rng.Next() % 5UL)
                    {
                        case 0:
                            h.Feed(r);
                            o.U64(r);
                            break;
                        case 1:
                            h.Feed(unchecked((long)r));
                            o.I64(unchecked((long)r));
                            break;
                        case 2:
                            h.Feed((r & 1UL) == 1UL);
                            o.Bool((r & 1UL) == 1UL);
                            break;
                        case 3:
                            h.Feed(Fx.FromRaw(unchecked((long)r)));
                            o.I64(unchecked((long)r));
                            break;
                        default:
                            var bytes = new byte[(int)(r % 40UL)];
                            for (int j = 0; j < bytes.Length; j++)
                            {
                                bytes[j] = (byte)rng.Next();
                            }

                            h.Feed((ReadOnlySpan<byte>)bytes);
                            o.Span(bytes);
                            break;
                    }
                }

                Assert.True(o.Result == h.Result, $"seed {seed:X}, iteration {i}: {h.Result:X16} != oracle {o.Result:X16}");
            }
        }

        [Fact]
        public void test_hasher_through_interface_matches_struct()
        {
            IStateHasher boxed = new StateHasher();
            boxed.Feed(1UL);
            boxed.Feed(-1L);
            boxed.Feed(true);
            Assert.Equal(0x9185A69DA7E88AC7UL, boxed.Result);
        }

        [Fact]
        public void test_hasher_copy_is_an_independent_snapshot()
        {
            var h = new StateHasher();
            h.Feed(5UL);
            StateHasher snapshot = h;
            h.Feed(6UL);
            Assert.Equal(new FnvOracle().U64(5UL).Result, snapshot.Result);
            Assert.Equal(new FnvOracle().U64(5UL).U64(6UL).Result, h.Result);
        }
    }
}
