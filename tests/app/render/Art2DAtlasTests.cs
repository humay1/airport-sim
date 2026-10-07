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
    /// 15 §15.17 "Packing", "Rasterisation" and the style guide's testable rules
    /// (Q-130, as amended by Q-131), and the atlas tests of §15.18 task 2. The
    /// atlas is built once for the class (§15.18).
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
            Assert.Equal(ArtCells.AtlasSize, Art2DConstants.ATLAS_SIZE);
            Assert.Equal(ArtCells.MipCount, Art2DConstants.ATLAS_MIP_COUNT);
            Assert.Equal(ArtCells.AtlasSize, _atlas.Size);
            Assert.NotNull(_atlas.Mips);
            Assert.Equal(ArtCells.MipCount, _atlas.Mips.Count);
            for (int m = 0; m < ArtCells.MipCount; m++)
            {
                long side = ArtCells.AtlasSize >> m;
                Assert.True(_atlas.Mips[m] != null, "mip " + m + " is null");
                Assert.True(side * side * 4 == _atlas.Mips[m].LongLength, "mip " + m + " has " + _atlas.Mips[m].LongLength + " bytes, expected (4096 >> m)² × 4 = " + (side * side * 4));
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
                        ArtShow.Rect(want[i].Rect) == ArtShow.Rect(got[i].Rect),
                        v + " layer " + i + ": rect " + ArtShow.Rect(got[i].Rect) + ", expected " + ArtShow.Rect(want[i].Rect));
                }
            }

            // Spelled out: a small cell, two aircraft cells, the ground, Rubber (row 2 of the small band).
            AssertRect(4, 3588, 124, 3708, Art2DFactory.LayersOf(VisualId.QueueFill)[0].Rect, "Solid, small slot 0");
            AssertRect(528, 1040, 1008, 1520, Art2DFactory.LayersOf(VisualId.AircraftC)[1].Rect, "AircraftC Status, column 1 row 2");
            AssertRect(3600, 2576, 4080, 3056, Art2DFactory.LayersOf(VisualId.AircraftF)[8].Rect, "AircraftF Glazing, column 7 row 5");
            AssertRect(1040, 3088, 1520, 3568, Art2DFactory.GroundLayer().Rect, "GroundLayer, Grass, band slot 2");
            AssertRect(516, 3716, 636, 3836, Art2DFactory.LayersOf(VisualId.RunwaySurface)[1].Rect, "Rubber, small slot 36");

            // The logo marks are small slots 28 to 34; None has no cell.
            for (int k = 1; k <= 7; k++)
            {
                var mark = (LogoMark)k;
                AtlasRect want = ArtCells.RectOf(ArtCells.Logo(mark));
                AtlasRect got = Art2DFactory.LogoRect(mark);
                Assert.True(ArtShow.Rect(want) == ArtShow.Rect(got), "LogoRect(" + mark + ") is " + ArtShow.Rect(got) + ", expected " + ArtShow.Rect(want));
            }

            AssertRect(3588, 3588, 3708, 3708, Art2DFactory.LogoRect(LogoMark.Disc), "LogoRect(Disc), slot 28");
            AssertRect(260, 3716, 380, 3836, Art2DFactory.LogoRect(LogoMark.Crescent), "LogoRect(Crescent), slot 34");
            Assert.Throws<ArgumentOutOfRangeException>(() => Art2DFactory.LogoRect(LogoMark.None));

            // Every pixel outside a cell, including the empty large band slot 7,
            // is transparent with all bytes 0, at every mip.
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
        public void test_art2d_every_cell_is_drawn_inside_its_border()
        {
            var lowAlpha = new HashSet<string> { "SoftBox", "SoftBar", "Rubber" };
            var thinAtMip2 = new Dictionary<string, int> { { "RunwayEdgeLines", 64 } };
            for (int s = 0; s < 6; s++)
            {
                lowAlpha.Add(ArtCells.Aircraft(s, 0).Name);
                thinAtMip2.Add(ArtCells.Aircraft(s, 5).Name, 1);
                thinAtMip2.Add(ArtCells.Aircraft(s, 7).Name, 1);
            }

            var failures = new List<string>();
            foreach (ArtCell cell in ArtCells.All())
            {
                for (int m = 0; m < ArtCells.MipCount; m++)
                {
                    int maxAlpha = MaxAlpha(cell, m);

                    // Alpha ≥ 128 (≥ 64 for the shadow cells and Rubber) somewhere at
                    // mips 0 to 2, and > 0 at mips 3 to 5. At mip 2 only (Q-133), the
                    // parts narrower than a pixel: RunwayEdgeLines ≥ 64, and the
                    // Cheatline and Glazing cells > 0.
                    int need = m <= 2 ? (lowAlpha.Contains(cell.Name) ? 64 : 128) : 1;
                    if (m == 2 && thinAtMip2.TryGetValue(cell.Name, out int thin))
                    {
                        need = thin;
                    }
                    if (maxAlpha < need)
                    {
                        failures.Add(cell.Name + " mip " + m + ": max alpha " + maxAlpha + ", needs " + need);
                    }
                }

                // Every cell that is not edge-to-edge has an all-transparent border
                // ring of c/32 pixels at mip 0.
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
            ArtCell solid = ArtCells.Named("Solid");
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
        public void test_art2d_tiled_textures_follow_the_style_guide()
        {
            // 15 §15.17 "Tiled textures": at mip 0, over each tiled cell's visible
            // square, every texel is opaque and grey, the values lie in the cell's
            // range and their mean in its mean range, the standard deviation is at
            // least 3, and each border texel equals the texel one visible side away.
            var ranges = new Dictionary<string, int[]>
            {
                { "Asphalt", new[] { 200, 255, 228, 244 } },
                { "Concrete", new[] { 160, 255, 220, 240 } },
                { "Grass", new[] { 190, 255, 220, 236 } },
                { "Roof", new[] { 140, 255, 215, 240 } },
            };

            var failures = new List<string>();
            byte[] mip0 = _atlas.Mips[0];
            foreach (string name in ArtCells.TiledNames)
            {
                ArtCell cell = ArtCells.Named(name);
                int[] r = ranges[name];
                int b = cell.Border;
                int c = cell.Side;
                int visible = c - (2 * b);
                long n = 0;
                long sum = 0;
                long sumSq = 0;
                int notOpaque = 0;
                int notGrey = 0;
                int outOfRange = 0;
                int lo = 255;
                int hi = 0;
                for (int j = b; j < c - b; j++)
                {
                    for (int i = b; i < c - b; i++)
                    {
                        int o = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j);
                        int v = mip0[o];
                        if (mip0[o + 3] != 255)
                        {
                            notOpaque++;
                        }

                        if (mip0[o + 1] != v || mip0[o + 2] != v)
                        {
                            notGrey++;
                        }

                        if (v < r[0] || v > r[1])
                        {
                            outOfRange++;
                        }

                        lo = Math.Min(lo, v);
                        hi = Math.Max(hi, v);
                        n++;
                        sum += v;
                        sumSq += (long)v * v;
                    }
                }

                if (notOpaque != 0)
                {
                    failures.Add(name + ": " + notOpaque + " texels with alpha below 255");
                }

                if (notGrey != 0)
                {
                    failures.Add(name + ": " + notGrey + " texels with R, G, B not equal");
                }

                if (outOfRange != 0)
                {
                    failures.Add(name + ": " + outOfRange + " values outside " + r[0] + "–" + r[1] + " (min " + lo + ", max " + hi + ")");
                }

                if (sum < r[2] * n || sum > r[3] * n)
                {
                    failures.Add(string.Format(CultureInfo.InvariantCulture, "{0}: mean {1:F2} outside {2}–{3}", name, sum / (double)n, r[2], r[3]));
                }

                // Standard deviation ≥ 3 ⇔ n Σv² − (Σv)² ≥ 9 n², in long.
                if ((n * sumSq) - (sum * sum) < 9 * n * n)
                {
                    failures.Add(name + ": standard deviation below 3, the texture is flat");
                }

                // Periodic: each border texel equals, byte for byte, the texel one visible side away.
                int mismatched = 0;
                string first = string.Empty;
                for (int j = 0; j < c; j++)
                {
                    for (int i = 0; i < c; i++)
                    {
                        bool border = i < b || j < b || i >= c - b || j >= c - b;
                        if (!border)
                        {
                            continue;
                        }

                        int pi = i < b ? i + visible : (i >= c - b ? i - visible : i);
                        int pj = j < b ? j + visible : (j >= c - b ? j - visible : j);
                        int o1 = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j);
                        int o2 = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + pi, cell.Py + pj);
                        for (int ch = 0; ch < 4; ch++)
                        {
                            if (mip0[o1 + ch] != mip0[o2 + ch])
                            {
                                if (mismatched == 0)
                                {
                                    first = "(" + i + "," + j + ") vs (" + pi + "," + pj + ")";
                                }

                                mismatched++;
                                break;
                            }
                        }
                    }
                }

                if (mismatched != 0)
                {
                    failures.Add(name + ": " + mismatched + " border texels differ from the texel one visible side away, first " + first);
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void test_art2d_cells_follow_the_value_and_alpha_rules()
        {
            var failures = new List<string>();
            byte[] mip0 = _atlas.Mips[0];

            // Value steps: every role or region cell (all but the fixed ones) has
            // R ≥ 140 wherever alpha > 0, or ≥ 70 in ControlTower.
            var fixedCells = new HashSet<string> { "SoftBox", "SoftBar", "Rubber", "StandPad", "GseBody", "GseDetail" };
            for (int s = 0; s < 6; s++)
            {
                fixedCells.Add(ArtCells.Aircraft(s, 0).Name);
                fixedCells.Add(ArtCells.Aircraft(s, 2).Name);
                fixedCells.Add(ArtCells.Aircraft(s, 7).Name);
            }

            foreach (ArtCell cell in ArtCells.All())
            {
                if (fixedCells.Contains(cell.Name))
                {
                    continue;
                }

                int floor = cell.Name == "ControlTower" ? 70 : 140;
                int dark = 0;
                string first = string.Empty;
                for (int j = 0; j < cell.Side; j++)
                {
                    for (int i = 0; i < cell.Side; i++)
                    {
                        int o = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j);
                        if (mip0[o + 3] > 0 && mip0[o] < floor)
                        {
                            if (dark == 0)
                            {
                                first = "(" + i + "," + j + ") R " + mip0[o] + " alpha " + mip0[o + 3];
                            }

                            dark++;
                        }
                    }
                }

                if (dark != 0)
                {
                    failures.Add("value steps: " + cell.Name + " has " + dark + " drawn texels with R below " + floor + ", first " + first);
                }
            }

            // Worn floor: CentreStripe for sample x in 488..536, RunwayEdgeLines for
            // x in 24..40 or 984..1000, every texel has alpha 215 to 255.
            WornFloor(failures, ArtCells.Named("CentreStripe"), new[] { 488, 536 });
            WornFloor(failures, ArtCells.Named("RunwayEdgeLines"), new[] { 24, 40, 984, 1000 });

            // Shadow alpha: the texel at pixel (c/2, c/2) has alpha 112.
            var shadows = new List<ArtCell> { ArtCells.Named("SoftBox"), ArtCells.Named("SoftBar") };
            for (int s = 0; s < 6; s++)
            {
                shadows.Add(ArtCells.Aircraft(s, 0));
            }

            foreach (ArtCell cell in shadows)
            {
                int alpha = mip0[ArtCells.Offset(ArtCells.AtlasSize, cell.Px + (cell.Side / 2), cell.Py + (cell.Side / 2)) + 3];
                if (alpha != 112)
                {
                    failures.Add("shadow alpha: " + cell.Name + " has alpha " + alpha + " at its centre, expected 112");
                }
            }

            // Sliced cells (inset 128): uniform centre, and edge bands that vary only across their width.
            foreach (string name in new[] { "Parapet", "SoftBox", "StandPad", "TerminalZone" })
            {
                SlicedUniform(failures, ArtCells.Named(name), 128);
            }

            // Mirror seams, at mips 0 to 3, in the cells continuous across the axis.
            var seams = new List<(ArtCell Cell, int Full)> { (ArtCells.Named("JetBridge"), 255) };
            for (int s = 0; s < 6; s++)
            {
                seams.Add((ArtCells.Aircraft(s, 0), 112));
                seams.Add((ArtCells.Aircraft(s, 1), 255));
                seams.Add((ArtCells.Aircraft(s, 2), 255));
                seams.Add((ArtCells.Aircraft(s, 4), 255));
            }

            foreach ((ArtCell cell, int full) in seams)
            {
                for (int m = 0; m <= 3; m++)
                {
                    int side = ArtCells.AtlasSize >> m;
                    byte[] mip = _atlas.Mips[m];
                    int cm = cell.Side >> m;
                    int h = cm / 2;
                    int x0 = cell.Px >> m;
                    int y0 = cell.Py >> m;
                    int holes = 0;
                    string first = string.Empty;
                    for (int j = 0; j < cm; j++)
                    {
                        int aL = mip[ArtCells.Offset(side, x0 + h - 2, y0 + j) + 3];
                        int aR = mip[ArtCells.Offset(side, x0 + h + 1, y0 + j) + 3];
                        if (aL != full || aR != full)
                        {
                            continue;
                        }

                        int a1 = mip[ArtCells.Offset(side, x0 + h - 1, y0 + j) + 3];
                        int a2 = mip[ArtCells.Offset(side, x0 + h, y0 + j) + 3];
                        if (a1 != full || a2 != full)
                        {
                            if (holes == 0)
                            {
                                first = "row " + j + ": axis alphas " + a1 + ", " + a2;
                            }

                            holes++;
                        }
                    }

                    if (holes != 0)
                    {
                        failures.Add("mirror seam: " + cell.Name + " mip " + m + ": " + holes + " rows with the axis below " + full + " between full neighbours, first " + first);
                    }
                }
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void test_art2d_aircraft_follow_the_proportion_table()
        {
            byte[] mip0 = _atlas.Mips[0];
            var widths = new int[6];
            for (int s = 0; s < 6; s++)
            {
                ArtCell status = ArtCells.Aircraft(s, 1);
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
                int height = maxJ - minJ + 1;

                // Width span × 480 and height length × 480, each ± 3 pixels (in hundredths: ± 300).
                int spanH = ArtTable.SpanHundredths[s];
                int lenH = ArtTable.LengthHundredths[s];
                Assert.True(
                    Math.Abs((100 * widths[s]) - (spanH * 480)) <= 300,
                    string.Format(CultureInfo.InvariantCulture, "{0}: status width {1} px, expected {2} × 480 = {3} ± 3", status.Name, widths[s], spanH / 100.0, spanH * 4.8));
                Assert.True(
                    Math.Abs((100 * height) - (lenH * 480)) <= 300,
                    string.Format(CultureInfo.InvariantCulture, "{0}: status height {1} px, expected {2} × 480 = {3} ± 3", status.Name, height, lenH / 100.0, lenH * 4.8));
            }

            // Span grows strictly from A to F (length does not: F is shorter than E).
            for (int s = 1; s < 6; s++)
            {
                Assert.True(widths[s] > widths[s - 1], "status width does not grow from " + ArtCells.SizeNames[s - 1] + " (" + widths[s - 1] + ") to " + ArtCells.SizeNames[s] + " (" + widths[s] + ")");
            }

            // Each aircraft cell, Shadow included, is mirror-symmetric about its
            // vertical centre line within ± 1 per byte.
            var failures = new List<string>();
            for (int s = 0; s < 6; s++)
            {
                for (int l = 0; l < 8; l++)
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

        private int MaxAlpha(in ArtCell cell, int m)
        {
            int side = ArtCells.AtlasSize >> m;
            byte[] mip = _atlas.Mips[m];
            int x0 = cell.Px >> m;
            int y0 = cell.Py >> m;
            int c = cell.Side >> m;
            int max = 0;
            for (int j = 0; j < c; j++)
            {
                for (int i = 0; i < c; i++)
                {
                    max = Math.Max(max, mip[ArtCells.Offset(side, x0 + i, y0 + j) + 3]);
                }
            }

            return max;
        }

        private void WornFloor(List<string> failures, in ArtCell cell, int[] xRanges)
        {
            byte[] mip0 = _atlas.Mips[0];
            int checkedTexels = 0;
            int bad = 0;
            string first = string.Empty;
            for (int i = 0; i < cell.Side; i++)
            {
                long x = ArtCells.SampleQ8(0, cell.Side, i);
                bool inside = false;
                for (int k = 0; k < xRanges.Length; k += 2)
                {
                    inside |= x >= 256L * xRanges[k] && x <= 256L * xRanges[k + 1];
                }

                if (!inside)
                {
                    continue;
                }

                for (int j = 0; j < cell.Side; j++)
                {
                    int alpha = mip0[ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j) + 3];
                    checkedTexels++;
                    if (alpha < 215)
                    {
                        if (bad == 0)
                        {
                            first = "(" + i + "," + j + ") alpha " + alpha;
                        }

                        bad++;
                    }
                }
            }

            if (checkedTexels == 0)
            {
                failures.Add("worn floor: " + cell.Name + " has no texel in the checked columns");
            }

            if (bad != 0)
            {
                failures.Add("worn floor: " + cell.Name + " has " + bad + " texels below alpha 215 in the line, first " + first);
            }
        }

        private void SlicedUniform(List<string> failures, in ArtCell cell, int inset)
        {
            byte[] mip0 = _atlas.Mips[0];
            int c = cell.Side;
            long lo = 256L * (inset + 16);
            long hi = 256L * (1024 - inset - 16);
            long bandLo = 256L * inset;
            long bandHi = 256L * (1024 - inset);
            var sample = new long[c];
            for (int i = 0; i < c; i++)
            {
                sample[i] = ArtCells.SampleQ8(0, c, i);
            }

            int centre = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + (c / 2), cell.Py + (c / 2));
            int offCentre = 0;
            var bandRef = new Dictionary<string, int>();
            int offBand = 0;
            string first = string.Empty;
            for (int j = 0; j < c; j++)
            {
                for (int i = 0; i < c; i++)
                {
                    long x = sample[i];
                    long y = sample[j];
                    int o = ArtCells.Offset(ArtCells.AtlasSize, cell.Px + i, cell.Py + j);
                    bool xMid = x >= lo && x <= hi;
                    bool yMid = y >= lo && y <= hi;
                    if (xMid && yMid)
                    {
                        if (!SameTexel(mip0, o, centre))
                        {
                            if (offCentre == 0)
                            {
                                first = "centre (" + i + "," + j + ")";
                            }

                            offCentre++;
                        }

                        continue;
                    }

                    // An edge band: within the inset of one edge, and mid-range along it.
                    string? key = null;
                    if (yMid && x < bandLo)
                    {
                        key = "left " + i;
                    }
                    else if (yMid && x > bandHi)
                    {
                        key = "right " + i;
                    }
                    else if (xMid && y < bandLo)
                    {
                        key = "bottom " + j;
                    }
                    else if (xMid && y > bandHi)
                    {
                        key = "top " + j;
                    }

                    if (key == null)
                    {
                        continue;
                    }

                    if (!bandRef.TryGetValue(key, out int refOffset))
                    {
                        bandRef[key] = o;
                    }
                    else if (!SameTexel(mip0, o, refOffset))
                    {
                        if (offBand == 0)
                        {
                            first += " band " + key + " at (" + i + "," + j + ")";
                        }

                        offBand++;
                    }
                }
            }

            if (offCentre != 0 || offBand != 0)
            {
                failures.Add("sliced: " + cell.Name + ": " + offCentre + " centre texels differ from the centre, " + offBand + " edge-band texels vary along their band; " + first);
            }
        }

        private static bool SameTexel(byte[] mip, int a, int b)
        {
            return mip[a] == mip[b] && mip[a + 1] == mip[b + 1] && mip[a + 2] == mip[b + 2] && mip[a + 3] == mip[b + 3];
        }

        private static void AssertRect(int u0, int v0, int u1, int v1, in AtlasRect got, string what)
        {
            var want = new AtlasRect((float)(u0 / 4096.0), (float)(v0 / 4096.0), (float)(u1 / 4096.0), (float)(v1 / 4096.0));
            Assert.True(ArtShow.Rect(want) == ArtShow.Rect(got), what + ": " + ArtShow.Rect(got) + ", expected " + ArtShow.Rect(want));
        }
    }

    /// <summary>15 §15.18 test_art2d_atlas_is_deterministic: the one test that builds the atlas twice.</summary>
    public sealed class Art2DDeterminismTests
    {
        [Fact]
        public void test_art2d_atlas_is_deterministic()
        {
            SpriteAtlas a = Art2DFactory.BuildAtlas();
            SpriteAtlas b = Art2DFactory.BuildAtlas();
            Assert.Equal(a.Size, b.Size);
            Assert.Equal(a.Mips.Count, b.Mips.Count);
            for (int m = 0; m < a.Mips.Count; m++)
            {
                byte[] x = a.Mips[m];
                byte[] y = b.Mips[m];
                Assert.True(x.Length == y.Length, "mip " + m + " lengths " + x.Length + " and " + y.Length);
                for (int i = 0; i < x.Length; i++)
                {
                    if (x[i] != y[i])
                    {
                        Assert.Fail("two builds: mip " + m + " differs first at byte " + i + " (" + x[i] + " vs " + y[i] + ")");
                    }
                }
            }

            // Each call returns fresh arrays.
            Assert.NotSame(a.Mips, b.Mips);
            for (int m = 0; m < a.Mips.Count; m++)
            {
                Assert.False(ReferenceEquals(a.Mips[m], b.Mips[m]), "mip " + m + " is the same array in two builds");
                for (int k = 0; k < m; k++)
                {
                    Assert.False(ReferenceEquals(a.Mips[k], a.Mips[m]), "mips " + k + " and " + m + " share one array");
                }
            }
        }
    }

    /// <summary>A minimal PNG writer (RGBA, 8 bits, no filter) for the atlas dump only.</summary>
    internal static class Png
    {
        public static byte[] Encode(byte[] rgbaBottomUp, int side)
        {
            var raw = new byte[(long)side * ((side * 4) + 1)];
            for (int row = 0; row < side; row++)
            {
                long src = (long)(side - 1 - row) * side * 4;
                long dst = (long)row * ((side * 4) + 1);
                raw[dst] = 0;
                Array.Copy(rgbaBottomUp, src, raw, dst + 1, side * 4);
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
