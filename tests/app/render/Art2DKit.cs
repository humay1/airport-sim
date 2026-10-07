using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the T-052 2D-art suite (Q-130), written from
    // 15 §15.3, §15.9, §15.11 and §15.17 only.

    /// <summary>One atlas cell: its lower-left pixel and side at mip 0 (15 §15.17 "Packing").</summary>
    internal readonly struct ArtCell
    {
        public ArtCell(string name, int px, int py, int side, bool edgeToEdge)
        {
            Name = name;
            Px = px;
            Py = py;
            Side = side;
            EdgeToEdge = edgeToEdge;
        }

        public string Name { get; }

        public int Px { get; }

        public int Py { get; }

        public int Side { get; }

        /// <summary>Solid, RunwayEdgeLines, CentreStripe and JetBridge fill their bleed border.</summary>
        public bool EdgeToEdge { get; }

        /// <summary>The bleed border at mip 0: c/32 pixels (8 or 4).</summary>
        public int Border => Side / 32;
    }

    /// <summary>15 §15.17's packing table, written out from the spec's literals.</summary>
    internal static class ArtCells
    {
        public const int AtlasSize = 2048;
        public const int LargeCell = 256;
        public const int SmallCell = 128;
        public const int MipCount = 5;
        public const int ArtUnits = 1024;

        /// <summary>Small-cell slots 0 to 34, in slot order.</summary>
        public static readonly string[] SmallNames =
        {
            "Solid", "Disc", "RunwayEdgeLines", "CentreStripe", "RunwayThreshold",
            "BuildingRoof", "ControlTower", "JetBridge", "StandPad", "StandLeadIn",
            "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
            "TerminalZone", "LanePip",
            "PaxOutline", "PaxBottom", "PaxBag", "PaxTop", "PaxSkin", "PaxHair",
            "LogoDisc", "LogoRing", "LogoChevron", "LogoStar", "LogoBars", "LogoDiamond", "LogoCrescent",
        };

        /// <summary>Aircraft layers 0 to 6, which are the large-cell columns.</summary>
        public static readonly string[] AircraftLayerNames = { "Status", "Wings", "Engines", "Fuselage", "Cheatline", "Tail", "Glazing" };

        public static readonly string[] SizeNames = { "A", "B", "C", "D", "E", "F" };

        public static ArtCell Small(int slot)
        {
            Assert.InRange(slot, 0, SmallNames.Length - 1);
            bool edge = slot == 0 || slot == 2 || slot == 3 || slot == 7;
            return new ArtCell(SmallNames[slot], SmallCell * (slot % 16), 1536 + (SmallCell * (slot / 16)), SmallCell, edge);
        }

        public static ArtCell Small(string name)
        {
            return Small(Array.IndexOf(SmallNames, name));
        }

        public static ArtCell Aircraft(int size, int layer)
        {
            return new ArtCell("Aircraft" + SizeNames[size] + "." + AircraftLayerNames[layer], LargeCell * layer, LargeCell * size, LargeCell, false);
        }

        public static ArtCell Logo(LogoMark mark)
        {
            return Small(27 + (int)mark);
        }

        public static List<ArtCell> All()
        {
            var all = new List<ArtCell>();
            for (int s = 0; s < 6; s++)
            {
                for (int l = 0; l < 7; l++)
                {
                    all.Add(Aircraft(s, l));
                }
            }

            for (int k = 0; k < SmallNames.Length; k++)
            {
                all.Add(Small(k));
            }

            return all;
        }

        /// <summary>U0 = (px + c/32) / 2048, U1 = (px + c − c/32) / 2048, and V alike; exact in float.</summary>
        public static AtlasRect RectOf(in ArtCell c)
        {
            int b = c.Border;
            return new AtlasRect(
                (float)((c.Px + b) / 2048.0),
                (float)((c.Py + b) / 2048.0),
                (float)((c.Px + c.Side - b) / 2048.0),
                (float)((c.Py + c.Side - b) / 2048.0));
        }

        /// <summary>
        /// Whether pixel (x, y) of mip m lies in a cell. The 42 large cells tile
        /// [0, 1792) × [0, 1536) exactly; small slot k is in the band y ≥ 1536.
        /// </summary>
        public static bool InAnyCell(int m, int x, int y)
        {
            int band = 1536 >> m;
            if (y < band)
            {
                return x < (1792 >> m);
            }

            int small = SmallCell >> m;
            int slot = (x / small) + (16 * ((y - band) / small));
            return slot < SmallNames.Length;
        }

        public static int Offset(int side, int x, int y)
        {
            // Rows bottom to top, RGBA32 (15 §15.17 SpriteAtlas.Mips).
            return ((y * side) + x) * 4;
        }

        public static string Show(in AtlasRect r)
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:R},{1:R},{2:R},{3:R}]", r.U0, r.V0, r.U1, r.V1);
        }

        public static string Show(in ArtLayer l)
        {
            // A logo layer's Rect is unused (it draws from the Mark's cell), so it is not shown.
            string rect = l.IsLogo ? "logo" : Show(l.Rect);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} region={1} fixed={2} {3} box=({4},{5})-({6},{7})",
                l.Colour,
                l.Region,
                Prims.Show(l.Fixed),
                rect,
                l.MinX,
                l.MinY,
                l.MaxX,
                l.MaxY);
        }

        public static string Show(IReadOnlyList<ArtLayer> layers)
        {
            var parts = new List<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                parts.Add(Show(layers[i]));
            }

            return string.Join("\n", parts);
        }
    }

    /// <summary>15 §15.17's "Visual layers" table, written out from the spec.</summary>
    internal static class ArtTable
    {
        public static readonly Rgb WingGrey = new Rgb(0xD5, 0xD8, 0xDC);
        public static readonly Rgb GlazingDark = new Rgb(0x2A, 0x31, 0x38);

        /// <summary>The row length in hundredths, A to F (the proportion table).</summary>
        public static readonly int[] LengthHundredths = { 40, 50, 64, 76, 88, 94 };

        /// <summary>The row span in hundredths, A to F (the proportion table).</summary>
        public static readonly int[] SpanHundredths = { 40, 50, 62, 76, 88, 96 };

        public static VisualId[] AllVisuals()
        {
            return (VisualId[])Enum.GetValues(typeof(VisualId));
        }

        public static ArtLayer Role(in ArtCell cell)
        {
            return new ArtLayer(LayerColour.Role, 0, default(Rgb), ArtCells.RectOf(cell), false, 0, 0, 1024, 1024);
        }

        public static ArtLayer Region(in ArtCell cell, int region)
        {
            return new ArtLayer(LayerColour.Region, region, default(Rgb), ArtCells.RectOf(cell), false, 0, 0, 1024, 1024);
        }

        public static ArtLayer Fixed(in ArtCell cell, Rgb colour)
        {
            return new ArtLayer(LayerColour.Fixed, 0, colour, ArtCells.RectOf(cell), false, 0, 0, 1024, 1024);
        }

        /// <summary>The logo sub-square's integer rule; every quantity is non-negative, so / floors.</summary>
        public static void LogoSquare(int size, out int minX, out int minY, out int maxX, out int maxY)
        {
            int lh = LengthHundredths[size];
            int side = ((lh * 2048) + 500) / 1000;
            int off = ((lh * 3072) + 500) / 1000;
            int half = side / 2;
            minX = 512 - half;
            maxX = minX + side;
            minY = 512 - off - half;
            maxY = minY + side;
        }

        public static ArtLayer Logo(int size)
        {
            LogoSquare(size, out int minX, out int minY, out int maxX, out int maxY);
            return new ArtLayer(LayerColour.Region, 4, default(Rgb), default(AtlasRect), true, minX, minY, maxX, maxY);
        }

        public static List<ArtLayer> Layers(VisualId v)
        {
            switch (v)
            {
                case VisualId.RunwaySurface:
                case VisualId.TaxiwaySurface:
                case VisualId.Apron:
                case VisualId.QueueFill:
                    return One(Role(ArtCells.Small("Solid")));
                case VisualId.RunwayEdgeLines:
                    return One(Role(ArtCells.Small("RunwayEdgeLines")));
                case VisualId.RunwayThreshold:
                    return One(Role(ArtCells.Small("RunwayThreshold")));
                case VisualId.RunwayCentreDash:
                case VisualId.TaxiwayCentreline:
                    return One(Role(ArtCells.Small("CentreStripe")));
                case VisualId.TaxiwayJunction:
                    return One(Role(ArtCells.Small("Disc")));
                case VisualId.TerminalBuilding:
                case VisualId.Pier:
                    return One(Role(ArtCells.Small("BuildingRoof")));
                case VisualId.ControlTower:
                    return One(Role(ArtCells.Small("ControlTower")));
                case VisualId.JetBridge:
                    return One(Role(ArtCells.Small("JetBridge")));
                case VisualId.StandPad:
                    return One(Role(ArtCells.Small("StandPad")));
                case VisualId.StandLeadIn:
                    return One(Role(ArtCells.Small("StandLeadIn")));
                case VisualId.TerminalZone:
                    return One(Role(ArtCells.Small("TerminalZone")));
                case VisualId.LanePip:
                    return One(Role(ArtCells.Small("LanePip")));
                case VisualId.Passenger:
                    return new List<ArtLayer>
                    {
                        Role(ArtCells.Small("PaxOutline")),
                        Region(ArtCells.Small("PaxBottom"), 1),
                        Region(ArtCells.Small("PaxBag"), 4),
                        Region(ArtCells.Small("PaxTop"), 0),
                        Region(ArtCells.Small("PaxSkin"), 2),
                        Region(ArtCells.Small("PaxHair"), 3),
                    };
            }

            if (v >= VisualId.MarkingDigit0 && v <= VisualId.MarkingDigit9)
            {
                return One(Role(ArtCells.Small("Digit" + (v - VisualId.MarkingDigit0).ToString(CultureInfo.InvariantCulture))));
            }

            if (v >= VisualId.AircraftA && v <= VisualId.AircraftF)
            {
                int s = v - VisualId.AircraftA;
                return new List<ArtLayer>
                {
                    Role(ArtCells.Aircraft(s, 0)),
                    Fixed(ArtCells.Aircraft(s, 1), WingGrey),
                    Region(ArtCells.Aircraft(s, 2), 3),
                    Region(ArtCells.Aircraft(s, 3), 0),
                    Region(ArtCells.Aircraft(s, 4), 2),
                    Region(ArtCells.Aircraft(s, 5), 1),
                    Logo(s),
                    Fixed(ArtCells.Aircraft(s, 6), GlazingDark),
                };
            }

            throw new ArgumentOutOfRangeException(nameof(v), v, "VisualId not in 15 §15.17's table");
        }

        /// <summary>The Box slicing column: world inset in metres (world units), 0 when not sliced.</summary>
        public static double SliceWorld(VisualId v)
        {
            switch (v)
            {
                case VisualId.TerminalBuilding:
                case VisualId.Pier:
                    return 3.0;
                case VisualId.StandPad:
                    return 2.0;
                case VisualId.TerminalZone:
                    return 1.0;
                default:
                    return 0.0;
            }
        }

        /// <summary>The design inset of every sliced visual: 128 units.</summary>
        public const int SliceDesign = 128;

        /// <summary>Quads one primitive emits: one per layer, nine for a sliced Box, none for a logo without a mark.</summary>
        public static int QuadsOf(in DrawPrimitive p)
        {
            int n = 0;
            foreach (ArtLayer l in Layers(p.Visual))
            {
                if (l.IsLogo && p.Paint.Mark == 0)
                {
                    continue;
                }

                n += p.Kind == PrimitiveKind.Box && SliceWorld(p.Visual) > 0.0 ? 9 : 1;
            }

            return n;
        }

        private static List<ArtLayer> One(ArtLayer l)
        {
            return new List<ArtLayer> { l };
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

        public static Paint Paint(byte mark)
        {
            return new Paint(new Rgb(1, 2, 3), new Rgb(4, 5, 6), new Rgb(7, 8, 9), new Rgb(10, 11, 12), new Rgb(13, 14, 15), mark);
        }
    }

    /// <summary>
    /// 15 §15.17 "Tessellation": expected corners in double, converted to float once
    /// by the tessellator, and compared within the one allowed tolerance (§15.3).
    /// </summary>
    internal static class ArtGeometry
    {
        public const double CornerTolerance = 1e-3;

        public static double[] Box(double x0, double y0, double x1, double y1)
        {
            return new[] { x0, y0, x1, y0, x1, y1, x0, y1 };
        }

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

        public static float[] Uvs(float u0, float v0, float u1, float v1)
        {
            return new[] { u0, v0, u1, v0, u1, v1, u0, v1 };
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
