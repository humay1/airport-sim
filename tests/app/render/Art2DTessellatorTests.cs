using System;
using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 "Tessellation" (Q-130): RenderFrame to quads. Corners are
    /// compared within §15.3's one tolerance (1e-3 world units) using §15.17's
    /// chosen vectors; Uvs and colours exactly. Layers, rects and sub-squares are
    /// read through LayersOf and LogoRect, so these tests do not restate the
    /// style tables, which the realistic-2D amendment may change.
    /// </summary>
    public sealed class Art2DTessellatorTests
    {
        private const double Tol = ArtGeometry.CornerTolerance;

        // §15.17's tolerance note: axis-aligned facings, (3, 4) and (1, 1); a
        // long integer vector is the same direction as its unit (15 §15.16).
        private static readonly (float X, float Y, double[]? Whole)[] Facings =
        {
            (0f, 0f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }),
            (0f, 1f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }),
            (1f, 0f, new double[] { 990, -1990, 990, -2010, 1010, -2010, 1010, -1990 }),
            (-1f, 0f, new double[] { 1010, -2010, 1010, -1990, 990, -1990, 990, -2010 }),
            (0f, -1f, new double[] { 1010, -1990, 990, -1990, 990, -2010, 1010, -2010 }),
            (3f, 4f, new double[] { 986, -2002, 1002, -2014, 1014, -1998, 998, -1986 }),
            (1f, 1f, null),
            (2000f, 0f, new double[] { 990, -1990, 990, -2010, 1010, -2010, 1010, -1990 }),
            (0f, -7f, new double[] { 1010, -1990, 990, -1990, 990, -2010, 1010, -2010 }),
            (-300f, -400f, null),
        };

        [Fact]
        public void test_art2d_tessellator_corners_follow_kind_and_facing()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();

            // Dot, for every visual and every facing: f is Facing normalised, or
            // (0, 1) for (0, 0); r = (f.Y, −f.X); each layer's sub-square maps design
            // (x, y) to centre + r × (x/1024 − 0.5) × Size + f × (y/1024 − 0.5) × Size.
            // A whole layer (0,0)–(1024,1024) gives centre ∓ r h ∓ f h, spelled out
            // for centre (1000, −2000) and Size 20.
            Paint marked = ArtFrames.Paint((byte)LogoMark.Ring);
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                foreach ((float fx, float fy, double[]? whole) in Facings)
                {
                    DrawPrimitive p = ArtFrames.Dot(v, ColourRole.Agent, 1000f, -2000f, 20f, fx, fy, marked);
                    List<ArtLayer> layers = ArtFrames.Emitted(p);
                    string what = v + " dot facing (" + fx + "," + fy + ")";
                    Assert.True(layers.Count == t.Fill(ArtFrames.Of(p), roles, false), what + ": quad count is not one per layer");
                    Assert.Equal(layers.Count, t.QuadCount);
                    for (int q = 0; q < layers.Count; q++)
                    {
                        ArtLayer l = layers[q];
                        ArtGeometry.AssertQuad(
                            t,
                            q,
                            ArtGeometry.Dot(1000, -2000, 20, fx, fy, l.MinX, l.MinY, l.MaxX, l.MaxY),
                            ArtGeometry.Uvs(ArtGeometry.RectFor(l, marked.Mark)),
                            what + " layer " + q);
                        if (whole != null && IsWhole(l))
                        {
                            ArtGeometry.AssertCorners(t, q, whole, what + " layer " + q + " (spelled out)");
                        }
                    }
                }
            }

            // Large size and far centre, within the tolerance note (Size ≤ 1000, centres within ± 10 000).
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                DrawPrimitive p = ArtFrames.Dot(v, ColourRole.Agent, 9000f, -9000f, 1000f, 1f, 1f, marked);
                List<ArtLayer> layers = ArtFrames.Emitted(p);
                Assert.Equal(layers.Count, t.Fill(ArtFrames.Of(p), roles, false));
                for (int q = 0; q < layers.Count; q++)
                {
                    ArtLayer l = layers[q];
                    ArtGeometry.AssertCorners(t, q, ArtGeometry.Dot(9000, -9000, 1000, 1, 1, l.MinX, l.MinY, l.MaxX, l.MaxY), v + " dot size 1000 facing (1,1) layer " + q);
                }
            }

            // Segment: f = (B − A)/|B − A| (or (0, 1) when A = B), r = (f.Y, −f.X),
            // h = Size/2; corners A − r h, A + r h, B + r h, B − r h.
            int wholeSegmentLayers = 0;
            var segments = new (float Ax, float Ay, float Bx, float By, float Size, double[] Spelled)[]
            {
                (100f, 200f, 400f, 600f, 45f, new double[] { 82, 213.5, 118, 186.5, 418, 586.5, 382, 613.5 }),
                (-2000f, 0f, 0f, 0f, 45f, new double[] { -2000, 22.5, -2000, -22.5, 0, -22.5, 0, 22.5 }),
                (5f, 5f, 5f, 5f, 10f, new double[] { 0, 5, 10, 5, 10, 5, 0, 5 }),
            };
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                foreach (var s in segments)
                {
                    DrawPrimitive p = ArtFrames.Segment(v, ColourRole.Runway, s.Ax, s.Ay, s.Bx, s.By, s.Size);
                    List<ArtLayer> layers = ArtFrames.Emitted(p);
                    string what = v + " segment (" + s.Ax + "," + s.Ay + ")-(" + s.Bx + "," + s.By + ")";
                    Assert.True(layers.Count == t.Fill(ArtFrames.Of(p), roles, false), what + ": quad count is not one per layer");
                    for (int q = 0; q < layers.Count; q++)
                    {
                        ArtGeometry.AssertUvs(t, q, ArtGeometry.Uvs(ArtGeometry.RectFor(layers[q], 0)), what + " layer " + q);
                        if (IsWhole(layers[q]))
                        {
                            ArtGeometry.AssertCorners(t, q, ArtGeometry.Segment(s.Ax, s.Ay, s.Bx, s.By, s.Size), what + " layer " + q);
                            ArtGeometry.AssertCorners(t, q, s.Spelled, what + " layer " + q + " (spelled out)");
                            wholeSegmentLayers++;
                        }
                    }
                }
            }

            Assert.True(wholeSegmentLayers > 0, "no visual has a whole-square layer to check the segment rule on");

            // Every quad goes in list order, then layer order.
            var all = new List<DrawPrimitive>();
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                all.Add(ArtFrames.Dot(v, ColourRole.Agent, 0f, 0f, 10f, 0f, 0f, marked));
                all.Add(ArtFrames.Segment(v, ColourRole.Agent, 0f, 0f, 0f, 10f, 2f));
            }

            int expected = 0;
            foreach (DrawPrimitive p in all)
            {
                expected += ArtFrames.Emitted(p).Count;
            }

            Assert.Equal(expected, t.Fill(ArtFrames.Of(all.ToArray()), roles, false));
            int at = 0;
            foreach (DrawPrimitive p in all)
            {
                foreach (ArtLayer l in ArtFrames.Emitted(p))
                {
                    ArtGeometry.AssertUvs(t, at, ArtGeometry.Uvs(ArtGeometry.RectFor(l, p.Paint.Mark)), p.Kind + " " + p.Visual + " at quad " + at);
                    if (p.Kind == PrimitiveKind.Dot)
                    {
                        ArtGeometry.AssertCorners(t, at, ArtGeometry.Dot(0, 0, 10, 0, 0, l.MinX, l.MinY, l.MaxX, l.MaxY), p.Visual + " dot at quad " + at);
                    }

                    at++;
                }
            }
        }

        [Fact]
        public void test_art2d_tessellator_boxes_are_unrotated_and_slice_into_nine()
        {
            // Box: corners (MinX,MinY), (MaxX,MinY), (MaxX,MaxY), (MinX,MaxY), never
            // rotated. A sliced Box emits nine quads per layer, bottom row first,
            // left to right, on a 3 × 3 world grid at t from each side, with
            // 0 < t ≤ min(width/2, height/2), and the rect cut into a 3 × 3 grid.
            // Which visuals slice, and by how much, is the style table's; this
            // checks only the rule, for whichever the art slices.
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            var boxes = new (float X0, float Y0, float X1, float Y1)[]
            {
                (10f, 20f, 110f, 70f),
                (0f, 0f, 100f, 0.5f),
                (-20f, -20f, 20f, 20f),
            };

            foreach (VisualId v in ArtShow.AllVisuals())
            {
                IReadOnlyList<ArtLayer> layers = Art2DFactory.LayersOf(v);
                int plain = 0;
                foreach (ArtLayer l in layers)
                {
                    if (!l.IsLogo)
                    {
                        plain++;
                    }
                }

                Assert.True(plain > 0, v + " has no layer a Box draws");

                foreach (var b in boxes)
                {
                    string what = v + " box (" + b.X0 + "," + b.Y0 + ")-(" + b.X1 + "," + b.Y1 + ")";
                    int n = t.Fill(ArtFrames.Of(ArtFrames.Box(v, ColourRole.Apron, b.X0, b.Y0, b.X1, b.Y1)), roles, false);
                    Assert.Equal(n, t.QuadCount);
                    Assert.True(n == plain || n == 9 * plain, what + ": " + n + " quads, expected " + plain + " (one per layer) or " + (9 * plain) + " (sliced)");
                    int at = 0;
                    foreach (ArtLayer l in layers)
                    {
                        if (l.IsLogo)
                        {
                            continue;
                        }

                        if (n == plain)
                        {
                            ArtGeometry.AssertQuad(t, at, new double[] { b.X0, b.Y0, b.X1, b.Y0, b.X1, b.Y1, b.X0, b.Y1 }, ArtGeometry.Uvs(l.Rect), what);
                            at++;
                        }
                        else
                        {
                            AssertSlicedGrid(t, at, b.X0, b.Y0, b.X1, b.Y1, l.Rect, what);
                            at += 9;
                        }
                    }
                }
            }
        }

        [Fact]
        public void test_art2d_tessellator_colours_follow_role_region_and_fixed()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            Assert.Equal(17, roles.Length);

            // For every visual and role: Role gives roleColours[(int)Colour] with its
            // alpha; Region gives Paint.Region_R with alpha 255; Fixed gives the
            // constant with alpha 255. The logo is emitted only with a mark.
            bool sawRole = false;
            bool sawLogo = false;
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                for (int r = 0; r < roles.Length; r++)
                {
                    foreach (byte mark in new byte[] { 0, (byte)LogoMark.Star })
                    {
                        Paint paint = ArtFrames.Paint(mark);
                        DrawPrimitive p = ArtFrames.Dot(v, (ColourRole)r, 0f, 0f, 30f, 0f, 1f, paint);
                        List<ArtLayer> layers = ArtFrames.Emitted(p);
                        int withLogo = Art2DFactory.LayersOf(v).Count;
                        Assert.Equal(layers.Count, t.Fill(ArtFrames.Of(p), roles, false));
                        if (mark == 0)
                        {
                            Assert.True(t.QuadCount <= withLogo, v + ": more quads than layers");
                        }
                        else
                        {
                            Assert.Equal(withLogo, t.QuadCount);
                        }

                        for (int q = 0; q < layers.Count; q++)
                        {
                            ArtLayer l = layers[q];
                            sawRole |= l.Colour == LayerColour.Role;
                            sawLogo |= l.IsLogo;
                            Expected(l, roles[r], paint, false, out int er, out int eg, out int eb, out int ea);
                            ArtGeometry.AssertColour(t, q, er, eg, eb, ea, v + " role " + (ColourRole)r + " mark " + mark + " layer " + q + " (" + l.Colour + ")");
                        }

                        Assert.Equal(layers.Count, t.Fill(ArtFrames.Of(p), roles, true));
                        for (int q = 0; q < layers.Count; q++)
                        {
                            Expected(layers[q], roles[r], paint, true, out int er, out int eg, out int eb, out int ea);
                            ArtGeometry.AssertColour(t, q, er, eg, eb, ea, v + " role " + (ColourRole)r + " mark " + mark + " layer " + q + ", linear");
                        }
                    }
                }
            }

            Assert.True(sawRole, "no layer of any visual is coloured by role");
            Assert.True(sawLogo, "no visual has a logo layer");

            // The role alpha is kept, also when linear: QueueFill at alpha 140.
            (VisualId roleVisual, int roleLayer) = FirstRoleLayer();
            Rgba[] palette = ArtFrames.Roles();
            palette[(int)ColourRole.QueueFill] = new Rgba(0xE8, 0xA3, 0x3A, 140);
            DrawPrimitive fill = ArtFrames.Dot(roleVisual, ColourRole.QueueFill, 0f, 0f, 5f, 0f, 0f);
            t.Fill(ArtFrames.Of(fill), palette, false);
            ArtGeometry.AssertColour(t, roleLayer, 0xE8, 0xA3, 0x3A, 140, "queue fill, sRGB");
            t.Fill(ArtFrames.Of(fill), palette, true);
            ArtGeometry.AssertColour(t, roleLayer, ArtGeometry.Linear(0xE8), ArtGeometry.Linear(0xA3), ArtGeometry.Linear(0x3A), 140, "queue fill, linear: alpha unchanged");

            // linear: L(0) = 0, L(128) = 55, L(255) = 255, and every value by the table.
            Assert.Equal(0, ArtGeometry.Linear(0));
            Assert.Equal(55, ArtGeometry.Linear(128));
            Assert.Equal(255, ArtGeometry.Linear(255));
            for (int c = 0; c < 256; c++)
            {
                var sweep = new Rgba[roles.Length];
                for (int r = 0; r < sweep.Length; r++)
                {
                    sweep[r] = new Rgba((byte)c, (byte)(255 - c), (byte)((7 * c) % 256), (byte)(255 - (c / 2)));
                }

                t.Fill(ArtFrames.Of(ArtFrames.Dot(roleVisual, ColourRole.Agent, 0f, 0f, 5f, 0f, 0f)), sweep, true);
                ArtGeometry.AssertColour(t, roleLayer, ArtGeometry.Linear(c), ArtGeometry.Linear(255 - c), ArtGeometry.Linear((7 * c) % 256), 255 - (c / 2), "linear, c = " + c);
                t.Fill(ArtFrames.Of(ArtFrames.Dot(roleVisual, ColourRole.Agent, 0f, 0f, 5f, 0f, 0f)), sweep, false);
                ArtGeometry.AssertColour(t, roleLayer, c, 255 - c, (7 * c) % 256, 255 - (c / 2), "sRGB, c = " + c);
            }

            var only128 = new Rgba[roles.Length];
            for (int r = 0; r < only128.Length; r++)
            {
                only128[r] = new Rgba(128, 0, 255, 255);
            }

            t.Fill(ArtFrames.Of(ArtFrames.Dot(roleVisual, ColourRole.Agent, 0f, 0f, 5f, 0f, 0f)), only128, true);
            ArtGeometry.AssertColour(t, roleLayer, 55, 0, 255, 255, "L(128) = 55, L(0) = 0, L(255) = 255");

            // roleColours must have one entry per ColourRole, else ArgumentException (roleColours).
            RenderFrame one = ArtFrames.Of(ArtFrames.Dot(roleVisual, ColourRole.Apron, 0f, 0f, 1f, 0f, 0f));
            foreach (int n in new[] { roles.Length - 1, 0, roles.Length + 1 })
            {
                var wrong = new Rgba[n];
                ArgumentException e = Assert.ThrowsAny<ArgumentException>(() => t.Fill(one, wrong, false));
                Assert.True(e.ParamName == "roleColours", "roleColours of " + n + " entries: ParamName " + (e.ParamName ?? "null"));
            }
        }

        [Fact]
        public void test_art2d_tessellator_reuses_buffers_and_is_repeatable()
        {
            // 15 §15.17 "Buffers": reused, growing only when a frame needs more
            // quads than ever before, and valid until the next Fill.
            Rgba[] roles = ArtFrames.Roles();
            var prims = new List<DrawPrimitive>();
            for (int k = 0; k < 50; k++)
            {
                var rgb = new Rgb((byte)k, (byte)(2 * k), (byte)(3 * k));
                VisualId v = ArtShow.AllVisuals()[k % ArtShow.AllVisuals().Length];
                prims.Add(ArtFrames.Dot(v, (ColourRole)(k % 17), k, -k, 2f, k % 3, (k % 5) - 2, new Paint(rgb, rgb, rgb, rgb, rgb, (byte)(k % 8))));
            }

            int n = 0;
            foreach (DrawPrimitive p in prims)
            {
                n += ArtFrames.Emitted(p).Count;
            }

            Assert.True(n >= prims.Count, "the frame's " + prims.Count + " dots have only " + n + " layers");
            RenderFrame big = ArtFrames.Of(prims.ToArray());
            RenderFrame small = ArtFrames.Of(prims[0]);
            int smallCount = ArtFrames.Emitted(prims[0]).Count;
            RenderFrame empty = ArtFrames.Of();

            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Assert.Equal(n, t.Fill(big, roles, false));
            Assert.Equal(n, t.QuadCount);
            float[] corners = t.Corners;
            float[] uvs = t.Uvs;
            byte[] colours = t.Colours;
            Assert.True(corners.Length >= 8 * n && uvs.Length >= 8 * n && colours.Length >= 16 * n, "buffers hold the frame's quads");
            float[] firstCorners = Prefix(corners, 8 * n);
            float[] firstUvs = Prefix(uvs, 8 * n);
            byte[] firstColours = Prefix(colours, 16 * n);

            Assert.Equal(smallCount, t.Fill(small, roles, false));
            Assert.Equal(smallCount, t.QuadCount);
            Assert.Same(corners, t.Corners);
            Assert.Same(uvs, t.Uvs);
            Assert.Same(colours, t.Colours);

            Assert.Equal(0, t.Fill(empty, roles, false));
            Assert.Equal(0, t.QuadCount);

            Assert.Equal(n, t.Fill(big, roles, false));
            Assert.Same(corners, t.Corners);
            Assert.Same(uvs, t.Uvs);
            Assert.Same(colours, t.Colours);
            Assert.Equal(firstCorners, Prefix(t.Corners, 8 * n));
            Assert.Equal(firstUvs, Prefix(t.Uvs, 8 * n));
            Assert.Equal(firstColours, Prefix(t.Colours, 16 * n));

            // A fresh tessellator gives the same quads for the same frame.
            ISpriteTessellator u = Art2DFactory.CreateTessellator();
            Assert.Equal(n, u.Fill(big, roles, false));
            Assert.Equal(firstCorners, Prefix(u.Corners, 8 * n));
            Assert.Equal(firstUvs, Prefix(u.Uvs, 8 * n));
            Assert.Equal(firstColours, Prefix(u.Colours, 16 * n));
            Assert.NotSame(t.Corners, u.Corners);
        }

        private static bool IsWhole(in ArtLayer l)
        {
            return l.MinX == 0 && l.MinY == 0 && l.MaxX == 1024 && l.MaxY == 1024;
        }

        private static (VisualId Visual, int Layer) FirstRoleLayer()
        {
            foreach (VisualId v in ArtShow.AllVisuals())
            {
                IReadOnlyList<ArtLayer> layers = Art2DFactory.LayersOf(v);
                int q = 0;
                foreach (ArtLayer l in layers)
                {
                    if (l.IsLogo)
                    {
                        continue;
                    }

                    if (l.Colour == LayerColour.Role)
                    {
                        return (v, q);
                    }

                    q++;
                }
            }

            Assert.Fail("no visual has a role-coloured layer");
            return (VisualId.RunwaySurface, 0);
        }

        private static void Expected(in ArtLayer l, Rgba role, in Paint paint, bool linear, out int r, out int g, out int b, out int a)
        {
            switch (l.Colour)
            {
                case LayerColour.Role:
                    r = role.R;
                    g = role.G;
                    b = role.B;
                    a = role.A;
                    break;
                case LayerColour.Region:
                    Rgb c = ArtFrames.Region(paint, l.Region);
                    r = c.R;
                    g = c.G;
                    b = c.B;
                    a = 255;
                    break;
                default:
                    r = l.Fixed.R;
                    g = l.Fixed.G;
                    b = l.Fixed.B;
                    a = 255;
                    break;
            }

            if (linear)
            {
                r = ArtGeometry.Linear(r);
                g = ArtGeometry.Linear(g);
                b = ArtGeometry.Linear(b);
            }
        }

        private static void AssertSlicedGrid(ISpriteTessellator t, int first, double x0, double y0, double x1, double y1, in AtlasRect rect, string what)
        {
            // Read the grid off the bottom row and the left column, then check every quad against it.
            var xs = new double[4];
            var ys = new double[4];
            var us = new float[4];
            var vs = new float[4];
            for (int k = 0; k < 3; k++)
            {
                xs[k] = t.Corners[8 * (first + k)];
                us[k] = t.Uvs[8 * (first + k)];
                ys[k] = t.Corners[(8 * (first + (3 * k))) + 1];
                vs[k] = t.Uvs[(8 * (first + (3 * k))) + 1];
            }

            xs[3] = t.Corners[(8 * (first + 2)) + 2];
            us[3] = t.Uvs[(8 * (first + 2)) + 2];
            ys[3] = t.Corners[(8 * (first + 6)) + 5];
            vs[3] = t.Uvs[(8 * (first + 6)) + 5];

            // The world grid: the box's own edges outside, t from each side inside.
            double inset = xs[1] - xs[0];
            double half = Math.Min((x1 - x0) / 2.0, (y1 - y0) / 2.0);
            Assert.True(inset > 0.0 && inset <= half + Tol, what + ": slice inset " + inset + " is not in (0, " + half + "]");
            ArtGeometry.AssertCorners(t, first, new[] { x0, y0, x0 + inset, y0, x0 + inset, y0 + inset, x0, y0 + inset }, what + " slice grid (bottom-left)");
            Assert.True(Math.Abs((xs[3] - xs[2]) - inset) <= Tol && Math.Abs((ys[1] - ys[0]) - inset) <= Tol && Math.Abs((ys[3] - ys[2]) - inset) <= Tol, what + ": the world grid is not at one inset t from each side");
            Assert.True(Math.Abs(xs[3] - x1) <= Tol && Math.Abs(ys[3] - y1) <= Tol, what + ": the slices do not reach the box's max corner");

            // The rect grid: the rect's own edges outside, ordered inside.
            Assert.True(us[0] == rect.U0 && us[3] == rect.U1 && vs[0] == rect.V0 && vs[3] == rect.V1, what + ": the rect grid's outer edges are not the layer's rect");
            Assert.True(us[0] <= us[1] && us[1] <= us[2] && us[2] <= us[3] && vs[0] <= vs[1] && vs[1] <= vs[2] && vs[2] <= vs[3], what + ": the rect grid is not ordered");

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    ArtGeometry.AssertQuad(
                        t,
                        first + (row * 3) + col,
                        new[] { xs[col], ys[row], xs[col + 1], ys[row], xs[col + 1], ys[row + 1], xs[col], ys[row + 1] },
                        new[] { us[col], vs[row], us[col + 1], vs[row], us[col + 1], vs[row + 1], us[col], vs[row + 1] },
                        what + " slice (row " + row + ", col " + col + ")");
                }
            }
        }

        private static T[] Prefix<T>(T[] a, int n)
        {
            var p = new T[n];
            Array.Copy(a, p, n);
            return p;
        }
    }
}
