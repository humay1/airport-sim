using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using AirportSim.App.Render.Art2D;
using Xunit;
using Xunit.Abstractions;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 "Packing" and "Rasterisation" (Q-130), and the atlas tests of
    /// §15.18 task 2.
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
        public void test_art2d_atlas_shape_and_packing_match_spec()
        {
            Assert.Equal(2048, Art2DConstants.ATLAS_SIZE);
            Assert.Equal(256, Art2DConstants.LARGE_CELL);
            Assert.Equal(128, Art2DConstants.SMALL_CELL);
            Assert.Equal(5, Art2DConstants.ATLAS_MIP_COUNT);
            Assert.Equal(1024, Art2DConstants.ART_UNITS);

            Assert.Equal(ArtCells.AtlasSize, _atlas.Size);
            Assert.NotNull(_atlas.Mips);
            Assert.Equal(ArtCells.MipCount, _atlas.Mips.Count);
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                int side = ArtCells.AtlasSize >> m;
                Assert.True(_atlas.Mips[m] != null, "mip " + m + " is null");
                Assert.True(side * side * 4 == _atlas.Mips[m].Length, "mip " + m + " has " + _atlas.Mips[m].Length + " bytes, expected (2048 >> m)² × 4 = " + (side * side * 4));
            }

            // Every rect, exactly, through LayersOf: each non-logo layer's Rect is
            // its cell's visible square (15 §15.17 "Rects").
            foreach (VisualId v in ArtTable.AllVisuals())
            {
                List<ArtLayer> want = ArtTable.Layers(v);
                IReadOnlyList<ArtLayer> got = Art2DFactory.LayersOf(v);
                Assert.True(want.Count == got.Count, v + ": " + got.Count + " layers, expected " + want.Count);
                for (int i = 0; i < want.Count; i++)
                {
                    if (want[i].IsLogo)
                    {
                        continue;
                    }

                    Assert.True(
                        ArtCells.Show(want[i].Rect) == ArtCells.Show(got[i].Rect),
                        v + " layer " + i + ": rect " + ArtCells.Show(got[i].Rect) + ", expected " + ArtCells.Show(want[i].Rect));
                }
            }

            // Spelled out for two cells: Solid (slot 0) and AircraftC's Status (column 0, row 2).
            AssertRect(4, 1540, 124, 1660, Art2DFactory.LayersOf(VisualId.Apron)[0].Rect, "Solid");
            AssertRect(8, 520, 248, 760, Art2DFactory.LayersOf(VisualId.AircraftC)[0].Rect, "AircraftC Status");
            AssertRect(1544, 1288, 1784, 1528, Art2DFactory.LayersOf(VisualId.AircraftF)[7].Rect, "AircraftF Glazing");

            // The logo marks are slots 28 to 34; None has no cell.
            for (int k = 1; k <= 7; k++)
            {
                var mark = (LogoMark)k;
                AtlasRect want = ArtCells.RectOf(ArtCells.Logo(mark));
                AtlasRect got = Art2DFactory.LogoRect(mark);
                Assert.True(ArtCells.Show(want) == ArtCells.Show(got), "LogoRect(" + mark + ") is " + ArtCells.Show(got) + ", expected " + ArtCells.Show(want));
            }

            AssertRect(1540, 1668, 1660, 1788, Art2DFactory.LogoRect(LogoMark.Disc), "LogoRect(Disc), slot 28");
            AssertRect(260, 1796, 380, 1916, Art2DFactory.LogoRect(LogoMark.Crescent), "LogoRect(Crescent), slot 34");
            Assert.Throws<ArgumentOutOfRangeException>(() => Art2DFactory.LogoRect(LogoMark.None));

            // Every pixel outside a cell is transparent with all bytes 0, at every mip.
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                int side = ArtCells.AtlasSize >> m;
                byte[] mip = _atlas.Mips[m];
                long bad = 0;
                string first = string.Empty;
                for (int y = 0; y < side; y++)
                {
                    for (int x = 0; x < side; x++)
                    {
                        if (ArtCells.InAnyCell(m, x, y))
                        {
                            continue;
                        }

                        int o = ArtCells.Offset(side, x, y);
                        if ((mip[o] | mip[o + 1] | mip[o + 2] | mip[o + 3]) != 0)
                        {
                            if (bad == 0)
                            {
                                first = string.Format(CultureInfo.InvariantCulture, "({0},{1}) = ({2},{3},{4},{5})", x, y, mip[o], mip[o + 1], mip[o + 2], mip[o + 3]);
                            }

                            bad++;
                        }
                    }
                }

                Assert.True(bad == 0, "mip " + m + ": " + bad + " non-zero pixels outside every cell, first " + first);
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
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                Assert.False(ReferenceEquals(a.Mips[m], b.Mips[m]), "mip " + m + " is the same array in two builds");
            }

            // Writing into one build reaches no later build.
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                Array.Clear(a.Mips[m], 0, a.Mips[m].Length);
            }

            SpriteAtlas c = Art2DFactory.BuildAtlas();
            AssertSameBytes(b, c, "a build after another build was overwritten");
        }

        [Fact]
        public void test_art2d_every_cell_is_drawn_inside_its_border()
        {
            var failures = new List<string>();
            foreach (ArtCell cell in ArtCells.All())
            {
                for (int m = 0; m < ArtCells.MipCount; m++)
                {
                    int side = ArtCells.AtlasSize >> m;
                    byte[] mip = _atlas.Mips[m];
                    int x0 = cell.Px >> m;
                    int y0 = cell.Py >> m;
                    int c = cell.Side >> m;
                    int maxAlpha = 0;
                    for (int j = 0; j < c; j++)
                    {
                        for (int i = 0; i < c; i++)
                        {
                            maxAlpha = Math.Max(maxAlpha, mip[ArtCells.Offset(side, x0 + i, y0 + j) + 3]);
                        }
                    }

                    // Alpha ≥ 128 somewhere at mips 0 to 2, and > 0 at mips 3 and 4.
                    int need = m <= 2 ? 128 : 1;
                    if (maxAlpha < need)
                    {
                        failures.Add(cell.Name + " mip " + m + ": max alpha " + maxAlpha + ", needs " + need);
                    }
                }

                // Every cell except the four edge-to-edge ones has an all-transparent
                // border ring of c/32 pixels at mip 0.
                if (!cell.EdgeToEdge)
                {
                    byte[] mip0 = _atlas.Mips[0];
                    int b = cell.Border;
                    int c = cell.Side;
                    int opaque = 0;
                    string first = string.Empty;
                    for (int j = 0; j < c; j++)
                    {
                        for (int i = 0; i < c; i++)
                        {
                            bool ring = i < b || j < b || i >= c - b || j >= c - b;
                            if (!ring)
                            {
                                continue;
                            }

                            byte alpha = mip0[ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j) + 3];
                            if (alpha != 0)
                            {
                                if (opaque == 0)
                                {
                                    first = "(" + i + "," + j + ") alpha " + alpha;
                                }

                                opaque++;
                            }
                        }
                    }

                    if (opaque != 0)
                    {
                        failures.Add(cell.Name + " mip 0: " + opaque + " border texels with alpha > 0, first " + first);
                    }
                }
            }

            // Solid is (255, 255, 255, 255) on every texel of its cell at every mip.
            ArtCell solid = ArtCells.Small("Solid");
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                int side = ArtCells.AtlasSize >> m;
                byte[] mip = _atlas.Mips[m];
                int c = solid.Side >> m;
                int wrong = 0;
                for (int j = 0; j < c; j++)
                {
                    for (int i = 0; i < c; i++)
                    {
                        int o = ArtCells.Offset(side, (solid.Px >> m) + i, (solid.Py >> m) + j);
                        if (mip[o] != 255 || mip[o + 1] != 255 || mip[o + 2] != 255 || mip[o + 3] != 255)
                        {
                            wrong++;
                        }
                    }
                }

                if (wrong != 0)
                {
                    failures.Add("Solid mip " + m + ": " + wrong + " texels not (255,255,255,255)");
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void test_art2d_aircraft_follow_the_proportion_table()
        {
            byte[] mip0 = _atlas.Mips[0];
            var widths = new int[6];
            var heights = new int[6];
            for (int s = 0; s < 6; s++)
            {
                ArtCell status = ArtCells.Aircraft(s, 0);
                int minI = int.MaxValue;
                int maxI = int.MinValue;
                int minJ = int.MaxValue;
                int maxJ = int.MinValue;
                for (int j = 0; j < status.Side; j++)
                {
                    for (int i = 0; i < status.Side; i++)
                    {
                        if (mip0[ArtCells.Offset(ArtCells.AtlasSize, status.Px + i, status.Py + j) + 3] >= 128)
                        {
                            minI = Math.Min(minI, i);
                            maxI = Math.Max(maxI, i);
                            minJ = Math.Min(minJ, j);
                            maxJ = Math.Max(maxJ, j);
                        }
                    }
                }

                Assert.True(minI <= maxI, status.Name + " has no texel with alpha ≥ 128");
                widths[s] = maxI - minI + 1;
                heights[s] = maxJ - minJ + 1;

                // Width span × 240 and height length × 240, each ± 3 pixels (in hundredths: ± 300).
                int spanH = ArtTable.SpanHundredths[s];
                int lenH = ArtTable.LengthHundredths[s];
                Assert.True(
                    Math.Abs((100 * widths[s]) - (spanH * 240)) <= 300,
                    string.Format(CultureInfo.InvariantCulture, "{0}: status width {1} px, expected {2} × 240 = {3} ± 3", status.Name, widths[s], spanH / 100.0, spanH * 2.4));
                Assert.True(
                    Math.Abs((100 * heights[s]) - (lenH * 240)) <= 300,
                    string.Format(CultureInfo.InvariantCulture, "{0}: status height {1} px, expected {2} × 240 = {3} ± 3", status.Name, heights[s], lenH / 100.0, lenH * 2.4));
            }

            // Both grow strictly from A to F.
            for (int s = 1; s < 6; s++)
            {
                Assert.True(widths[s] > widths[s - 1], "status width does not grow from " + ArtCells.SizeNames[s - 1] + " (" + widths[s - 1] + ") to " + ArtCells.SizeNames[s] + " (" + widths[s] + ")");
                Assert.True(heights[s] > heights[s - 1], "status height does not grow from " + ArtCells.SizeNames[s - 1] + " (" + heights[s - 1] + ") to " + ArtCells.SizeNames[s] + " (" + heights[s] + ")");
            }

            // Each aircraft cell is mirror-symmetric about its vertical centre line
            // within ± 1 per byte.
            var failures = new List<string>();
            for (int s = 0; s < 6; s++)
            {
                for (int l = 0; l < 7; l++)
                {
                    ArtCell cell = ArtCells.Aircraft(s, l);
                    int worst = 0;
                    string where = string.Empty;
                    for (int j = 0; j < cell.Side; j++)
                    {
                        for (int i = 0; i < cell.Side / 2; i++)
                        {
                            int a = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j);
                            int b = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + cell.Side - 1 - i, cell.Py + j);
                            for (int ch = 0; ch < 4; ch++)
                            {
                                int d = Math.Abs(mip0[a + ch] - mip0[b + ch]);
                                if (d > worst)
                                {
                                    worst = d;
                                    where = "(" + i + "," + j + ") channel " + ch;
                                }
                            }
                        }
                    }

                    if (worst > 1)
                    {
                        failures.Add(cell.Name + ": mirror difference " + worst + " at " + where);
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        /// <summary>
        /// For inspection by eye, not a 15 §15.18 test. Off by default; with
        /// AIRPORTSIM_DUMP_ATLAS set it writes mip 0 as an RGBA PNG (top row
        /// first, straight alpha as stored) to the test output directory.
        /// </summary>
        [Fact]
        public void test_art2d_atlas_dump_png_when_requested()
        {
            Assert.Equal(ArtCells.AtlasSize, _atlas.Size);
            Assert.Equal(ArtCells.AtlasSize * ArtCells.AtlasSize * 4, _atlas.Mips[0].Length);
            string? flag = Environment.GetEnvironmentVariable(DumpVariable);
            if (string.IsNullOrEmpty(flag))
            {
                return;
            }

            string path = Path.Combine(AppContext.BaseDirectory, "art2d-atlas-mip0.png");
            File.WriteAllBytes(path, Png.Encode(_atlas.Mips[0], _atlas.Size));
            _output.WriteLine("atlas mip 0 written to " + path);
        }

        private static void AssertRect(int u0, int v0, int u1, int v1, in AtlasRect got, string what)
        {
            var want = new AtlasRect((float)(u0 / 2048.0), (float)(v0 / 2048.0), (float)(u1 / 2048.0), (float)(v1 / 2048.0));
            Assert.True(ArtCells.Show(want) == ArtCells.Show(got), what + ": " + ArtCells.Show(got) + ", expected " + ArtCells.Show(want));
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
