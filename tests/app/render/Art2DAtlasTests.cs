using System;
using System.IO;
using System.IO.Compression;
using AirportSim.App.Render.Art2D;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 SpriteAtlas (Q-130): the mip layout given Art2DConstants, and
    /// determinism. The packing, border and per-cell tests of §15.18 wait for
    /// the realistic-2D amendment of the style.
    /// </summary>
    public sealed class Art2DAtlasTests : IClassFixture<Art2DAtlasFixture>
    {
        /// <summary>When set to a non-empty value, the dump test writes mip 0 as a PNG.</summary>
        public const string DumpVariable = "AIRPORTSIM_DUMP_ATLAS";

        private readonly SpriteAtlas _atlas;
        private readonly ITestOutputHelper _output;

        public Art2DAtlasTests(Art2DAtlasFixture fixture, ITestOutputHelper output)
        {
            _atlas = fixture.Atlas;
            _output = output;
        }

        [Fact]
        public void test_art2d_atlas_mip_layout_follows_the_constants()
        {
            // Size is ATLAS_SIZE; Mips has ATLAS_MIP_COUNT entries; Mips[m] is
            // RGBA32 of (Size >> m)² × 4 bytes.
            Assert.Equal(Art2DConstants.ATLAS_SIZE, _atlas.Size);
            Assert.True(Art2DConstants.ATLAS_MIP_COUNT >= 1, "ATLAS_MIP_COUNT is at least 1");
            Assert.True(Art2DConstants.ATLAS_SIZE >> (Art2DConstants.ATLAS_MIP_COUNT - 1) >= 1, "the last mip has at least one pixel");
            Assert.NotNull(_atlas.Mips);
            Assert.Equal(Art2DConstants.ATLAS_MIP_COUNT, _atlas.Mips.Count);
            for (int m = 0; m < Art2DConstants.ATLAS_MIP_COUNT; m++)
            {
                long side = _atlas.Size >> m;
                Assert.True(_atlas.Mips[m] != null, "mip " + m + " is null");
                Assert.True(side * side * 4 == _atlas.Mips[m].Length, "mip " + m + " has " + _atlas.Mips[m].Length + " bytes, expected (Size >> m)² × 4 = " + (side * side * 4));
                for (int k = 0; k < m; k++)
                {
                    Assert.False(ReferenceEquals(_atlas.Mips[k], _atlas.Mips[m]), "mips " + k + " and " + m + " share one array");
                }
            }

            // Every mip is rasterised (none is left blank): some texel is drawn.
            for (int m = 0; m < Art2DConstants.ATLAS_MIP_COUNT; m++)
            {
                byte[] mip = _atlas.Mips[m];
                bool drawn = false;
                for (int i = 3; i < mip.Length && !drawn; i += 4)
                {
                    drawn = mip[i] != 0;
                }

                Assert.True(drawn, "mip " + m + " has no texel with alpha > 0");
            }
        }

        [Fact]
        public void test_art2d_atlas_is_deterministic()
        {
            SpriteAtlas a = Art2DFactory.BuildAtlas();
            SpriteAtlas b = Art2DFactory.BuildAtlas();
            AssertSameBytes(a, b, "two builds");
            AssertSameBytes(_atlas, a, "the shared build and a fresh one");

            // Each call returns fresh arrays.
            Assert.NotSame(a.Mips, b.Mips);
            for (int m = 0; m < a.Mips.Count; m++)
            {
                Assert.False(ReferenceEquals(a.Mips[m], b.Mips[m]), "mip " + m + " is the same array in two builds");
            }

            // Writing into one build reaches no later build.
            for (int m = 0; m < a.Mips.Count; m++)
            {
                Array.Clear(a.Mips[m], 0, a.Mips[m].Length);
            }

            SpriteAtlas c = Art2DFactory.BuildAtlas();
            AssertSameBytes(b, c, "a build after another build was overwritten");
        }

        /// <summary>
        /// For inspection by eye, not a 15 §15.18 test. Off by default; with
        /// AIRPORTSIM_DUMP_ATLAS set it writes mip 0 as an RGBA PNG (top row
        /// first, straight alpha as stored) to the test output directory.
        /// </summary>
        [Fact]
        public void test_art2d_atlas_dump_png_when_requested()
        {
            Assert.Equal(Art2DConstants.ATLAS_SIZE, _atlas.Size);
            Assert.Equal((long)_atlas.Size * _atlas.Size * 4, _atlas.Mips[0].LongLength);
            string? flag = Environment.GetEnvironmentVariable(DumpVariable);
            if (string.IsNullOrEmpty(flag))
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "art2d-atlas-mip0.png");
            File.WriteAllBytes(path, Png.Encode(_atlas.Mips[0], _atlas.Size));
            _output.WriteLine("atlas mip 0 written to " + path);
        }

        private static void AssertSameBytes(in SpriteAtlas a, in SpriteAtlas b, string what)
        {
            Assert.Equal(a.Size, b.Size);
            Assert.Equal(a.Mips.Count, b.Mips.Count);
            for (int m = 0; m < a.Mips.Count; m++)
            {
                byte[] x = a.Mips[m];
                byte[] y = b.Mips[m];
                Assert.True(x.Length == y.Length, what + ": mip " + m + " lengths " + x.Length + " and " + y.Length);
                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i])
                    {
                        Assert.Fail(what + ": mip " + m + " differs first at byte " + i + " (" + x[i] + " vs " + y[i] + ")");
                    }
                }
            }
        }
    }

    /// <summary>A minimal PNG writer (RGBA, 8 bits, no filter) for the atlas dump only.</summary>
    internal static class Png
    {
        public static byte[] Encode(byte[] rgbaBottomUp, int side)
        {
            var raw = new byte[side * ((side * 4) + 1)];
            for (int row = 0; row < side; row++)
            {
                int src = (side - 1 - row) * side * 4;
                int dst = row * ((side * 4) + 1);
                raw[dst] = 0;
                Buffer.BlockCopy(rgbaBottomUp, src, raw, dst + 1, side * 4);
            }

            byte[] idat;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true))
                {
                    z.Write(raw, 0, raw.Length);
                }

                idat = ms.ToArray();
            }

            using (var png = new MemoryStream())
            {
                png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                WriteBig(ihdr, 0, (uint)side);
                WriteBig(ihdr, 4, (uint)side);
                ihdr[8] = 8;
                ihdr[9] = 6;
                Chunk(png, "IHDR", ihdr);
                Chunk(png, "IDAT", idat);
                Chunk(png, "IEND", Array.Empty<byte>());
                return png.ToArray();
            }
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            WriteBig(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++)
            {
                body[i] = (byte)type[i];
            }

            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            s.Write(body, 0, body.Length);
            var crc = new byte[4];
            WriteBig(crc, 0, Crc32(body));
            s.Write(crc, 0, 4);
        }

        private static uint Crc32(byte[] bytes)
        {
            uint c = 0xFFFFFFFFU;
            foreach (byte b in bytes)
            {
                c ^= b;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1U) != 0 ? 0xEDB88320U ^ (c >> 1) : c >> 1;
                }
            }

            return c ^ 0xFFFFFFFFU;
        }

        private static void WriteBig(byte[] buffer, int at, uint value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }
    }
}
