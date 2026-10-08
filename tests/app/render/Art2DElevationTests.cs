using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.22 (Q-132): Art2D draws an airborne aircraft's Elevation as scale
    /// and a longer ground shadow, and §15.23's M2 tests. Corners keep §15.17's
    /// 1e-3 tolerance and its test vectors (§15.23 "Tolerance note"); counts,
    /// Uvs and colours are compared exactly, and "unchanged" is byte for byte.
    /// </summary>
    public sealed class Art2DElevationTests
    {
        private const int G = ArtFrames.GroundQuads;

        private static readonly float[] Elevations = { 0.5f, 12.5f, 100f, 400f, 799f, 800f, 1200f, 3000f };

        private static readonly float[][] Facings =
        {
            new[] { 0f, 0f }, new[] { 1f, 0f }, new[] { 0f, -1f }, new[] { 3f, 4f }, new[] { 1f, 1f },
        };

        [Fact]
        public void test_art2d_airborne_aircraft_scaled_with_a_ground_shadow()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();
            Paint disc = ArtFrames.Paint((byte)LogoMark.Disc);
            DrawPrimitive ground = ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 0f, 0f, disc);

            // AircraftC at Size 512, written out. The shadow keeps Size 512 and its
            // row shift (0.9, −1.2) gains (0.15 e, −0.2 e); every other layer is
            // drawn at 512 × min(2, 1 + e / 800). The logo sub-square
            // (447,250)–(578,381) is (−65,−262)–(66,−131) × Size / 1024.
            AssertAircraftC(t, roles, ground, 100f, 288, new double[] { -36.5625, -147.375, 37.125, -73.6875 }, 15.9, -21.2);
            AssertAircraftC(t, roles, ground, 400f, 384, new double[] { -48.75, -196.5, 49.5, -98.25 }, 60.9, -81.2);
            AssertAircraftC(t, roles, ground, 800f, 512, new double[] { -65, -262, 66, -131 }, 120.9, -161.2);

            // Above 800 m the scale stays at ELEVATION_SCALE_MAX; the shadow's shift keeps growing.
            AssertAircraftC(t, roles, ground, 1600f, 512, new double[] { -65, -262, 66, -131 }, 240.9, -321.2);

            // Rotated: the scaled layers turn with the facing, the shadow's shift does not.
            DrawPrimitive east = ArtFrames.Lifted(ArtFrames.Dot(VisualId.AircraftC, ColourRole.AircraftMoving, 0f, 0f, 512f, 1f, 0f, disc), 400f);
            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(east), roles, false));
            ArtGeometry.AssertCorners(t, G, ArtGeometry.Shifted(ArtGeometry.Dot(0, 0, 512, 1, 0), 60.9, -81.2), "e 400 facing (1,0): shadow at true size, shift not rotated");
            ArtGeometry.AssertCorners(t, G + 1, ArtGeometry.Dot(0, 0, 768, 1, 0), "e 400 facing (1,0): status");
            ArtGeometry.AssertCorners(t, G + 7, new double[] { -196.5, 48.75, -196.5, -49.5, -98.25, -49.5, -98.25, 48.75 }, "e 400 facing (1,0): logo");

            // Every row, facing, mark and colour space against the reference: the
            // same quads in the same order as at e = 0, with only corners moved.
            var centres = new[] { new[] { 0f, 0f }, new[] { 1234f, -5678f }, new[] { -9000f, 9000f } };
            var sizes = new[] { 30f, 512f, 1000f };
            int n = 0;
            for (int s = 0; s < 6; s++)
            {
                VisualId v = VisualId.AircraftA + s;
                for (int fi = 0; fi < Facings.Length; fi++)
                {
                    float[] c = centres[(s + fi) % centres.Length];
                    float size = sizes[(s + (2 * fi)) % sizes.Length];
                    Paint paint = ArtFrames.Paint((byte)((s + fi) % 2 == 0 ? (byte)LogoMark.Disc : 0));
                    DrawPrimitive p = ArtFrames.Dot(v, (ColourRole)((s + fi) % ArtFrames.RoleCount), c[0], c[1], size, Facings[fi][0], Facings[fi][1], paint);
                    foreach (float e in Elevations)
                    {
                        bool linear = (n++ % 2) == 1;
                        string what = string.Format(CultureInfo.InvariantCulture, "{0} size {1} facing ({2},{3}) e {4} linear {5}", v, size, Facings[fi][0], Facings[fi][1], e, linear);
                        AssertLiftedAgainstGround(t, roles, p, e, linear, what);
                    }
                }
            }

            // Several aircraft at different heights in one frame, among ground
            // primitives: each is drawn by its own Elevation, and no quad is added.
            var prims = new List<DrawPrimitive>
            {
                ArtFrames.Box(VisualId.TerminalBuilding, ColourRole.Building, 100f, 100f, 400f, 250f),
                ArtFrames.Lifted(ArtFrames.Dot(VisualId.AircraftF, ColourRole.AircraftMoving, 500f, -500f, 60f, 0f, 1f, disc), 400f),
                ArtFrames.Dot(VisualId.AircraftF, ColourRole.AircraftOnStand, 500f, -500f, 60f, 0f, 1f, disc),
                ArtFrames.Segment(VisualId.JetBridge, ColourRole.Building, 100f, 200f, 400f, 600f, 4f),
                ArtFrames.Lifted(ArtFrames.Dot(VisualId.AircraftA, ColourRole.AircraftHolding, -300f, 700f, 20f, -1f, 0f, ArtFrames.Paint(0)), 100f),
                ArtFrames.Lifted(ArtFrames.Dot(VisualId.AircraftD, ColourRole.AircraftMoving, 2000f, 2000f, 45f, 3f, 4f, disc), 2400f),
                ArtFrames.Dot(VisualId.Passenger, ColourRole.Agent, 10f, 10f, 1f, 0f, 1f, disc),
            };
            RenderFrame mixed = ArtFrames.Of(prims.ToArray());
            var flat = new List<DrawPrimitive>();
            foreach (DrawPrimitive p in prims)
            {
                flat.Add(ArtFrames.Lifted(p, 0f));
            }

            int flatCount = ArtRef.Fill(ArtFrames.Of(flat.ToArray()), roles, false).Count;
            Assert.Equal(flatCount, t.Fill(mixed, roles, false));
            ArtGeometry.AssertMatches(t, ArtRef.Fill(mixed, roles, false), "mixed frame");
        }

        [Fact]
        public void test_art2d_elevation_zero_or_non_aircraft_is_unchanged()
        {
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            Rgba[] roles = ArtFrames.Roles();

            // e = 0 through the eleven-field constructor is the kept constructor's
            // primitive, byte for byte, and §15.17's output.
            for (int s = 0; s < 6; s++)
            {
                for (int fi = 0; fi < Facings.Length; fi++)
                {
                    foreach (byte mark in new byte[] { 0, (byte)LogoMark.Disc })
                    {
                        DrawPrimitive kept = ArtFrames.Dot(VisualId.AircraftA + s, ColourRole.AircraftMoving, 1234f, -5678f, 512f, Facings[fi][0], Facings[fi][1], ArtFrames.Paint(mark));
                        string what = string.Format(CultureInfo.InvariantCulture, "{0} facing ({1},{2}) mark {3}", kept.Visual, Facings[fi][0], Facings[fi][1], mark);
                        AssertSameBytes(t, roles, ArtFrames.Of(kept), ArtFrames.Of(ArtFrames.Lifted(kept, 0f)), what + " at e 0");
                        Assert.Equal(G + (mark == 0 ? 8 : 9), t.QuadCount);
                        ArtGeometry.AssertMatches(t, ArtRef.Fill(ArtFrames.Of(kept), roles, true), what + " at e 0, against 15 §15.17");
                    }
                }
            }

            // Every other visual, whatever its Elevation: byte for byte its e = 0
            // output. Box for every visual; Segment for those without a sliced
            // layer; Dot for those with neither a tiled nor a sliced layer (the
            // kinds 15 §15.17 defines). JetBridge and the buildings carry shifted
            // shadows of their own, which elevation must not touch.
            Paint paint = ArtFrames.Paint((byte)LogoMark.Disc);
            int checkedVisuals = 0;
            foreach (VisualId v in ArtTable.AllVisuals())
            {
                if (v >= VisualId.AircraftA && v <= VisualId.AircraftF)
                {
                    continue;
                }

                bool sliced = false;
                bool tiled = false;
                bool partial = false;
                foreach (ArtLayer l in ArtTable.Layers(v))
                {
                    sliced |= l.SliceInset > 0;
                    tiled |= l.Tile > 0;
                    partial |= l.MinX != 0 || l.MinY != 0 || l.MaxX != 1024 || l.MaxY != 1024;
                }

                var prims = new List<DrawPrimitive>();
                if (!partial)
                {
                    // Every Box layer is whole (15 §15.17 Public surface).
                    prims.Add(ArtFrames.Box(v, ColourRole.Building, -310f, 20f, 290f, 470f));
                }

                if (!sliced)
                {
                    prims.Add(ArtFrames.Segment(v, ColourRole.Taxiway, 100f, 200f, 400f, 600f, 45f));
                }

                if (!sliced && !tiled)
                {
                    prims.Add(new DrawPrimitive(PrimitiveKind.Dot, DrawLayer.Agent, ColourRole.Agent, v, new WorldPoint(-50f, 75f), default(WorldPoint), 30f, new WorldPoint(3f, 4f), paint, new SourceRef(SourceKind.Aircraft, 7UL, 0)));
                }

                foreach (DrawPrimitive p in prims)
                {
                    foreach (float e in new[] { 0.5f, 400f, 5000f })
                    {
                        string what = string.Format(CultureInfo.InvariantCulture, "{0} {1} at e {2}", p.Kind, v, e);
                        AssertSameBytes(t, roles, ArtFrames.Of(p), ArtFrames.Of(ArtFrames.Lifted(p, e)), what);
                        ArtGeometry.AssertMatches(t, ArtRef.Fill(ArtFrames.Of(p), roles, true), what + ", against 15 §15.17");
                    }
                }

                checkedVisuals++;
            }

            Assert.Equal(ArtTable.AllVisuals().Length - 6, checkedVisuals);

            // A max-tier frame with nothing airborne: lifting every non-aircraft
            // primitive changes no byte (15 §15.22, "every frame with nothing
            // airborne is unchanged").
            var m = new MaxTierScene();
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = m.Builder();
            c.Update(MaxTierScene.Camera, Gfx.High());
            RenderFrame frame = b.Build(MaxTierScene.Camera, Gfx.High());
            List<DrawPrimitive> all = Prims.Copy(frame);
            var lifted = new List<DrawPrimitive>();
            int raised = 0;
            foreach (DrawPrimitive p in all)
            {
                Assert.True(p.Elevation == 0f, "the motion-free max-tier frame has an airborne primitive: " + Prims.Show(p));
                bool aircraft = p.Visual >= VisualId.AircraftA && p.Visual <= VisualId.AircraftF;
                lifted.Add(aircraft ? p : ArtFrames.Lifted(p, 250f));
                raised += aircraft ? 0 : 1;
            }

            Assert.True(raised > 16 * RenderConst.MaxDrawnAgentsPerNode, "the frame has too few non-aircraft primitives: " + raised);
            var copy = new RenderFrame(frame.Tick, frame.Camera, all.ToArray(), frame.Graphics);
            var raisedFrame = new RenderFrame(frame.Tick, frame.Camera, lifted.ToArray(), frame.Graphics);
            AssertSameBytes(t, roles, copy, raisedFrame, "max-tier frame with every non-aircraft primitive lifted");
            Assert.Empty(m.Guard.Violations);
        }

        private static void AssertAircraftC(ISpriteTessellator t, Rgba[] roles, in DrawPrimitive ground, float e, double half, double[] logo, double shiftX, double shiftY)
        {
            string what = "AircraftC Size 512 at e " + e.ToString("R", CultureInfo.InvariantCulture);

            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ground), roles, false));
            float[] uvs = Prefix(t.Uvs, 8 * t.QuadCount);
            byte[] colours = Prefix(t.Colours, 16 * t.QuadCount);

            Assert.Equal(G + 9, t.Fill(ArtFrames.Of(ArtFrames.Lifted(ground, e)), roles, false));
            Assert.Equal(G + 9, t.QuadCount);
            ArtGeometry.AssertQuad(t, 0, new double[] { 0, 0, 64, 0, 64, 64, 0, 64 }, ArtGeometry.Uvs(1040, 3088, 1520, 3568), what + ": ground tile");
            ArtGeometry.AssertQuad(
                t,
                G,
                new double[] { -256 + shiftX, -256 + shiftY, 256 + shiftX, -256 + shiftY, 256 + shiftX, 256 + shiftY, -256 + shiftX, 256 + shiftY },
                ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, 0))),
                what + ": shadow, true size, longer shift");
            ArtGeometry.AssertColour(t, G, 0, 0, 0, 255, what + ": shadow");
            double[] whole = { -half, -half, half, -half, half, half, -half, half };
            for (int l = 1; l <= 6; l++)
            {
                ArtGeometry.AssertQuad(t, G + l, whole, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, l))), what + ": layer " + l + ", scaled");
            }

            ArtGeometry.AssertQuad(t, G + 7, new[] { logo[0], logo[1], logo[2], logo[1], logo[2], logo[3], logo[0], logo[3] }, ArtGeometry.Uvs(Art2DFactory.LogoRect(LogoMark.Disc)), what + ": logo, scaled");
            ArtGeometry.AssertQuad(t, G + 8, whole, ArtGeometry.Uvs(ArtCells.RectOf(ArtCells.Aircraft(2, 7))), what + ": glazing, scaled");

            // Same rects and colours as on the ground, exactly.
            Assert.Equal(uvs, Prefix(t.Uvs, 8 * t.QuadCount));
            Assert.Equal(colours, Prefix(t.Colours, 16 * t.QuadCount));
        }

        private static void AssertLiftedAgainstGround(ISpriteTessellator t, Rgba[] roles, in DrawPrimitive p, float e, bool linear, string what)
        {
            int count = t.Fill(ArtFrames.Of(p), roles, linear);
            Assert.Equal(ArtRef.Fill(ArtFrames.Of(p), roles, linear).Count, count);
            float[] corners = Prefix(t.Corners, 8 * count);
            float[] uvs = Prefix(t.Uvs, 8 * count);
            byte[] colours = Prefix(t.Colours, 16 * count);

            RenderFrame frame = ArtFrames.Of(ArtFrames.Lifted(p, e));
            Assert.True(t.Fill(frame, roles, linear) == count, what + ": " + t.QuadCount + " quads, expected the ground count " + count);
            ArtGeometry.AssertMatches(t, ArtRef.Fill(frame, roles, linear), what);
            Assert.True(Same(uvs, Prefix(t.Uvs, 8 * count)), what + ": Uvs differ from e = 0");
            Assert.True(Same(colours, Prefix(t.Colours, 16 * count)), what + ": colours differ from e = 0");

            // Every layer of the aircraft moves: the shadow by its shift, the rest by the scale.
            for (int q = G; q < count; q++)
            {
                bool moved = false;
                for (int i = 0; i < 8; i++)
                {
                    moved |= Math.Abs(t.Corners[(8 * q) + i] - corners[(8 * q) + i]) > ArtGeometry.CornerTolerance;
                }

                Assert.True(moved, what + ": quad " + q + " did not move from its e = 0 corners");
            }
        }

        /// <summary>Both frames give the same QuadCount and the same buffers, bit for bit; the last Fill is linear.</summary>
        private static void AssertSameBytes(ISpriteTessellator t, Rgba[] roles, in RenderFrame expected, in RenderFrame actual, string what)
        {
            foreach (bool linear in new[] { false, true })
            {
                int count = t.Fill(expected, roles, linear);
                float[] corners = Prefix(t.Corners, 8 * count);
                float[] uvs = Prefix(t.Uvs, 8 * count);
                byte[] colours = Prefix(t.Colours, 16 * count);
                Assert.True(t.Fill(actual, roles, linear) == count, what + " (linear " + linear + "): " + t.QuadCount + " quads, expected " + count);
                Assert.True(Same(corners, Prefix(t.Corners, 8 * count)), what + " (linear " + linear + "): corners not byte-identical");
                Assert.True(Same(uvs, Prefix(t.Uvs, 8 * count)), what + " (linear " + linear + "): Uvs not byte-identical");
                Assert.True(Same(colours, Prefix(t.Colours, 16 * count)), what + " (linear " + linear + "): colours not byte-identical");
            }
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            return a.AsSpan().SequenceEqual(b);
        }

        private static T[] Prefix<T>(T[] a, int n)
        {
            var p = new T[n];
            Array.Copy(a, p, n);
            return p;
        }
    }
}
