using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the T-052 2D-art suite (Q-130), written from
    // 15 §15.3, §15.9, §15.11 and §15.17 only. They read the art's layers
    // through LayersOf and never restate the style tables, which are being
    // amended (realistic 2D, owner 2026-10-07).

    internal static class ArtShow
    {
        public static string Rect(in AtlasRect r)
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:R},{1:R},{2:R},{3:R}]", r.U0, r.V0, r.U1, r.V1);
        }

        public static VisualId[] AllVisuals()
        {
            return (VisualId[])Enum.GetValues(typeof(VisualId));
        }

        public static bool IsAircraft(VisualId v)
        {
            return v >= VisualId.AircraftA && v <= VisualId.AircraftF;
        }
    }

    /// <summary>Frames and primitives for the tessellator; layer and source do not affect it.</summary>
    internal static class ArtFrames
    {
        public static int RoleCount => Enum.GetValues(typeof(ColourRole)).Length;

        public static RenderFrame Of(params DrawPrimitive[] prims)
        {
            return new RenderFrame(0UL, Cam.At(0f, 0f, 100f), prims, Gfx.High());
        }

        public static DrawPrimitive Box(VisualId v, ColourRole role, float minX, float minY, float maxX, float maxY)
        {
            return new DrawPrimitive(
                PrimitiveKind.Box,
                DrawLayer.Ground,
                role,
                v,
                new WorldPoint(minX, minY),
                new WorldPoint(maxX, maxY),
                0f,
                default(WorldPoint),
                default(Paint),
                new SourceRef(SourceKind.Apron, 1UL, 0));
        }

        public static DrawPrimitive Segment(VisualId v, ColourRole role, float ax, float ay, float bx, float by, float size)
        {
            return new DrawPrimitive(
                PrimitiveKind.Segment,
                DrawLayer.Runway,
                role,
                v,
                new WorldPoint(ax, ay),
                new WorldPoint(bx, by),
                size,
                default(WorldPoint),
                default(Paint),
                new SourceRef(SourceKind.Runway, 1UL, 0));
        }

        public static DrawPrimitive Dot(VisualId v, ColourRole role, float cx, float cy, float size, float fx, float fy, Paint paint = default)
        {
            return new DrawPrimitive(
                PrimitiveKind.Dot,
                DrawLayer.Aircraft,
                role,
                v,
                new WorldPoint(cx, cy),
                default(WorldPoint),
                size,
                new WorldPoint(fx, fy),
                paint,
                new SourceRef(SourceKind.Aircraft, 1UL, 0));
        }

        /// <summary>One distinct colour per ColourRole, each with its own alpha.</summary>
        public static Rgba[] Roles()
        {
            var roles = new Rgba[RoleCount];
            for (int i = 0; i < roles.Length; i++)
            {
                roles[i] = new Rgba((byte)(10 + i), (byte)(200 - i), (byte)((3 * i) + 1), (byte)(120 + i));
            }

            return roles;
        }

        /// <summary>Five distinct region colours and the given mark.</summary>
        public static Paint Paint(byte mark)
        {
            return new Paint(new Rgb(1, 2, 3), new Rgb(4, 5, 6), new Rgb(7, 8, 9), new Rgb(10, 11, 12), new Rgb(13, 14, 15), mark);
        }

        public static Rgb Region(in Paint p, int region)
        {
            switch (region)
            {
                case 0: return p.Region0;
                case 1: return p.Region1;
                case 2: return p.Region2;
                case 3: return p.Region3;
                case 4: return p.Region4;
                default: throw new ArgumentOutOfRangeException(nameof(region), region, "Paint has regions 0 to 4");
            }
        }

        /// <summary>
        /// The layers a Dot or Segment emits, one quad each, in order: LayersOf,
        /// without a logo layer when Paint.Mark is None (15 §15.17 "Tessellation").
        /// </summary>
        public static List<ArtLayer> Emitted(in DrawPrimitive p)
        {
            Assert.True(p.Kind != PrimitiveKind.Box, "a Box may be sliced; its quad count is not derived here");
            var list = new List<ArtLayer>();
            foreach (ArtLayer l in Art2DFactory.LayersOf(p.Visual))
            {
                if (l.IsLogo && p.Paint.Mark == 0)
                {
                    continue;
                }

                list.Add(l);
            }

            return list;
        }
    }

    /// <summary>
    /// 15 §15.17 "Tessellation": expected corners in double, converted to float once
    /// by the tessellator, and compared within the one allowed tolerance (§15.3).
    /// </summary>
    internal static class ArtGeometry
    {
        public const double CornerTolerance = 1e-3;

        public static double[] Segment(double ax, double ay, double bx, double by, double size)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double n = Math.Sqrt((dx * dx) + (dy * dy));
            double fx = n == 0.0 ? 0.0 : dx / n;
            double fy = n == 0.0 ? 1.0 : dy / n;
            double rx = fy;
            double ry = -fx;
            double h = size / 2.0;
            return new[]
            {
                ax - (rx * h), ay - (ry * h),
                ax + (rx * h), ay + (ry * h),
                bx + (rx * h), by + (ry * h),
                bx - (rx * h), by - (ry * h),
            };
        }

        /// <summary>
        /// The Dot's corners for a layer's sub-square: design (x, y) goes to
        /// centre + r × (x/1024 − 0.5) × Size + f × (y/1024 − 0.5) × Size.
        /// </summary>
        public static double[] Dot(double cx, double cy, double size, double facingX, double facingY, int minX = 0, int minY = 0, int maxX = 1024, int maxY = 1024)
        {
            double fx = 0.0;
            double fy = 1.0;
            if (facingX != 0.0 || facingY != 0.0)
            {
                double n = Math.Sqrt((facingX * facingX) + (facingY * facingY));
                fx = facingX / n;
                fy = facingY / n;
            }

            double rx = fy;
            double ry = -fx;
            var c = new double[8];
            int[] xs = { minX, maxX, maxX, minX };
            int[] ys = { minY, minY, maxY, maxY };
            for (int k = 0; k < 4; k++)
            {
                double a = ((xs[k] / 1024.0) - 0.5) * size;
                double b = ((ys[k] / 1024.0) - 0.5) * size;
                c[2 * k] = cx + (rx * a) + (fx * b);
                c[(2 * k) + 1] = cy + (ry * a) + (fy * b);
            }

            return c;
        }

        /// <summary>Corners 0..3 are (U0,V0), (U1,V0), (U1,V1), (U0,V1).</summary>
        public static float[] Uvs(in AtlasRect r)
        {
            return new[] { r.U0, r.V0, r.U1, r.V0, r.U1, r.V1, r.U0, r.V1 };
        }

        /// <summary>The rect a layer's quad samples: its own, or the Mark's cell for a logo.</summary>
        public static AtlasRect RectFor(in ArtLayer l, byte mark)
        {
            return l.IsLogo ? Art2DFactory.LogoRect((LogoMark)mark) : l.Rect;
        }

        public static void AssertCorners(ISpriteTessellator t, int q, double[] expected, string what)
        {
            Assert.True(q < t.QuadCount, what + ": quad " + q + " not emitted (QuadCount " + t.QuadCount + ")");
            for (int i = 0; i < 8; i++)
            {
                double got = t.Corners[(8 * q) + i];
                Assert.True(
                    Math.Abs(got - expected[i]) <= CornerTolerance,
                    string.Format(CultureInfo.InvariantCulture, "{0}: quad {1} corner {2} {3} is {4:R}, expected {5:R} (± {6})", what, q, i / 2, i % 2 == 0 ? "x" : "y", got, expected[i], CornerTolerance));
            }
        }

        public static void AssertUvs(ISpriteTessellator t, int q, float[] expected, string what)
        {
            Assert.True(q < t.QuadCount, what + ": quad " + q + " not emitted (QuadCount " + t.QuadCount + ")");
            for (int i = 0; i < 8; i++)
            {
                float got = t.Uvs[(8 * q) + i];
                Assert.True(
                    got == expected[i],
                    string.Format(CultureInfo.InvariantCulture, "{0}: quad {1} uv {2} {3} is {4:R}, expected {5:R} exactly", what, q, i / 2, i % 2 == 0 ? "u" : "v", got, expected[i]));
            }
        }

        public static void AssertQuad(ISpriteTessellator t, int q, double[] corners, float[] uvs, string what)
        {
            AssertCorners(t, q, corners, what);
            AssertUvs(t, q, uvs, what);
        }

        /// <summary>All four corners of quad q carry the same RGBA.</summary>
        public static void AssertColour(ISpriteTessellator t, int q, int r, int g, int b, int a, string what)
        {
            Assert.True(q < t.QuadCount, what + ": quad " + q + " not emitted (QuadCount " + t.QuadCount + ")");
            for (int k = 0; k < 4; k++)
            {
                int o = (16 * q) + (4 * k);
                string got = string.Format(CultureInfo.InvariantCulture, "({0},{1},{2},{3})", t.Colours[o], t.Colours[o + 1], t.Colours[o + 2], t.Colours[o + 3]);
                string want = string.Format(CultureInfo.InvariantCulture, "({0},{1},{2},{3})", r, g, b, a);
                Assert.True(got == want, what + ": quad " + q + " corner " + k + " colour " + got + ", expected " + want);
            }
        }

        /// <summary>L(c) = floor(255 × lin(c / 255) + 0.5), in double (15 §15.17 "Colour").</summary>
        public static int Linear(int c)
        {
            double x = c / 255.0;
            double lin = x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            return (int)Math.Floor((255.0 * lin) + 0.5);
        }
    }

    /// <summary>One atlas build shared by the read-only atlas tests (BuildAtlas returns a fresh value per call).</summary>
    public sealed class Art2DAtlasFixture
    {
        public Art2DAtlasFixture()
        {
            Atlas = Art2DFactory.BuildAtlas();
        }

        public SpriteAtlas Atlas { get; }
    }
}
