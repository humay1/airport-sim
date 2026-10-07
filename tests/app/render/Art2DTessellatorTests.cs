using System;
using System.Collections.Generic;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 "Tessellation" (Q-130). Corners are compared within §15.3's one
    /// tolerance (1e-3 world units) using §15.17's chosen vectors; Uvs and
    /// colours exactly.
    /// </summary>
    public sealed class Art2DTessellatorTests
    {
        [Fact]
        public void test_art2d_tessellator_corners_follow_kind_facing_and_slicing()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            float[] solid = ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Small("Solid")));

            // Box: (MinX,MinY), (MaxX,MinY), (MaxX,MaxY), (MinX,MaxY), never rotated; Apron is not sliced.
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.Apron, ColourRole.Apron, 10f, 20f, 110f, 70f)), roles, false));
            Assert.Equal(1, t.QuadCount);
            ArtGeometry.AssertQuad(t, 0, new double[] { 10, 20, 110, 20, 110, 70, 10, 70 }, solid, "apron box");

            // ControlTower is not sliced either.
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.ControlTower, ColourRole.Building, 920f, 200f, 940f, 220f)), roles, false));
            ArtGeometry.AssertQuad(t, 0, ArtGeometry.Box(920, 200, 940, 220), ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Small("ControlTower"))), "control tower box");

            // Segment: f = (B − A)/|B − A|, r = (f.Y, −f.X), h = Size/2:
            // A − r h, A + r h, B + r h, B − r h.
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.RunwaySurface, ColourRole.Runway, 100f, 200f, 400f, 600f, 45f)), roles, false));
            ArtGeometry.AssertQuad(t, 0, new double[] { 82, 213.5, 118, 186.5, 418, 586.5, 382, 613.5 }, solid, "diagonal segment");
            ArtGeometry.AssertCorners(t, 0, ArtGeometry.Segment(100, 200, 400, 600, 45), "diagonal segment, reference");

            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.RunwayEdgeLines, ColourRole.RunwayMarking, -2000f, 0f, 0f, 0f, 45f)), roles, false));
            ArtGeometry.AssertQuad(t, 0, new double[] { -2000, 22.5, -2000, -22.5, 0, -22.5, 0, 22.5 }, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Small("RunwayEdgeLines"))), "axis segment");

            // A zero-length segment uses f = (0, 1).
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Segment(VisualId.TaxiwayCentreline, ColourRole.TaxiwayMarking, 5f, 5f, 5f, 5f, 10f)), roles, false));
            ArtGeometry.AssertQuad(t, 0, new double[] { 0, 5, 10, 5, 10, 5, 0, 5 }, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Small("CentreStripe"))), "zero-length segment");

            // Dot: f is Facing normalised, or (0, 1) for (0, 0); r = (f.Y, −f.X):
            // centre − r h − f h, centre + r h − f h, centre + r h + f h, centre − r h + f h.
            float[] disc = ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Small("Disc")));
            AssertDot(t, roles, 0f, 0f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }, disc);
            AssertDot(t, roles, 0f, 1f, new double[] { 990, -2010, 1010, -2010, 1010, -1990, 990, -1990 }, disc);
            AssertDot(t, roles, 1f, 0f, new double[] { 990, -1990, 990, -2010, 1010, -2010, 1010, -1990 }, disc);
            AssertDot(t, roles, -1f, 0f, new double[] { 1010, -2010, 1010, -1990, 990, -1990, 990, -2010 }, disc);
            AssertDot(t, roles, 0f, -1f, new double[] { 1010, -1990, 990, -1990, 990, -2010, 1010, -2010 }, disc);
            AssertDot(t, roles, 3f, 4f, new double[] { 986, -2002, 1002, -2014, 1014, -1998, 998, -1986 }, disc);
            AssertDot(t, roles, 1f, 1f, ArtGeometry.Dot(1000, -2000, 20, 1, 1), disc);

            // Only the direction matters (15 §15.16): a long integer vector is the same as its unit.
            AssertDot(t, roles, 2000f, 0f, new double[] { 990, -1990, 990, -2010, 1010, -2010, 1010, -1990 }, disc);
            AssertDot(t, roles, 0f, -7f, new double[] { 1010, -1990, 990, -1990, 990, -2010, 1010, -2010 }, disc);
            AssertDot(t, roles, -300f, -400f, ArtGeometry.Dot(1000, -2000, 20, -3, -4), disc);

            // Large size and far centre, within §15.17's tolerance note (Size ≤ 1000, centres within ± 10 000).
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.TaxiwayJunction, ColourRole.Taxiway, 9000f, -9000f, 1000f, 1f, 1f)), roles, false));
            ArtGeometry.AssertQuad(t, 0, ArtGeometry.Dot(9000, -9000, 1000, 1, 1), disc, "dot size 1000 facing (1,1)");

            // The logo sub-square: design (x, y) maps to
            // centre + r × (x/1024 − 0.5) × Size + f × (y/1024 − 0.5) × Size.
            // AircraftC (447,250)–(578,381) at Size 512 is (−32.5,−131)–(33,−65.5) unrotated.
            var paint = ArtFrames.Paint((byte)LogoMark.Disc);
            Assert.Equal(8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 0f, 0f, paint)), roles, false));
            for (int l = 0; l < 6; l++)
            {
                ArtGeometry.AssertQuad(t, l, new double[] { -256, -256, 256, -256, 256, 256, -256, 256 }, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, l))), "AircraftC layer " + l);
            }

            ArtGeometry.AssertQuad(t, 6, new double[] { -32.5, -131, 33, -131, 33, -65.5, -32.5, -65.5 }, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Disc)), "AircraftC logo");
            ArtGeometry.AssertQuad(t, 7, new double[] { -256, -256, 256, -256, 256, 256, -256, 256 }, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, 6))), "AircraftC glazing");

            // Rotated: facing (1, 0) takes design (447, 250) to (−131, 32.5).
            Assert.Equal(8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 1f, 0f, paint)), roles, false));
            ArtGeometry.AssertCorners(t, 6, new double[] { -131, 32.5, -131, -33, -65.5, -33, -65.5, 32.5 }, "AircraftC logo facing (1,0)");
            ArtGeometry.AssertCorners(t, 6, ArtGeometry.Dot(0, 0, 512, 1, 0, 447, 250, 578, 381), "AircraftC logo facing (1,0), reference");

            // Facing (3, 4) and a far centre, every layer of AircraftF.
            var paintF = ArtFrames.Paint((byte)LogoMark.Crescent);
            Assert.Equal(8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftF, ColourRole.AircraftOnStand, -4000f, 7000f, 60f, 3f, 4f, paintF)), roles, false));
            for (int q = 0; q < 8; q++)
            {
                double[] corners = q == 6 ? ArtGeometry.Dot(-4000, 7000, 60, 3, 4, 416, 127, 609, 320) : ArtGeometry.Dot(-4000, 7000, 60, 3, 4);
                ArtGeometry.AssertCorners(t, q, corners, "AircraftF facing (3,4) quad " + q);
            }

            ArtGeometry.AssertUvs(t, 6, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Crescent)), "AircraftF logo");

            // Sliced boxes: nine quads, bottom row first, left to right. The world
            // grid is at t = min(sliceWorld, width/2, height/2) from each side, the
            // rect grid at 128/1024 of the rect.
            var sliced = ArtFrames.Of(
                ArtFrames.Box(VisualId.TerminalBuilding, ColourRole.Building, 0f, 0f, 30f, 20f),
                ArtFrames.Box(VisualId.Pier, ColourRole.Building, 0f, 0f, 100f, 4f),
                ArtFrames.Box(VisualId.StandPad, ColourRole.StandFree, -20f, -20f, 20f, 20f),
                ArtFrames.Box(VisualId.TerminalZone, ColourRole.LandsideNode, 0f, 0f, 10f, 1f));
            Assert.Equal(36, t.Fill(sliced, roles, false));
            Assert.Equal(36, t.QuadCount);

            // The terminal (3 m slice) spelled out: corner, centre and opposite corner.
            ArtGeometry.AssertQuad(t, 0, ArtGeometry.Box(0, 0, 3, 3), ArtGeometry.Uvs(644f / 2048f, 1540f / 2048f, 659f / 2048f, 1555f / 2048f), "terminal slice 0");
            ArtGeometry.AssertQuad(t, 4, ArtGeometry.Box(3, 3, 27, 17), ArtGeometry.Uvs(659f / 2048f, 1555f / 2048f, 749f / 2048f, 1645f / 2048f), "terminal slice 4");
            ArtGeometry.AssertQuad(t, 8, ArtGeometry.Box(27, 17, 30, 20), ArtGeometry.Uvs(749f / 2048f, 1645f / 2048f, 764f / 2048f, 1660f / 2048f), "terminal slice 8");

            AssertSliced(t, 0, 0, 0, 30, 20, 3.0, "BuildingRoof", "terminal");
            AssertSliced(t, 9, 0, 0, 100, 4, 2.0, "BuildingRoof", "thin pier, t clamped to 2");
            AssertSliced(t, 18, -20, -20, 20, 20, 2.0, "StandPad", "stand pad");
            AssertSliced(t, 27, 0, 0, 10, 1, 0.5, "TerminalZone", "thin zone, t clamped to 0.5");

            // List order, then layer order; every visual's UVs follow the table.
            var all = new List<DrawPrimitive>();
            foreach (VisualId v in ArtTable.AllVisuals())
            {
                all.Add(ArtFrames.Dot(v, ColourRole.Agent, 0f, 0f, 10f, 0f, 0f, ArtFrames.Paint((byte)LogoMark.Ring)));
            }

            int expected = 0;
            foreach (DrawPrimitive p in all)
            {
                expected += ArtTable.QuadsOf(p);
            }

            Assert.Equal(expected, t.Fill(ArtFrames.Of(all.ToArray()), roles, false));
            int q2 = 0;
            foreach (DrawPrimitive p in all)
            {
                foreach (ArtLayer l in ArtTable.Layers(p.Visual))
                {
                    AtlasRect r = l.IsLogo ? Art2DFactory.LogoRect(LogoMark.Ring) : l.Rect;
                    ArtGeometry.AssertQuad(t, q2, ArtGeometry.Dot(0, 0, 10, 0, 0, l.MinX, l.MinY, l.MaxX, l.MaxY), ArtGeometry.Uvs(r), p.Visual + " quad " + q2);
                    q2++;
                }
            }

            Assert.Equal(expected, q2);
        }

        [Fact]
        public void test_art2d_tessellator_colours_follow_role_region_and_fixed()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            Assert.Equal(17, roles.Length);

            // Role: roleColours[(int)Colour], with its alpha.
            var boxes = new List<DrawPrimitive>();
            for (int r = 0; r < roles.Length; r++)
            {
                boxes.Add(ArtFrames.Box(VisualId.Apron, (ColourRole)r, 0f, 0f, 1f, 1f));
            }

            Assert.Equal(roles.Length, t.Fill(ArtFrames.Of(boxes.ToArray()), roles, false));
            for (int r = 0; r < roles.Length; r++)
            {
                Rgba c = roles[r];
                ArtGeometry.AssertColour(t, r, c.R, c.G, c.B, c.A, "role " + (ColourRole)r);
            }

            // Region: Paint.Region_R with alpha 255. Passenger: Outline role;
            // Bottom 1; Bag 4; Top 0; Skin 2; Hair 3.
            Paint paint = ArtFrames.Paint(0);
            Assert.Equal(6, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 0f, 0f, 1f, 0f, 0f, paint)), roles, false));
            Rgba agent = roles[(int)ColourRole.Agent];
            ArtGeometry.AssertColour(t, 0, agent.R, agent.G, agent.B, agent.A, "passenger outline");
            ArtGeometry.AssertColour(t, 1, 4, 5, 6, 255, "passenger bottom (region 1)");
            ArtGeometry.AssertColour(t, 2, 13, 14, 15, 255, "passenger bag (region 4)");
            ArtGeometry.AssertColour(t, 3, 1, 2, 3, 255, "passenger top (region 0)");
            ArtGeometry.AssertColour(t, 4, 7, 8, 9, 255, "passenger skin (region 2)");
            ArtGeometry.AssertColour(t, 5, 10, 11, 12, 255, "passenger hair (region 3)");

            // Aircraft with a mark: status role, wings fixed #D5D8DC, engines 3,
            // fuselage 0, cheatline 2, tail 1, the logo region 4, glazing fixed #2A3138.
            Paint marked = ArtFrames.Paint((byte)LogoMark.Star);
            Assert.Equal(8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, marked)), roles, false));
            Rgba holding = roles[(int)ColourRole.AircraftHolding];
            ArtGeometry.AssertColour(t, 0, holding.R, holding.G, holding.B, holding.A, "aircraft status");
            ArtGeometry.AssertColour(t, 1, 0xD5, 0xD8, 0xDC, 255, "aircraft wings (fixed)");
            ArtGeometry.AssertColour(t, 2, 10, 11, 12, 255, "aircraft engines (region 3)");
            ArtGeometry.AssertColour(t, 3, 1, 2, 3, 255, "aircraft fuselage (region 0)");
            ArtGeometry.AssertColour(t, 4, 7, 8, 9, 255, "aircraft cheatline (region 2)");
            ArtGeometry.AssertColour(t, 5, 4, 5, 6, 255, "aircraft tail (region 1)");
            ArtGeometry.AssertColour(t, 6, 13, 14, 15, 255, "aircraft logo (region 4)");
            ArtGeometry.AssertUvs(t, 6, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Star)), "aircraft logo cell");
            ArtGeometry.AssertColour(t, 7, 0x2A, 0x31, 0x38, 255, "aircraft glazing (fixed)");

            // The logo is emitted only with a mark.
            Assert.Equal(7, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, ArtFrames.Paint(0))), roles, false));
            Assert.Equal(7, t.QuadCount);
            ArtGeometry.AssertColour(t, 6, 0x2A, 0x31, 0x38, 255, "glazing follows the tail when there is no mark");
            ArtGeometry.AssertUvs(t, 6, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(1, 6))), "glazing cell when there is no mark");

            // The role alpha is kept: QueueFill at alpha 140 (the palette's).
            Rgba[] palette = ArtFrames.Roles();
            palette[(int)ColourRole.QueueFill] = new Rgba(0xE8, 0xA3, 0x3A, 140);
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.QueueFill, ColourRole.QueueFill, 0f, 0f, 5f, 1f)), palette, false));
            ArtGeometry.AssertColour(t, 0, 0xE8, 0xA3, 0x3A, 140, "queue fill, sRGB");
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Box(VisualId.QueueFill, ColourRole.QueueFill, 0f, 0f, 5f, 1f)), palette, true));
            ArtGeometry.AssertColour(t, 0, ArtGeometry.Linear(0xE8), ArtGeometry.Linear(0xA3), ArtGeometry.Linear(0x3A), 140, "queue fill, linear: alpha unchanged");

            // linear: L(0) = 0, L(128) = 55, L(255) = 255, and every value by the table.
            Assert.Equal(0, ArtGeometry.Linear(0));
            Assert.Equal(55, ArtGeometry.Linear(128));
            Assert.Equal(255, ArtGeometry.Linear(255));
            var pax = new List<DrawPrimitive>();
            for (int c = 0; c < 256; c++)
            {
                var rgb = new Rgb((byte)c, (byte)(255 - c), (byte)((7 * c) % 256));
                pax.Add(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 0f, 0f, 1f, 0f, 0f, new Paint(rgb, rgb, rgb, rgb, rgb, 0)));
            }

            Assert.Equal(6 * 256, t.Fill(ArtFrames.Of(pax.ToArray()), roles, true));
            for (int c = 0; c < 256; c++)
            {
                ArtGeometry.AssertColour(t, (6 * c) + 3, ArtGeometry.Linear(c), ArtGeometry.Linear(255 - c), ArtGeometry.Linear((7 * c) % 256), 255, "linear top, c = " + c);
            }

            ArtGeometry.AssertColour(t, (6 * 128) + 3, 55, ArtGeometry.Linear(127), 55, 255, "L(128) = 55");
            Assert.Equal(6 * 256, t.Fill(ArtFrames.Of(pax.ToArray()), roles, false));
            for (int c = 0; c < 256; c++)
            {
                ArtGeometry.AssertColour(t, (6 * c) + 3, c, 255 - c, (7 * c) % 256, 255, "sRGB top, c = " + c);
            }

            // linear applies to role and fixed colours too.
            Assert.Equal(roles.Length, t.Fill(ArtFrames.Of(boxes.ToArray()), roles, true));
            for (int r = 0; r < roles.Length; r++)
            {
                Rgba c = roles[r];
                ArtGeometry.AssertColour(t, r, ArtGeometry.Linear(c.R), ArtGeometry.Linear(c.G), ArtGeometry.Linear(c.B), c.A, "linear role " + (ColourRole)r);
            }

            Assert.Equal(8, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.AircraftB, ColourRole.AircraftHolding, 0f, 0f, 30f, 0f, 1f, marked)), roles, true));
            ArtGeometry.AssertColour(t, 1, ArtGeometry.Linear(0xD5), ArtGeometry.Linear(0xD8), ArtGeometry.Linear(0xDC), 255, "linear wings");
            ArtGeometry.AssertColour(t, 7, ArtGeometry.Linear(0x2A), ArtGeometry.Linear(0x31), ArtGeometry.Linear(0x38), 255, "linear glazing");

            // roleColours must have one entry per ColourRole, else ArgumentException (roleColours).
            RenderFrame one = ArtFrames.Of(ArtFrames.Box(VisualId.Apron, ColourRole.Apron, 0f, 0f, 1f, 1f));
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
            var pax = new List<DrawPrimitive>();
            for (int k = 0; k < 50; k++)
            {
                byte b = (byte)k;
                var rgb = new Rgb(b, (byte)(2 * k), (byte)(3 * k));
                pax.Add(ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, k, -k, 2f, k % 3, (k % 5) - 2, new Paint(rgb, rgb, rgb, rgb, rgb, 0)));
            }

            RenderFrame big = ArtFrames.Of(pax.ToArray());
            RenderFrame small = ArtFrames.Of(ArtFrames.Box(VisualId.Apron, ColourRole.Apron, 0f, 0f, 1f, 1f));
            RenderFrame empty = ArtFrames.Of();

            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Assert.Equal(300, t.Fill(big, roles, false));
            Assert.Equal(300, t.QuadCount);
            float[] corners = t.Corners;
            float[] uvs = t.Uvs;
            byte[] colours = t.Colours;
            Assert.True(corners.Length >= 8 * 300 && uvs.Length >= 8 * 300 && colours.Length >= 16 * 300, "buffers hold the frame's quads");
            float[] firstCorners = Prefix(corners, 8 * 300);
            float[] firstUvs = Prefix(uvs, 8 * 300);
            byte[] firstColours = Prefix(colours, 16 * 300);

            Assert.Equal(1, t.Fill(small, roles, false));
            Assert.Equal(1, t.QuadCount);
            Assert.Same(corners, t.Corners);
            Assert.Same(uvs, t.Uvs);
            Assert.Same(colours, t.Colours);

            Assert.Equal(0, t.Fill(empty, roles, false));
            Assert.Equal(0, t.QuadCount);

            Assert.Equal(300, t.Fill(big, roles, false));
            Assert.Same(corners, t.Corners);
            Assert.Same(uvs, t.Uvs);
            Assert.Same(colours, t.Colours);
            Assert.Equal(firstCorners, Prefix(t.Corners, 8 * 300));
            Assert.Equal(firstUvs, Prefix(t.Uvs, 8 * 300));
            Assert.Equal(firstColours, Prefix(t.Colours, 16 * 300));

            // A fresh tessellator gives the same quads for the same frame.
            ISpriteTessellator u = Art2DFactory.CreateTessellator();
            Assert.Equal(300, u.Fill(big, roles, false));
            Assert.Equal(firstCorners, Prefix(u.Corners, 8 * 300));
            Assert.Equal(firstUvs, Prefix(u.Uvs, 8 * 300));
            Assert.Equal(firstColours, Prefix(u.Colours, 16 * 300));
            Assert.NotSame(t.Corners, u.Corners);
        }

        private static void AssertDot(ISpriteTessellator t, Rgba[] roles, float fx, float fy, double[] corners, float[] uvs)
        {
            string what = "dot facing (" + fx + "," + fy + ")";
            Assert.Equal(1, t.Fill(ArtFrames.Of(ArtFrames.Dot(VisualId.TaxiwayJunction, ColourRole.Taxiway, 1000f, -2000f, 20f, fx, fy)), roles, false));
            ArtGeometry.AssertQuad(t, 0, corners, uvs, what);
            ArtGeometry.AssertCorners(t, 0, ArtGeometry.Dot(1000, -2000, 20, fx, fy), what + ", reference");
        }

        private static void AssertSliced(ISpriteTessellator t, int first, double x0, double y0, double x1, double y1, double tWorld, string cell, string what)
        {
            AtlasRect r = ArtCells.RectOf(ArtCells.Small(cell));
            double[] xs = { x0, x0 + tWorld, x1 - tWorld, x1 };
            double[] ys = { y0, y0 + tWorld, y1 - tWorld, y1 };
            double du = (r.U1 - (double)r.U0) * ArtTable.SliceDesign / 1024.0;
            double dv = (r.V1 - (double)r.V0) * ArtTable.SliceDesign / 1024.0;
            float[] us = { r.U0, (float)(r.U0 + du), (float)(r.U1 - du), r.U1 };
            float[] vs = { r.V0, (float)(r.V0 + dv), (float)(r.V1 - dv), r.V1 };
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int q = first + (row * 3) + col;
                    ArtGeometry.AssertQuad(
                        t,
                        q,
                        ArtGeometry.Box(xs[col], ys[row], xs[col + 1], ys[row + 1]),
                        ArtGeometry.Uvs(us[col], vs[row], us[col + 1], vs[row + 1]),
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
