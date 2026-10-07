using System;
using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 "Tessellation" (Q-130, as amended by Q-131). Corners are compared
    /// within §15.3's one tolerance (1e-3 world units) using §15.17's chosen
    /// vectors; Uvs and colours exactly. Every Fill emits the ground first; the
    /// frames here use a camera that needs one ground tile, unless a test is
    /// about the ground.
    /// </summary>
    public sealed class Art2DTessellatorTests
    {
        private const int G = ArtFrames.GroundQuads;

        [Fact]
        public void test_art2d_tessellator_corners_follow_kind_facing_and_slicing()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            float[] solid = ArtGeometry.Uvs(4, 3588, 124, 3708);
            float[] softBar = ArtGeometry.Uvs(388, 3716, 508, 3836);
            float[] jetBridge = ArtGeometry.Uvs(900, 3588, 1020, 3708);

            // The ground comes first: one tile (0,0)–(64,64) for this camera.
            Assert.Equal(G + 1, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.QueueFill, ColourRole.QueueFill, 10f, 20f, 110f, 70f)), roles, false));
            Assert.Equal(G + 1, t.QuadCount);
            ArtGeometry.AssertQuad(t, 0, new double[] { 0, 0, 64, 0, 64, 64, 0, 64 }, ArtGeometry.Uvs(1040, 3088, 1520, 3568), "ground tile");

            // Box: (MinX,MinY), (MaxX,MinY), (MaxX,MaxY), (MinX,MaxY), never rotated.
            ArtGeometry.AssertQuad(t, G, new double[] { 10, 20, 110, 20, 110, 70, 10, 70 }, solid, "queue fill box");

            // Segment: f = (B − A)/|B − A|, r = (f.Y, −f.X), h = Size/2:
            // A − r h, A + r h, B + r h, B − r h. JetBridge's shadow is shifted (1.5, −2), never rotated.
            Assert.Equal(G + 2, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.JetBridge, ColourRole.Building, 100f, 200f, 400f, 600f, 45f)), roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { 83.5, 211.5, 119.5, 184.5, 419.5, 584.5, 383.5, 611.5 }, softBar, "jet bridge shadow, shifted");
            ArtGeometry.AssertQuad(t, G + 1, new double[] { 82, 213.5, 118, 186.5, 418, 586.5, 382, 613.5 }, jetBridge, "jet bridge, diagonal");

            Assert.Equal(G + 1, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.RunwayEdgeLines, ColourRole.RunwayMarking, -2000f, 0f, 0f, 0f, 45f)), roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { -2000, 22.5, -2000, -22.5, 0, -22.5, 0, 22.5 }, ArtGeometry.Uvs(ArtCells.RectOf("RunwayEdgeLines")), "axis segment");

            // A zero-length segment of an untiled visual uses f = (0, 1).
            Assert.Equal(G + 2, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.JetBridge, ColourRole.Building, 5f, 5f, 5f, 5f, 10f)), roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { 1.5, 3, 11.5, 3, 11.5, 3, 1.5, 3 }, softBar, "zero-length jet bridge shadow");
            ArtGeometry.AssertQuad(t, G + 1, new double[] { 0, 5, 10, 5, 10, 5, 0, 5 }, jetBridge, "zero-length jet bridge");

            // Dot: f is Facing normalised, or (0, 1) for (0, 0); r = (f.Y, −f.X):
            // centre − r h − f h, centre + r h − f h, centre + r h + f h, centre − r h + f h.
            float[] disc = ArtGeometry.Uvs(ArtCells.RectOf("Disc"));
            AssertDisc(t, roles, 0f, 0f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }, disc);
            AssertDisc(t, roles, 0f, 1f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }, disc);
            AssertDisc(t, roles, 1f, 0f, new double[] { 990, -1990, 990, -2010, 1010, -2010, 1010, -1990 }, disc);
            AssertDisc(t, roles, -1f, 0f, new double[] { 1010, -2010, 1010, -1990, 990, -1990, 990, -2010 }, disc);
            AssertDisc(t, roles, 0f, -1f, new double[] { 1010, -1990, 990, -1990, 990, -2010, 1010, -2010 }, disc);
            AssertDisc(t, roles, 3f, 4f, new double[] { 986, -2002, 1002, -2014, 1014, -1998, 998, -1986 }, disc);
            AssertDisc(t, roles, 1f, 1f, ArtGeometry.Dot(1000, -2000, 20, 1, 1), disc);

            // The logo sub-square: AircraftC (447,250)–(578,381) at Size 512 is
            // (−32.5,−131)–(33,−65.5) unrotated. The shadow (layer 0) is shifted (0.9, −1.2).
            Paint disk = ArtFrames.Paint((byte)LogoMark.Disc);
            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 0f, 0f, disk)), roles, false));
            double[] whole = { -256, -256, 256, -256, 256, 256, -256, 256 };
            ArtGeometry.AssertQuad(t, G, new double[] { -255.1, -257.2, 256.9, -257.2, 256.9, 254.8, -255.1, 254.8 }, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, 0))), "AircraftC shadow");
            for (int l = 1; l <= 6; l++)
            {
                ArtGeometry.AssertQuad(t, G + l, whole, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, l))), "AircraftC layer " + l);
            }

            ArtGeometry.AssertQuad(t, G + 7, new double[] { -32.5, -131, 33, -131, 33, -65.5, -32.5, -65.5 }, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Disc)), "AircraftC logo");
            ArtGeometry.AssertQuad(t, G + 8, whole, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, 7))), "AircraftC glazing");

            // Rotated: facing (1, 0) takes design (447, 250) to (−131, 32.5); the shadow's shift is not rotated.
            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 1f, 0f, disk)), roles, false));
            ArtGeometry.AssertCorners(t, G + 7, new double[] { -131, 32.5, -131, -33, -65.5, -33, -65.5, 32.5 }, "AircraftC logo facing (1,0)");
            ArtGeometry.AssertCorners(t, G, ArtGeometry.Shifted(ArtGeometry.Dot(0, 0, 512, 1, 0), 0.9, -1.2), "AircraftC shadow facing (1,0)");

            // Slicing, read from the layers' fields. The terminal (20 m): SoftBox
            // sliced 128 ↔ 3 m and shifted (6, −8), Roof tiled 32, Parapet sliced.
            Assert.Equal(G + 19, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.TerminalBuilding, ColourRole.Building, 0f, 0f, 30f, 20f)), roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { 6, -8, 9, -8, 9, -5, 6, -5 }, ArtGeometry.Uvs(772, 3588, 787, 3603), "terminal shadow slice 0");
            ArtGeometry.AssertQuad(t, G + 9, new double[] { 0, 0, 30, 0, 30, 20, 0, 20 }, ArtGeometry.Uvs(1552, 3088, 2002, 3388), "terminal roof, one partial tile");
            ArtGeometry.AssertQuad(t, G + 14, new double[] { 3, 3, 27, 3, 27, 17, 3, 17 }, ArtGeometry.Uvs(659, 3603, 749, 3693), "terminal parapet centre slice");
            ArtGeometry.AssertQuad(t, G + 18, new double[] { 27, 17, 30, 17, 30, 20, 27, 20 }, ArtGeometry.Uvs(749, 3693, 764, 3708), "terminal parapet top-right slice");

            // A thin pier clamps t to half its height (2), and a thin zone to 0.5.
            Assert.Equal(G + 22, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.Pier, ColourRole.Building, 0f, 0f, 100f, 4f)), roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { 4.5, -6, 6.5, -6, 6.5, -4, 4.5, -4 }, ArtGeometry.Uvs(772, 3588, 787, 3603), "pier shadow slice 0, t = 2");
            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.TerminalZone, ColourRole.LandsideNode, 0f, 0f, 10f, 1f)), roles, false));
            ArtGeometry.AssertQuad(t, G + 4, new double[] { 0.5, 0.5, 9.5, 0.5, 9.5, 0.5, 0.5, 0.5 }, ArtGeometry.Uvs(2579, 3603, 2669, 3693), "zone centre slice, t = 0.5");

            // Every Box visual, against the spec's rules (ArtRef over §15.17's table).
            var boxes = new (VisualId V, ColourRole R, float X0, float Y0, float X1, float Y1)[]
            {
                (VisualId.Apron, ColourRole.Apron, -10f, 20f, 150f, 140f),
                (VisualId.QueueFill, ColourRole.QueueFill, 1f, 2f, 9f, 4f),
                (VisualId.TerminalBuilding, ColourRole.Building, 0f, 0f, 30f, 20f),
                (VisualId.Pier, ColourRole.Building, 0f, 0f, 100f, 4f),
                (VisualId.ControlTower, ColourRole.Building, 920f, 200f, 940f, 220f),
                (VisualId.StandPad, ColourRole.StandFree, -20f, -20f, 20f, 20f),
                (VisualId.TerminalZone, ColourRole.LandsideNode, 0f, 0f, 10f, 1f),
            };
            foreach (var b in boxes)
            {
                RenderFrame f = ArtFrames.Of(ArtFrames.Box(b.V, b.R, b.X0, b.Y0, b.X1, b.Y1));
                t.Fill(f, roles, false);
                ArtGeometry.AssertMatches(t, ArtRef.Fill(f, roles, false), b.V + " box");
            }

            // Every visual as a Dot at §15.17's facings, and as a Segment, against the
            // spec's rules. Tiling applies to Box and Segment layers and slicing to Box
            // layers only, so a visual with such a layer is not drawn here as a kind
            // the field does not apply to.
            Paint ring = ArtFrames.Paint((byte)LogoMark.Ring);
            var facings = new (float X, float Y)[] { (0f, 0f), (0f, 1f), (1f, 0f), (-1f, 0f), (0f, -1f), (3f, 4f), (1f, 1f) };
            int dots = 0;
            int segments = 0;
            foreach (VisualId v in ArtTable.AllVisuals())
            {
                bool tiled = false;
                bool sliced = false;
                foreach (ArtLayer l in ArtTable.Layers(v))
                {
                    tiled |= l.Tile > 0;
                    sliced |= l.SliceInset > 0;
                }

                if (!sliced)
                {
                    RenderFrame seg = ArtFrames.Of(ArtFrames.Segment(v, ColourRole.Agent, 0f, 0f, 0f, 64f, 16f));
                    t.Fill(seg, roles, false);
                    ArtGeometry.AssertMatches(t, ArtRef.Fill(seg, roles, false), v + " segment");
                    segments++;
                }

                if (tiled || sliced)
                {
                    continue;
                }

                dots++;
                foreach ((float fx, float fy) in facings)
                {
                    RenderFrame f = ArtFrames.Of(ArtFrames.Dot(v, ColourRole.Agent, 1000f, -2000f, 20f, fx, fy, ring));
                    t.Fill(f, roles, false);
                    ArtGeometry.AssertMatches(t, ArtRef.Fill(f, roles, false), v + " dot facing (" + fx + "," + fy + ")");
                }

                RenderFrame big = ArtFrames.Of(ArtFrames.Dot(v, ColourRole.Agent, 9000f, -9000f, 1000f, 1f, 1f, ring));
                t.Fill(big, roles, false);
                ArtGeometry.AssertMatches(t, ArtRef.Fill(big, roles, false), v + " dot size 1000 facing (1,1)");
            }

            Assert.True(dots >= 20 && segments >= 25, "only " + dots + " dot and " + segments + " segment visuals checked");
        }

        [Fact]
        public void test_art2d_tessellator_ground_tiles_follow_the_camera()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            float[] grass = ArtGeometry.Uvs(1040, 3088, 1520, 3568);

            // k = 0: view −125..325 by −200..100, T = 64: nx = 5 − (−2) + 1 = 8, ny = 1 − (−4) + 1 = 6.
            CameraView near = Cam.At(100f, -50f, 300f, 1.5f);
            ArtRef.Ground(near, false, out long nx, out long ny, out double tile);
            Assert.Equal(new long[] { 8, 6 }, new[] { nx, ny });
            Assert.Equal(64.0, tile);
            Assert.Equal(48, t.Fill(ArtFrames.At(near), roles, false));
            int q = 0;
            for (int iy = -4; iy <= 1; iy++)
            {
                for (int ix = -2; ix <= 5; ix++)
                {
                    string what = "ground k=0 tile (" + ix + "," + iy + ")";
                    ArtGeometry.AssertQuad(t, q, new double[] { 64 * ix, 64 * iy, 64 * (ix + 1), 64 * iy, 64 * (ix + 1), 64 * (iy + 1), 64 * ix, 64 * (iy + 1) }, grass, what);
                    ArtGeometry.AssertColour(t, q, 0x6F, 0x8F, 0x5E, 255, what);
                    q++;
                }
            }

            Assert.Equal(48, t.Fill(ArtFrames.At(near), roles, true));
            ArtGeometry.AssertColour(t, 0, SrgbTable.L(0x6F), SrgbTable.L(0x8F), SrgbTable.L(0x5E), 255, "ground, linear");

            // k = 3: view −5000..5000 by −2500..2500 needs T = 512: nx = 20, ny = 10.
            CameraView far = Cam.At(0f, 0f, 5000f, 2f);
            ArtRef.Ground(far, false, out nx, out ny, out tile);
            Assert.Equal(new long[] { 20, 10 }, new[] { nx, ny });
            Assert.Equal(512.0, tile);
            Assert.Equal(200, t.Fill(ArtFrames.At(far), roles, false));
            ArtGeometry.AssertQuad(t, 0, new double[] { -5120, -2560, -4608, -2560, -4608, -2048, -5120, -2048 }, grass, "ground k=3 first tile (−10,−5)");
            ArtGeometry.AssertQuad(t, 199, new double[] { 4608, 2048, 5120, 2048, 5120, 2560, 4608, 2560 }, grass, "ground k=3 last tile (9,4)");
            ArtGeometry.AssertMatches(t, ArtRef.Ground(far, false, out _, out _, out _), "ground k=3");

            // A view edge on a multiple of T still counts the tile beyond it: view 0..128 by −64..64 gives 3 × 3.
            CameraView edge = Cam.At(64f, 0f, 128f, 1f);
            Assert.Equal(9, t.Fill(ArtFrames.At(edge), roles, false));
            ArtGeometry.AssertMatches(t, ArtRef.Ground(edge, false, out _, out _, out _), "ground on tile edges");

            // At most 1024 quads, whatever the zoom and aspect, and always the spec's tiles.
            foreach (float height in new[] { 1f, 37f, 120f, 999f, 4096f, 65536f, 1000000f })
            {
                foreach (float aspect in new[] { 0.25f, 1f, 1.7777778f, 4f })
                {
                    CameraView cam = Cam.At(-3333f, 1234f, height, aspect);
                    int n = t.Fill(ArtFrames.At(cam), roles, false);
                    Assert.True(n <= 1024, "ground of " + n + " quads for view height " + height + ", aspect " + aspect);
                    ArtGeometry.AssertMatches(t, ArtRef.Ground(cam, false, out _, out _, out _), "ground view " + height + " × " + aspect);
                }
            }

            // The ground comes before the primitives.
            RenderFrame withBox = ArtFrames.At(near, ArtFrames.Box(VisualId.QueueFill, ColourRole.QueueFill, 0f, 0f, 1f, 1f));
            Assert.Equal(49, t.Fill(withBox, roles, false));
            ArtGeometry.AssertMatches(t, ArtRef.Fill(withBox, roles, false), "ground then a box");
            ArtGeometry.AssertUvs(t, 48, ArtGeometry.Uvs(4, 3588, 124, 3708), "the box after the ground");
        }

        [Fact]
        public void test_art2d_tessellator_tiles_boxes_and_segments()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();

            // A tiled box (Apron, Concrete tile 64) cut by the world grid at multiples of 64:
            // x −10|0|64|128|150, y 20|64|128|140: 4 × 3 quads, bottom row first,
            // with U = U0 + (x − ix·T)/T × (U1 − U0).
            RenderFrame apron = ArtFrames.Of(ArtFrames.Box(VisualId.Apron, ColourRole.Apron, -10f, 20f, 150f, 140f));
            Assert.Equal(G + 12, t.Fill(apron, roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { -10, 20, 0, 20, 0, 64, -10, 64 }, ArtGeometry.Uvs(933, 3238, 1008, 3568), "apron tile (−1,0)");
            ArtGeometry.AssertQuad(t, G + 1, new double[] { 0, 20, 64, 20, 64, 64, 0, 64 }, ArtGeometry.Uvs(528, 3238, 1008, 3568), "apron tile (0,0)");
            ArtGeometry.AssertQuad(t, G + 11, new double[] { 128, 128, 150, 128, 150, 140, 128, 140 }, ArtGeometry.Uvs(528, 3088, 693, 3178), "apron tile (2,2)");
            ArtGeometry.AssertMatches(t, ArtRef.Fill(apron, roles, false), "tiled apron");

            // A tiled axis-aligned segment (TaxiwaySurface, Asphalt tile 32), A = (0,0),
            // B = (0,70), Size 40: across −20..12, 12..20; along 0..32, 32..64, 64..70.
            RenderFrame taxi = ArtFrames.Of(ArtFrames.Segment(VisualId.TaxiwaySurface, ColourRole.Taxiway, 0f, 0f, 0f, 70f, 40f));
            Assert.Equal(G + 6, t.Fill(taxi, roles, false));
            ArtGeometry.AssertQuad(t, G, new double[] { -20, 0, 12, 0, 12, 32, -20, 32 }, ArtGeometry.Uvs(16, 3088, 496, 3568), "taxiway tile (0,0)");
            ArtGeometry.AssertQuad(t, G + 1, new double[] { 12, 0, 20, 0, 20, 32, 12, 32 }, ArtGeometry.Uvs(16, 3088, 136, 3568), "taxiway tile (0,1), partial across");
            ArtGeometry.AssertQuad(t, G + 4, new double[] { -20, 64, 12, 64, 12, 70, -20, 70 }, ArtGeometry.Uvs(16, 3088, 496, 3178), "taxiway tile (2,0), partial along");
            ArtGeometry.AssertQuad(t, G + 5, new double[] { 12, 64, 20, 64, 20, 70, 12, 70 }, ArtGeometry.Uvs(16, 3088, 136, 3178), "taxiway tile (2,1), partial both");
            ArtGeometry.AssertMatches(t, ArtRef.Fill(taxi, roles, false), "tiled taxiway");

            // Pointing −X, the tiles follow the segment's frame.
            RenderFrame west = ArtFrames.Of(ArtFrames.Segment(VisualId.TaxiwaySurface, ColourRole.Taxiway, 100f, 0f, 30f, 0f, 8f));
            Assert.Equal(G + 3, t.Fill(west, roles, false));
            ArtGeometry.AssertCorners(t, G, new double[] { 100, -4, 100, 4, 68, 4, 68, -4 }, "westward taxiway tile (0,0)");
            ArtGeometry.AssertMatches(t, ArtRef.Fill(west, roles, false), "westward taxiway");

            // A zero-length tiled segment, or one of Size 0, emits no tile.
            Assert.Equal(G, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.TaxiwaySurface, ColourRole.Taxiway, 5f, 5f, 5f, 5f, 40f)), roles, false));
            Assert.Equal(G, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.TaxiwaySurface, ColourRole.Taxiway, 0f, 0f, 0f, 70f, 0f)), roles, false));

            // RunwaySurface: the tiled asphalt, then the two rubber layers on their
            // segment sub-squares, design (x, y) → A + r (x/1024 − 0.5) Size + f (y/1024) len.
            RenderFrame runway = ArtFrames.Of(ArtFrames.Segment(VisualId.RunwaySurface, ColourRole.Runway, 0f, 0f, 0f, 320f, 64f));
            Assert.Equal(G + 22, t.Fill(runway, roles, false));
            float[] rubber = ArtGeometry.Uvs(516, 3716, 636, 3836);
            ArtGeometry.AssertQuad(t, G + 20, new double[] { -12, 24.0625, 12, 24.0625, 12, 95.9375, -12, 95.9375 }, rubber, "rubber, touchdown zone at A");
            ArtGeometry.AssertQuad(t, G + 21, new double[] { -12, 224.0625, 12, 224.0625, 12, 295.9375, -12, 295.9375 }, rubber, "rubber, touchdown zone at B");
            ArtGeometry.AssertColour(t, G + 20, 0x1A, 0x1A, 0x1A, 255, "rubber colour");
            ArtGeometry.AssertMatches(t, ArtRef.Fill(runway, roles, false), "runway");

            // Emission order: list order, then layers, then tiles.
            RenderFrame mixed = ArtFrames.Of(
                ArtFrames.Segment(VisualId.RunwaySurface, ColourRole.RunwayQueued, 0f, 0f, 70f, 0f, 45f),
                ArtFrames.Box(VisualId.StandPad, ColourRole.StandOccupied, -20f, -20f, 20f, 20f),
                ArtFrames.Box(VisualId.Apron, ColourRole.Apron, 0f, 0f, 64f, 64f),
                ArtFrames.Dot(VisualId.TaxiwayJunction, ColourRole.Taxiway, 0f, 0f, 8f, 0f, 0f));
            t.Fill(mixed, roles, false);
            ArtGeometry.AssertMatches(t, ArtRef.Fill(mixed, roles, false), "mixed frame");
            Assert.Equal(G + (6 + 2) + (4 + 9) + 1 + 1, t.QuadCount);
        }

        [Fact]
        public void test_art2d_tessellator_colours_follow_role_region_and_fixed()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            Assert.Equal(17, roles.Length);

            // Role: roleColours[(int)Colour], with its alpha. The ground is fixed #6F8F5E.
            var boxes = new List<DrawPrimitive>();
            for (int r = 0; r < roles.Length; r++)
            {
                boxes.Add(ArtFrames.Box(VisualId.QueueFill, (ColourRole)r, 0f, 0f, 1f, 1f));
            }

            Assert.Equal(G + roles.Length, t.Fill(ArtFrames.Of(boxes.ToArray()), roles, false));
            ArtGeometry.AssertColour(t, 0, 0x6F, 0x8F, 0x5E, 255, "ground");
            for (int r = 0; r < roles.Length; r++)
            {
                Rgba c = roles[r];
                ArtGeometry.AssertColour(t, G + r, c.R, c.G, c.B, c.A, "role " + (ColourRole)r);
            }

            // Region: Paint.Region_R with alpha 255. Passenger: Outline role;
            // Bottom 1; Bag 4; Top 0; Skin 2; Hair 3.
            Paint paint = ArtFrames.Paint(0);
            Assert.Equal(G + 6, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 0f, 0f, 1f, 0f, 0f, paint)), roles, false));
            Rgba agent = roles[(int)ColourRole.Agent];
            ArtGeometry.AssertColour(t, G, agent.R, agent.G, agent.B, agent.A, "passenger outline");
            ArtGeometry.AssertColour(t, G + 1, 4, 5, 6, 255, "passenger bottom (region 1)");
            ArtGeometry.AssertColour(t, G + 2, 13, 14, 15, 255, "passenger bag (region 4)");
            ArtGeometry.AssertColour(t, G + 3, 1, 2, 3, 255, "passenger top (region 0)");
            ArtGeometry.AssertColour(t, G + 4, 7, 8, 9, 255, "passenger skin (region 2)");
            ArtGeometry.AssertColour(t, G + 5, 10, 11, 12, 255, "passenger hair (region 3)");

            // Aircraft with a mark: shadow fixed black, status role, wings fixed
            // #D5D8DC, engines 3, fuselage 0, cheatline 2, tail 1, logo 4, glazing fixed #2A3138.
            Paint marked = ArtFrames.Paint((byte)LogoMark.Star);
            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, marked)), roles, false));
            Rgba holding = roles[(int)ColourRole.AircraftHolding];
            ArtGeometry.AssertColour(t, G, 0, 0, 0, 255, "aircraft shadow (fixed black)");
            ArtGeometry.AssertColour(t, G + 1, holding.R, holding.G, holding.B, holding.A, "aircraft status");
            ArtGeometry.AssertColour(t, G + 2, 0xD5, 0xD8, 0xDC, 255, "aircraft wings (fixed)");
            ArtGeometry.AssertColour(t, G + 3, 10, 11, 12, 255, "aircraft engines (region 3)");
            ArtGeometry.AssertColour(t, G + 4, 1, 2, 3, 255, "aircraft fuselage (region 0)");
            ArtGeometry.AssertColour(t, G + 5, 7, 8, 9, 255, "aircraft cheatline (region 2)");
            ArtGeometry.AssertColour(t, G + 6, 4, 5, 6, 255, "aircraft tail (region 1)");
            ArtGeometry.AssertColour(t, G + 7, 13, 14, 15, 255, "aircraft logo (region 4)");
            ArtGeometry.AssertUvs(t, G + 7, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Star)), "aircraft logo cell");
            ArtGeometry.AssertColour(t, G + 8, 0x2A, 0x31, 0x38, 255, "aircraft glazing (fixed)");

            // The logo is emitted only with a mark.
            Assert.Equal(G + 8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, ArtFrames.Paint(0))), roles, false));
            ArtGeometry.AssertColour(t, G + 7, 0x2A, 0x31, 0x38, 255, "glazing follows the tail when there is no mark");
            ArtGeometry.AssertUvs(t, G + 7, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(1, 7))), "glazing cell when there is no mark");

            // The other fixed layers: the lead-in's equipment and the stand's red edge line.
            Assert.Equal(G + 3, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.StandLeadIn, ColourRole.TaxiwayMarking, 0f, 0f, 40f, 0f, 1f)), roles, false));
            ArtGeometry.AssertColour(t, G + 1, 0xE0, 0xB1, 0x2A, 255, "ground equipment body");
            ArtGeometry.AssertColour(t, G + 2, 0x30, 0x35, 0x3B, 255, "ground equipment detail");
            Assert.Equal(G + 1 + 9, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.StandPad, ColourRole.StandFree, 0f, 0f, 40f, 40f)), roles, false));
            ArtGeometry.AssertColour(t, G + 1, 0xC2, 0x3B, 0x30, 255, "stand edge line");

            // The role alpha is kept: QueueFill at alpha 140 (the palette's), also when linear.
            Rgba[] palette = ArtFrames.Roles();
            palette[(int)ColourRole.QueueFill] = new Rgba(0xE8, 0xA3, 0x3A, 140);
            RenderFrame fill = ArtFrames.Of(ArtFrames.Box(VisualId.QueueFill, ColourRole.QueueFill, 0f, 0f, 5f, 1f));
            t.Fill(fill, palette, false);
            ArtGeometry.AssertColour(t, G, 0xE8, 0xA3, 0x3A, 140, "queue fill, sRGB");
            t.Fill(fill, palette, true);
            ArtGeometry.AssertColour(t, G, SrgbTable.L(0xE8), SrgbTable.L(0xA3), SrgbTable.L(0x3A), 140, "queue fill, linear: alpha unchanged");

            // linear: L is §15.17's literal table: L(0) = 0, L(128) = 55, L(255) = 255.
            Assert.Equal(256, SrgbTable.Count);
            Assert.Equal(0, SrgbTable.L(0));
            Assert.Equal(55, SrgbTable.L(128));
            Assert.Equal(255, SrgbTable.L(255));
            var only = new Rgba[roles.Length];
            for (int r = 0; r < only.Length; r++)
            {
                only[r] = new Rgba(128, 0, 255, 77);
            }

            t.Fill(fill, only, true);
            ArtGeometry.AssertColour(t, G, 55, 0, 255, 77, "L(128) = 55, L(0) = 0, L(255) = 255");

            // Every entry of the table, through role, region and fixed colours.
            for (int c = 0; c < 256; c++)
            {
                var sweep = new Rgba[roles.Length];
                for (int r = 0; r < sweep.Length; r++)
                {
                    sweep[r] = new Rgba((byte)c, (byte)(255 - c), (byte)((7 * c) % 256), (byte)(255 - (c / 2)));
                }

                t.Fill(fill, sweep, true);
                ArtGeometry.AssertColour(t, G, SrgbTable.L(c), SrgbTable.L(255 - c), SrgbTable.L((7 * c) % 256), 255 - (c / 2), "linear role, c = " + c);
                t.Fill(fill, sweep, false);
                ArtGeometry.AssertColour(t, G, c, 255 - c, (7 * c) % 256, 255 - (c / 2), "sRGB role, c = " + c);
                var rgb = new Rgb((byte)c, (byte)(255 - c), (byte)((7 * c) % 256));
                t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 0f, 0f, 1f, 0f, 0f, new Paint(rgb, rgb, rgb, rgb, rgb, 0))), roles, true);
                ArtGeometry.AssertColour(t, G + 3, SrgbTable.L(c), SrgbTable.L(255 - c), SrgbTable.L((7 * c) % 256), 255, "linear region, c = " + c);
            }

            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, marked)), roles, true));
            ArtGeometry.AssertColour(t, G + 2, SrgbTable.L(0xD5), SrgbTable.L(0xD8), SrgbTable.L(0xDC), 255, "linear wings");
            ArtGeometry.AssertColour(t, G + 8, SrgbTable.L(0x2A), SrgbTable.L(0x31), SrgbTable.L(0x38), 255, "linear glazing");

            // Every visual drawn as a Dot (no tiled or sliced layer), role and mark,
            // in both colour spaces, against the spec's rules; the Box visuals' colours
            // are in the corners test's box frames.
            foreach (VisualId v in ArtTable.AllVisuals())
            {
                bool boxOnly = false;
                foreach (ArtLayer l in ArtTable.Layers(v))
                {
                    boxOnly |= l.Tile > 0 || l.SliceInset > 0;
                }

                if (boxOnly)
                {
                    continue;
                }

                foreach (byte mark in new byte[] { 0, (byte)LogoMark.Bars })
                {
                    for (int r = 0; r < roles.Length; r += 4)
                    {
                        foreach (bool linear in new[] { false, true })
                        {
                            RenderFrame f = ArtFrames.Of(ArtFrames.Dot(v, (ColourRole)r, 0f, 0f, 30f, 0f, 1f, ArtFrames.Paint(mark)));
                            t.Fill(f, roles, linear);
                            ArtGeometry.AssertMatches(t, ArtRef.Fill(f, roles, linear), v + " role " + (ColourRole)r + " mark " + mark + (linear ? " linear" : string.Empty));
                        }
                    }
                }
            }

            // roleColours must have exactly one entry per ColourRole: fewer or more
            // throw ArgumentException, null throws ArgumentNullException, each with
            // ParamName roleColours, before any buffer changes.
            RenderFrame before = ArtFrames.Of(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 3f, 4f, 2f, 0f, 1f, paint));
            int count = t.Fill(before, roles, false);
            float[] corners = Prefix(t.Corners, 8 * count);
            float[] uvs = Prefix(t.Uvs, 8 * count);
            byte[] colours = Prefix(t.Colours, 16 * count);
            RenderFrame other = ArtFrames.Of(ArtFrames.Box(VisualId.Apron, ColourRole.Apron, 0f, 0f, 500f, 500f));
            foreach (int n in new[] { roles.Length - 1, 0, roles.Length + 1 })
            {
                var wrong = new Rgba[n];
                ArgumentException e = Assert.ThrowsAny<ArgumentException>(() => t.Fill(other, wrong, false));
                Assert.IsNotType<ArgumentNullException>(e);
                Assert.True(e.ParamName == "roleColours", "roleColours of " + n + " entries: ParamName " + (e.ParamName ?? "null"));
                AssertUnchanged(t, count, corners, uvs, colours, n + " entries");
            }

            ArgumentNullException nul = Assert.Throws<ArgumentNullException>(() => t.Fill(other, null!, false));
            Assert.Equal("roleColours", nul.ParamName);
            AssertUnchanged(t, count, corners, uvs, colours, "null");
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
                VisualId v = ArtTable.AllVisuals()[k % ArtTable.AllVisuals().Length];
                prims.Add(ArtFrames.Dot(v, (ColourRole)(k % 17), k, -k, 2f, k % 3, (k % 5) - 2, new Paint(rgb, rgb, rgb, rgb, rgb, (byte)(k % 8))));
            }

            RenderFrame big = ArtFrames.Of(prims.ToArray());
            int n = ArtRef.Fill(big, roles, false).Count;
            Assert.True(n > prims.Count, "the big frame has " + n + " quads");
            RenderFrame small = ArtFrames.Of(prims[0]);
            int smallCount = ArtRef.Fill(small, roles, false).Count;
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

            // An empty frame still draws its ground.
            Assert.Equal(G, t.Fill(empty, roles, false));
            Assert.Equal(G, t.QuadCount);

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

        private static void AssertDisc(ISpriteTessellator t, Rgba[] roles, float fx, float fy, double[] corners, float[] uvs)
        {
            string what = "junction dot facing (" + fx + "," + fy + ")";
            Assert.Equal(G + 1, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.TaxiwayJunction, ColourRole.Taxiway, 1000f, -2000f, 20f, fx, fy)), roles, false));
            ArtGeometry.AssertQuad(t, G, corners, uvs, what);
            ArtGeometry.AssertCorners(t, G, ArtGeometry.Dot(1000, -2000, 20, fx, fy), what + ", reference");
        }

        private static void AssertUnchanged(ISpriteTessellator t, int count, float[] corners, float[] uvs, byte[] colours, string what)
        {
            Assert.True(t.QuadCount == count, "roleColours " + what + ": QuadCount changed to " + t.QuadCount + " from " + count);
            Assert.True(t.Corners.Length >= corners.Length && t.Uvs.Length >= uvs.Length && t.Colours.Length >= colours.Length, "roleColours " + what + ": a buffer shrank");
            Assert.Equal(corners, Prefix(t.Corners, corners.Length));
            Assert.Equal(uvs, Prefix(t.Uvs, uvs.Length));
            Assert.Equal(colours, Prefix(t.Colours, colours.Length));
        }

        private static T[] Prefix<T>(T[] a, int n)
        {
            var p = new T[n];
            Array.Copy(a, p, n);
            return p;
        }
    }
}
