using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the T-052 2D-art suite, written from 15 §15.3, §15.9,
    // §15.11, §15.17 and §15.18 (Q-130, as amended by Q-131) only.

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

        /// <summary>15 §15.17 "Design space": the cells that fill their bleed border.</summary>
        public bool EdgeToEdge { get; }

        /// <summary>The bleed border at mip 0: c/32 pixels (16 or 4).</summary>
        public int Border => Side / 32;
    }

    /// <summary>15 §15.17's packing (Q-131), written out from the spec's literals.</summary>
    internal static class ArtCells
    {
        public const int AtlasSize = 4096;
        public const int LargeCell = 512;
        public const int SmallCell = 128;
        public const int MipCount = 6;
        public const int ArtUnits = 1024;
        public const int GroundTile = 64;
        public const int GroundTilesPerAxis = 32;

        /// <summary>Small-cell slots 0 to 36, in slot order.</summary>
        public static readonly string[] SmallNames =
        {
            "Solid", "Disc", "RunwayEdgeLines", "CentreStripe", "RunwayThreshold",
            "Parapet", "SoftBox", "JetBridge", "StandPad", "StandLeadIn",
            "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
            "TerminalZone", "LanePip",
            "PaxOutline", "PaxBottom", "PaxBag", "PaxTop", "PaxSkin", "PaxHair",
            "LogoDisc", "LogoRing", "LogoChevron", "LogoStar", "LogoBars", "LogoDiamond", "LogoCrescent",
            "SoftBar", "Rubber",
        };

        /// <summary>Large band slots 0 to 6 at y = 3072; slot 7 is empty.</summary>
        public static readonly string[] BandNames = { "Asphalt", "Concrete", "Grass", "Roof", "ControlTower", "GseBody", "GseDetail" };

        /// <summary>Aircraft layers 0 to 7 (Q-131 numbering), which are the large-cell columns.</summary>
        public static readonly string[] AircraftLayerNames = { "Shadow", "Status", "Wings", "Engines", "Fuselage", "Cheatline", "Tail", "Glazing" };

        public static readonly string[] SizeNames = { "A", "B", "C", "D", "E", "F" };

        /// <summary>The edge-to-edge cells (15 §15.17 "Design space").</summary>
        public static readonly string[] EdgeToEdgeNames = { "Solid", "Asphalt", "Concrete", "Grass", "Roof", "RunwayEdgeLines", "CentreStripe", "JetBridge", "SoftBar" };

        /// <summary>The tiled cells.</summary>
        public static readonly string[] TiledNames = { "Asphalt", "Concrete", "Grass", "Roof" };

        public static ArtCell Small(int slot)
        {
            Assert.InRange(slot, 0, SmallNames.Length - 1);
            string name = SmallNames[slot];
            return new ArtCell(name, SmallCell * (slot % 32), 3584 + (SmallCell * (slot / 32)), SmallCell, IsEdgeToEdge(name));
        }

        public static ArtCell Band(int slot)
        {
            Assert.InRange(slot, 0, BandNames.Length - 1);
            string name = BandNames[slot];
            return new ArtCell(name, LargeCell * slot, 3072, LargeCell, IsEdgeToEdge(name));
        }

        public static ArtCell Aircraft(int size, int layer)
        {
            return new ArtCell("Aircraft" + SizeNames[size] + "." + AircraftLayerNames[layer], LargeCell * layer, LargeCell * size, LargeCell, false);
        }

        /// <summary>A small or large-band cell by name.</summary>
        public static ArtCell Named(string name)
        {
            int k = Array.IndexOf(SmallNames, name);
            if (k >= 0)
            {
                return Small(k);
            }

            int t = Array.IndexOf(BandNames, name);
            Assert.True(t >= 0, "no cell named " + name);
            return Band(t);
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
                for (int l = 0; l < 8; l++)
                {
                    all.Add(Aircraft(s, l));
                }
            }

            for (int t = 0; t < BandNames.Length; t++)
            {
                all.Add(Band(t));
            }

            for (int k = 0; k < SmallNames.Length; k++)
            {
                all.Add(Small(k));
            }

            return all;
        }

        /// <summary>U0 = (px + c/32) / 4096, U1 = (px + c − c/32) / 4096, and V alike; exact in float.</summary>
        public static AtlasRect RectOf(in ArtCell c)
        {
            int b = c.Border;
            return new AtlasRect(
                (float)((c.Px + b) / 4096.0),
                (float)((c.Py + b) / 4096.0),
                (float)((c.Px + c.Side - b) / 4096.0),
                (float)((c.Py + c.Side - b) / 4096.0));
        }

        public static AtlasRect RectOf(string name)
        {
            return RectOf(Named(name));
        }

        /// <summary>
        /// Whether pixel (x, y) of mip m lies in a cell. The 48 aircraft cells tile
        /// [0, 4096) × [0, 3072); band slots 0 to 6 tile [0, 3584) × [3072, 3584);
        /// small slot k is in the band y ≥ 3584, 32 to a row.
        /// </summary>
        public static bool InAnyCell(int m, int x, int y)
        {
            if (y < (3072 >> m))
            {
                return true;
            }

            if (y < (3584 >> m))
            {
                return x < (3584 >> m);
            }

            int small = SmallCell >> m;
            int slot = (x / small) + (32 * ((y - (3584 >> m)) / small));
            return slot < SmallNames.Length;
        }

        public static int Offset(int side, int x, int y)
        {
            // Rows bottom to top, RGBA32 (15 §15.17 SpriteAtlas.Mips).
            return ((y * side) + x) * 4;
        }

        /// <summary>⌊a / b⌋ for b > 0 (15 §15.17 Rasterisation).</summary>
        public static long Fdiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && a < 0) ? q - 1 : q;
        }

        /// <summary>
        /// The design coordinate, in Q8, that pixel index i of a cell of side c
        /// samples at mip m: N = 32·2^m·i + 16·2^m − c, then fdiv(2·N·131072 + 15c, 30c).
        /// </summary>
        public static long SampleQ8(int m, int c, int i)
        {
            long n = (32L << m) * i + (16L << m) - c;
            return Fdiv((2 * n * 131072) + (15L * c), 30L * c);
        }

        private static bool IsEdgeToEdge(string name)
        {
            return Array.IndexOf(EdgeToEdgeNames, name) >= 0;
        }
    }

    /// <summary>15 §15.17's "Visual layers" table (Q-131), field by field.</summary>
    internal static class ArtTable
    {
        public static readonly Rgb Black = new Rgb(0, 0, 0);
        public static readonly Rgb WingGrey = new Rgb(0xD5, 0xD8, 0xDC);
        public static readonly Rgb GlazingDark = new Rgb(0x2A, 0x31, 0x38);
        public static readonly Rgb RubberDark = new Rgb(0x1A, 0x1A, 0x1A);
        public static readonly Rgb StandRed = new Rgb(0xC2, 0x3B, 0x30);
        public static readonly Rgb GseYellow = new Rgb(0xE0, 0xB1, 0x2A);
        public static readonly Rgb GseDark = new Rgb(0x30, 0x35, 0x3B);
        public static readonly Rgb GrassGreen = new Rgb(0x6F, 0x8F, 0x5E);

        /// <summary>The row length in hundredths, A to F (Q-131).</summary>
        public static readonly int[] LengthHundredths = { 38, 56, 64, 80, 92, 88 };

        /// <summary>The row span in hundredths, A to F (Q-131).</summary>
        public static readonly int[] SpanHundredths = { 40, 46, 60, 70, 84, 96 };

        /// <summary>The aircraft shadow shift by row, hundredths of a world unit.</summary>
        public static readonly int[] ShadowShiftX = { 60, 75, 90, 105, 120, 135 };
        public static readonly int[] ShadowShiftY = { -80, -100, -120, -140, -160, -180 };

        public static VisualId[] AllVisuals()
        {
            return (VisualId[])Enum.GetValues(typeof(VisualId));
        }

        public static ArtLayer Make(
            LayerColour colour,
            int region,
            Rgb fixedColour,
            AtlasRect rect,
            bool isLogo = false,
            int minX = 0,
            int minY = 0,
            int maxX = 1024,
            int maxY = 1024,
            int sliceInset = 0,
            int sliceWorld = 0,
            int tile = 0,
            int shiftX = 0,
            int shiftY = 0)
        {
            return new ArtLayer(colour, region, fixedColour, rect, isLogo, minX, minY, maxX, maxY, sliceInset, sliceWorld, tile, shiftX, shiftY);
        }

        public static ArtLayer Role(string cell, int tile = 0, int sliceInset = 0, int sliceWorld = 0)
        {
            return Make(LayerColour.Role, 0, default(Rgb), ArtCells.RectOf(cell), sliceInset: sliceInset, sliceWorld: sliceWorld, tile: tile);
        }

        public static ArtLayer RoleAt(in ArtCell cell)
        {
            return Make(LayerColour.Role, 0, default(Rgb), ArtCells.RectOf(cell));
        }

        public static ArtLayer Region(in ArtCell cell, int region)
        {
            return Make(LayerColour.Region, region, default(Rgb), ArtCells.RectOf(cell));
        }

        public static ArtLayer Fixed(in ArtCell cell, Rgb colour, int shiftX = 0, int shiftY = 0)
        {
            return Make(LayerColour.Fixed, 0, colour, ArtCells.RectOf(cell), shiftX: shiftX, shiftY: shiftY);
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
            return Make(LayerColour.Region, 4, default(Rgb), default(AtlasRect), true, minX, minY, maxX, maxY);
        }

        /// <summary>GroundLayer()'s definition (15 §15.17 Public surface).</summary>
        public static ArtLayer Ground()
        {
            return Make(LayerColour.Fixed, 0, GrassGreen, ArtCells.RectOf("Grass"), tile: ArtCells.GroundTile);
        }

        private static List<ArtLayer> Building(int shiftX, int shiftY)
        {
            return new List<ArtLayer>
            {
                Make(LayerColour.Fixed, 0, Black, ArtCells.RectOf("SoftBox"), sliceInset: 128, sliceWorld: 300, shiftX: shiftX, shiftY: shiftY),
                Role("Roof", tile: 32),
                Role("Parapet", sliceInset: 128, sliceWorld: 300),
            };
        }

        public static List<ArtLayer> Layers(VisualId v)
        {
            switch (v)
            {
                case VisualId.RunwaySurface:
                    return new List<ArtLayer>
                    {
                        Role("Asphalt", tile: 32),
                        Make(LayerColour.Fixed, 0, RubberDark, ArtCells.RectOf("Rubber"), minX: 320, minY: 77, maxX: 704, maxY: 307),
                        Make(LayerColour.Fixed, 0, RubberDark, ArtCells.RectOf("Rubber"), minX: 320, minY: 717, maxX: 704, maxY: 947),
                    };
                case VisualId.TaxiwaySurface:
                    return One(Role("Asphalt", tile: 32));
                case VisualId.Apron:
                    return One(Role("Concrete", tile: 64));
                case VisualId.QueueFill:
                    return One(Role("Solid"));
                case VisualId.RunwayEdgeLines:
                    return One(Role("RunwayEdgeLines"));
                case VisualId.RunwayThreshold:
                    return One(Role("RunwayThreshold"));
                case VisualId.RunwayCentreDash:
                case VisualId.TaxiwayCentreline:
                    return One(Role("CentreStripe"));
                case VisualId.TaxiwayJunction:
                    return One(Role("Disc"));
                case VisualId.TerminalBuilding:
                    return Building(600, -800);
                case VisualId.Pier:
                    return Building(450, -600);
                case VisualId.ControlTower:
                    return new List<ArtLayer>
                    {
                        Make(LayerColour.Fixed, 0, Black, ArtCells.RectOf("SoftBox"), sliceInset: 128, sliceWorld: 300, shiftX: 900, shiftY: -1200),
                        Role("ControlTower"),
                    };
                case VisualId.JetBridge:
                    return new List<ArtLayer>
                    {
                        Fixed(ArtCells.Named("SoftBar"), Black, 150, -200),
                        Role("JetBridge"),
                    };
                case VisualId.StandPad:
                    return new List<ArtLayer>
                    {
                        Role("Concrete", tile: 64),
                        Make(LayerColour.Fixed, 0, StandRed, ArtCells.RectOf("StandPad"), sliceInset: 128, sliceWorld: 200),
                    };
                case VisualId.StandLeadIn:
                    return new List<ArtLayer>
                    {
                        Role("StandLeadIn"),
                        Fixed(ArtCells.Named("GseBody"), GseYellow),
                        Fixed(ArtCells.Named("GseDetail"), GseDark),
                    };
                case VisualId.TerminalZone:
                    return One(Role("TerminalZone", sliceInset: 128, sliceWorld: 100));
                case VisualId.LanePip:
                    return One(Role("LanePip"));
                case VisualId.Passenger:
                    return new List<ArtLayer>
                    {
                        Role("PaxOutline"),
                        Region(ArtCells.Named("PaxBottom"), 1),
                        Region(ArtCells.Named("PaxBag"), 4),
                        Region(ArtCells.Named("PaxTop"), 0),
                        Region(ArtCells.Named("PaxSkin"), 2),
                        Region(ArtCells.Named("PaxHair"), 3),
                    };
            }

            if (v >= VisualId.MarkingDigit0 && v <= VisualId.MarkingDigit9)
            {
                return One(Role("Digit" + (v - VisualId.MarkingDigit0).ToString(CultureInfo.InvariantCulture)));
            }

            if (v >= VisualId.AircraftA && v <= VisualId.AircraftF)
            {
                int s = v - VisualId.AircraftA;
                return new List<ArtLayer>
                {
                    Fixed(ArtCells.Aircraft(s, 0), Black, ShadowShiftX[s], ShadowShiftY[s]),
                    RoleAt(ArtCells.Aircraft(s, 1)),
                    Fixed(ArtCells.Aircraft(s, 2), WingGrey),
                    Region(ArtCells.Aircraft(s, 3), 3),
                    Region(ArtCells.Aircraft(s, 4), 0),
                    Region(ArtCells.Aircraft(s, 5), 2),
                    Region(ArtCells.Aircraft(s, 6), 1),
                    Logo(s),
                    Fixed(ArtCells.Aircraft(s, 7), GlazingDark),
                };
            }

            throw new ArgumentOutOfRangeException(nameof(v), v, "VisualId not in 15 §15.17's table");
        }

        private static List<ArtLayer> One(ArtLayer l)
        {
            return new List<ArtLayer> { l };
        }
    }

    internal static class ArtShow
    {
        public static string Rect(in AtlasRect r)
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:R},{1:R},{2:R},{3:R}]", r.U0, r.V0, r.U1, r.V1);
        }

        public static string Layer(in ArtLayer l)
        {
            // A logo layer's Rect is unused (it draws from the Mark's cell), so it is not shown.
            string rect = l.IsLogo ? "logo" : Rect(l.Rect);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} region={1} fixed={2} {3} box=({4},{5})-({6},{7}) slice={8}/{9} tile={10} shift=({11},{12})",
                l.Colour,
                l.Region,
                Prims.Show(l.Fixed),
                rect,
                l.MinX,
                l.MinY,
                l.MaxX,
                l.MaxY,
                l.SliceInset,
                l.SliceWorld,
                l.Tile,
                l.ShiftX,
                l.ShiftY);
        }

        public static string Layers(IReadOnlyList<ArtLayer> layers)
        {
            var parts = new List<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                parts.Add(i.ToString(CultureInfo.InvariantCulture) + ": " + Layer(layers[i]));
            }

            return string.Join("\n", parts);
        }
    }

    /// <summary>15 §15.17's literal sRGB-to-linear table L (Q-131).</summary>
    internal static class SrgbTable
    {
        private static readonly byte[] Table =
        {
              0,   0,   0,   0,   0,   0,   0,   1,   1,   1,   1,   1,   1,   1,   1,   1,
              1,   1,   2,   2,   2,   2,   2,   2,   2,   2,   3,   3,   3,   3,   3,   3,
              4,   4,   4,   4,   4,   5,   5,   5,   5,   6,   6,   6,   6,   7,   7,   7,
              8,   8,   8,   8,   9,   9,   9,  10,  10,  10,  11,  11,  12,  12,  12,  13,
             13,  13,  14,  14,  15,  15,  16,  16,  17,  17,  17,  18,  18,  19,  19,  20,
             20,  21,  22,  22,  23,  23,  24,  24,  25,  25,  26,  27,  27,  28,  29,  29,
             30,  30,  31,  32,  32,  33,  34,  35,  35,  36,  37,  37,  38,  39,  40,  41,
             41,  42,  43,  44,  45,  45,  46,  47,  48,  49,  50,  51,  51,  52,  53,  54,
             55,  56,  57,  58,  59,  60,  61,  62,  63,  64,  65,  66,  67,  68,  69,  70,
             71,  72,  73,  74,  76,  77,  78,  79,  80,  81,  82,  84,  85,  86,  87,  88,
             90,  91,  92,  93,  95,  96,  97,  99, 100, 101, 103, 104, 105, 107, 108, 109,
            111, 112, 114, 115, 116, 118, 119, 121, 122, 124, 125, 127, 128, 130, 131, 133,
            134, 136, 138, 139, 141, 142, 144, 146, 147, 149, 151, 152, 154, 156, 157, 159,
            161, 163, 164, 166, 168, 170, 171, 173, 175, 177, 179, 181, 183, 184, 186, 188,
            190, 192, 194, 196, 198, 200, 202, 204, 206, 208, 210, 212, 214, 216, 218, 220,
            222, 224, 226, 229, 231, 233, 235, 237, 239, 242, 244, 246, 248, 250, 253, 255,
        };

        public static int L(int c)
        {
            return Table[c];
        }

        public static int Count => Table.Length;
    }

    /// <summary>Frames and primitives for the tessellator; layer and source do not affect it.</summary>
    internal static class ArtFrames
    {
        /// <summary>A camera whose view, 27..37 on both axes, needs exactly one ground tile (0..64).</summary>
        public static CameraView OneTile => Cam.At(32f, 32f, 10f, 1f);

        public const int GroundQuads = 1;

        public static int RoleCount => Enum.GetValues(typeof(ColourRole)).Length;

        public static RenderFrame Of(params DrawPrimitive[] prims)
        {
            return new RenderFrame(0UL, OneTile, prims, Gfx.High());
        }

        public static RenderFrame At(in CameraView camera, params DrawPrimitive[] prims)
        {
            return new RenderFrame(0UL, camera, prims, Gfx.High());
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
    }

    /// <summary>One expected quad: corners in double, UVs as float (computed in double, converted once), and RGBA.</summary>
    internal sealed class ExpQuad
    {
        public ExpQuad(string what)
        {
            What = what;
        }

        public string What { get; }

        public double[] Corners { get; } = new double[8];

        public float[] Uvs { get; } = new float[8];

        public byte[] Colour { get; } = new byte[4];
    }

    /// <summary>
    /// 15 §15.17 "Tessellation", restated from the spec as the expected output of
    /// Fill, over ArtTable (the spec's layer table), never over LayersOf.
    /// </summary>
    internal static class ArtRef
    {
        public static List<ExpQuad> Fill(in RenderFrame frame, Rgba[] roles, bool linear)
        {
            var quads = Ground(frame.Camera, linear, out _, out _, out _);
            for (int i = 0; i < frame.Primitives.Count; i++)
            {
                AddPrimitive(quads, frame.Primitives[i], roles, linear, "prim " + i);
            }

            return quads;
        }

        /// <summary>The ground: whole tiles of GroundLayer() over the camera's view, T = GROUND_TILE × 2^k.</summary>
        public static List<ExpQuad> Ground(in CameraView cam, bool linear, out long nx, out long ny, out double tile)
        {
            double hw = cam.ViewHeight * (double)cam.Aspect / 2.0;
            double hh = cam.ViewHeight / 2.0;
            double minX = cam.Centre.X - hw;
            double maxX = cam.Centre.X + hw;
            double minY = cam.Centre.Y - hh;
            double maxY = cam.Centre.Y + hh;
            double t = 0;
            nx = 0;
            ny = 0;
            for (int k = 0; k <= 24; k++)
            {
                t = ArtCells.GroundTile * Math.Pow(2, k);
                nx = (long)Math.Floor(maxX / t) - (long)Math.Floor(minX / t) + 1;
                ny = (long)Math.Floor(maxY / t) - (long)Math.Floor(minY / t) + 1;
                if (nx <= ArtCells.GroundTilesPerAxis && ny <= ArtCells.GroundTilesPerAxis)
                {
                    break;
                }
            }

            tile = t;
            long ix0 = (long)Math.Floor(minX / t);
            long iy0 = (long)Math.Floor(minY / t);
            AtlasRect r = ArtCells.RectOf("Grass");
            var quads = new List<ExpQuad>();
            for (long iy = iy0; iy < iy0 + ny; iy++)
            {
                for (long ix = ix0; ix < ix0 + nx; ix++)
                {
                    var q = new ExpQuad("ground (" + ix + "," + iy + ")");
                    SetBox(q, ix * t, iy * t, (ix + 1) * t, (iy + 1) * t);
                    SetUvs(q, r.U0, r.V0, r.U1, r.V1);
                    SetColour(q, ArtTable.GrassGreen.R, ArtTable.GrassGreen.G, ArtTable.GrassGreen.B, 255, linear);
                    quads.Add(q);
                }
            }

            return quads;
        }

        public static void AddPrimitive(List<ExpQuad> quads, in DrawPrimitive p, Rgba[] roles, bool linear, string what)
        {
            List<ArtLayer> layers = ArtTable.Layers(p.Visual);
            for (int li = 0; li < layers.Count; li++)
            {
                ArtLayer l = layers[li];
                if (l.IsLogo && p.Paint.Mark == 0)
                {
                    continue;
                }

                AtlasRect r = l.IsLogo ? ArtCells.RectOf(ArtCells.Logo((LogoMark)p.Paint.Mark)) : l.Rect;
                byte cr;
                byte cg;
                byte cb;
                byte ca;
                switch (l.Colour)
                {
                    case LayerColour.Role:
                        Rgba role = roles[(int)p.Colour];
                        cr = role.R;
                        cg = role.G;
                        cb = role.B;
                        ca = role.A;
                        break;
                    case LayerColour.Region:
                        Rgb c = ArtFrames.Region(p.Paint, l.Region);
                        cr = c.R;
                        cg = c.G;
                        cb = c.B;
                        ca = 255;
                        break;
                    default:
                        cr = l.Fixed.R;
                        cg = l.Fixed.G;
                        cb = l.Fixed.B;
                        ca = 255;
                        break;
                }

                string lw = what + " " + p.Kind + " " + p.Visual + " layer " + li;
                int first = quads.Count;
                switch (p.Kind)
                {
                    case PrimitiveKind.Box:
                        AddBox(quads, p, l, r, lw);
                        break;
                    case PrimitiveKind.Segment:
                        AddSegment(quads, p, l, r, lw);
                        break;
                    default:
                        AddDot(quads, p, l, r, lw);
                        break;
                }

                double sx = l.ShiftX / 100.0;
                double sy = l.ShiftY / 100.0;
                for (int q = first; q < quads.Count; q++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        quads[q].Corners[2 * k] += sx;
                        quads[q].Corners[(2 * k) + 1] += sy;
                    }

                    SetColour(quads[q], cr, cg, cb, ca, linear);
                }
            }
        }

        private static void AddBox(List<ExpQuad> quads, in DrawPrimitive p, in ArtLayer l, in AtlasRect r, string what)
        {
            double x0 = p.A.X;
            double y0 = p.A.Y;
            double x1 = p.B.X;
            double y1 = p.B.Y;
            if (l.Tile > 0)
            {
                double t = l.Tile;
                long ix0 = (long)Math.Floor(x0 / t);
                long iy0 = (long)Math.Floor(y0 / t);
                for (long iy = iy0; iy * t < y1; iy++)
                {
                    double qy0 = Math.Max(y0, iy * t);
                    double qy1 = Math.Min(y1, (iy + 1) * t);
                    if (qy1 <= qy0)
                    {
                        continue;
                    }

                    for (long ix = ix0; ix * t < x1; ix++)
                    {
                        double qx0 = Math.Max(x0, ix * t);
                        double qx1 = Math.Min(x1, (ix + 1) * t);
                        if (qx1 <= qx0)
                        {
                            continue;
                        }

                        var q = new ExpQuad(what + " tile (" + ix + "," + iy + ")");
                        SetBox(q, qx0, qy0, qx1, qy1);
                        SetUvs(
                            q,
                            (float)(r.U0 + ((qx0 - (ix * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((qy0 - (iy * t)) / t * ((double)r.V1 - r.V0))),
                            (float)(r.U0 + ((qx1 - (ix * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((qy1 - (iy * t)) / t * ((double)r.V1 - r.V0))));
                        quads.Add(q);
                    }
                }

                return;
            }

            if (l.SliceInset > 0)
            {
                double t = Math.Min(l.SliceWorld / 100.0, Math.Min((x1 - x0) / 2.0, (y1 - y0) / 2.0));
                double du = ((double)r.U1 - r.U0) * l.SliceInset / 1024.0;
                double dv = ((double)r.V1 - r.V0) * l.SliceInset / 1024.0;
                double[] xs = { x0, x0 + t, x1 - t, x1 };
                double[] ys = { y0, y0 + t, y1 - t, y1 };
                float[] us = { r.U0, (float)(r.U0 + du), (float)(r.U1 - du), r.U1 };
                float[] vs = { r.V0, (float)(r.V0 + dv), (float)(r.V1 - dv), r.V1 };
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        var q = new ExpQuad(what + " slice (row " + row + ", col " + col + ")");
                        SetBox(q, xs[col], ys[row], xs[col + 1], ys[row + 1]);
                        SetUvs(q, us[col], vs[row], us[col + 1], vs[row + 1]);
                        quads.Add(q);
                    }
                }

                return;
            }

            var one = new ExpQuad(what);
            SetBox(one, x0, y0, x1, y1);
            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
            quads.Add(one);
        }

        private static void AddSegment(List<ExpQuad> quads, in DrawPrimitive p, in ArtLayer l, in AtlasRect r, string what)
        {
            double ax = p.A.X;
            double ay = p.A.Y;
            double dx = p.B.X - ax;
            double dy = p.B.Y - ay;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            double fx = len == 0.0 ? 0.0 : dx / len;
            double fy = len == 0.0 ? 1.0 : dy / len;
            double rx = fy;
            double ry = -fx;
            double size = p.Size;
            double h = size / 2.0;
            if (l.Tile > 0)
            {
                if (len == 0.0 || size == 0.0)
                {
                    return;
                }

                double t = l.Tile;
                for (long i = 0; i * t < len; i++)
                {
                    double y0 = i * t;
                    double y1 = Math.Min(y0 + t, len);
                    for (long j = 0; -h + (j * t) < h; j++)
                    {
                        double x0 = -h + (j * t);
                        double x1 = Math.Min(x0 + t, h);
                        var q = new ExpQuad(what + " tile (" + i + "," + j + ")");
                        double[] xs = { x0, x1, x1, x0 };
                        double[] ys = { y0, y0, y1, y1 };
                        for (int k = 0; k < 4; k++)
                        {
                            q.Corners[2 * k] = ax + (rx * xs[k]) + (fx * ys[k]);
                            q.Corners[(2 * k) + 1] = ay + (ry * xs[k]) + (fy * ys[k]);
                        }

                        SetUvs(
                            q,
                            (float)(r.U0 + ((x0 + h - (j * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((y0 - (i * t)) / t * ((double)r.V1 - r.V0))),
                            (float)(r.U0 + ((x1 + h - (j * t)) / t * ((double)r.U1 - r.U0))),
                            (float)(r.V0 + ((y1 - (i * t)) / t * ((double)r.V1 - r.V0))));
                        quads.Add(q);
                    }
                }

                return;
            }

            var one = new ExpQuad(what);
            int[] sxs = { l.MinX, l.MaxX, l.MaxX, l.MinX };
            int[] sys = { l.MinY, l.MinY, l.MaxY, l.MaxY };
            for (int k = 0; k < 4; k++)
            {
                double a = ((sxs[k] / 1024.0) - 0.5) * size;
                double b = (sys[k] / 1024.0) * len;
                one.Corners[2 * k] = ax + (rx * a) + (fx * b);
                one.Corners[(2 * k) + 1] = ay + (ry * a) + (fy * b);
            }

            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
            quads.Add(one);
        }

        private static void AddDot(List<ExpQuad> quads, in DrawPrimitive p, in ArtLayer l, in AtlasRect r, string what)
        {
            var one = new ExpQuad(what);
            double[] c = ArtGeometry.Dot(p.A.X, p.A.Y, p.Size, p.Facing.X, p.Facing.Y, l.MinX, l.MinY, l.MaxX, l.MaxY);
            Array.Copy(c, one.Corners, 8);
            SetUvs(one, r.U0, r.V0, r.U1, r.V1);
            quads.Add(one);
        }

        private static void SetBox(ExpQuad q, double x0, double y0, double x1, double y1)
        {
            double[] c = { x0, y0, x1, y0, x1, y1, x0, y1 };
            Array.Copy(c, q.Corners, 8);
        }

        private static void SetUvs(ExpQuad q, float u0, float v0, float u1, float v1)
        {
            float[] uv = { u0, v0, u1, v0, u1, v1, u0, v1 };
            Array.Copy(uv, q.Uvs, 8);
        }

        private static void SetColour(ExpQuad q, int r, int g, int b, int a, bool linear)
        {
            q.Colour[0] = (byte)(linear ? SrgbTable.L(r) : r);
            q.Colour[1] = (byte)(linear ? SrgbTable.L(g) : g);
            q.Colour[2] = (byte)(linear ? SrgbTable.L(b) : b);
            q.Colour[3] = (byte)a;
        }
    }

    /// <summary>
    /// Assertions on the tessellator's buffers: corners within the one allowed
    /// tolerance (15 §15.3), UVs and colours exactly.
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

        public static double[] Shifted(double[] corners, double sx, double sy)
        {
            var c = (double[])corners.Clone();
            for (int k = 0; k < 4; k++)
            {
                c[2 * k] += sx;
                c[(2 * k) + 1] += sy;
            }

            return c;
        }

        /// <summary>Corners 0..3 are (U0,V0), (U1,V0), (U1,V1), (U0,V1).</summary>
        public static float[] Uvs(in AtlasRect r)
        {
            return new[] { r.U0, r.V0, r.U1, r.V0, r.U1, r.V1, r.U0, r.V1 };
        }

        public static float[] Uvs(int u0, int v0, int u1, int v1)
        {
            float a = (float)(u0 / 4096.0);
            float b = (float)(v0 / 4096.0);
            float c = (float)(u1 / 4096.0);
            float d = (float)(v1 / 4096.0);
            return new[] { a, b, c, b, c, d, a, d };
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

        /// <summary>The whole output of the last Fill equals the expected quads, in order.</summary>
        public static void AssertMatches(ISpriteTessellator t, List<ExpQuad> expected, string what)
        {
            Assert.True(expected.Count == t.QuadCount, what + ": " + t.QuadCount + " quads, expected " + expected.Count);
            for (int q = 0; q < expected.Count; q++)
            {
                ExpQuad e = expected[q];
                AssertQuad(t, q, e.Corners, e.Uvs, what + ", " + e.What);
                AssertColour(t, q, e.Colour[0], e.Colour[1], e.Colour[2], e.Colour[3], what + ", " + e.What);
            }
        }
    }

    /// <summary>One atlas build shared by a test class (15 §15.18: about 85 MiB, built once per class).</summary>
    public sealed class Art2DAtlasFixture
    {
        public Art2DAtlasFixture()
        {
            Atlas = Art2DFactory.BuildAtlas();
        }

        public SpriteAtlas Atlas { get; }
    }
}
