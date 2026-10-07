using System.Collections.Generic;
using AirportSim.Sim.Airside;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.16 "Runway markings": the integer rule, its worked fixture
    /// example, and the designator numbering. Every expected value is worked
    /// by hand from the rule, with C# long division truncating toward zero.
    /// </summary>
    public sealed class SceneRunwayMarkingTests
    {
        private static List<DrawPrimitive> Markings(IReadOnlyList<DrawPrimitive> all, ushort runway)
        {
            return Prims.Of(all, SourceKind.RunwayMarking, runway);
        }

        private static void AssertCommon(in DrawPrimitive p, string what)
        {
            Assert.True(p.Layer == DrawLayer.Runway, what + ": layer " + p.Layer + ": " + Prims.Show(p));
            Assert.True(p.Colour == ColourRole.RunwayMarking, what + ": colour " + p.Colour + ": " + Prims.Show(p));
            Art.AssertZeroPaint(p, what);
        }

        /// <summary>Edge lines (Sub 0), both thresholds (Sub 1, 2).</summary>
        private static void AssertEdgesAndThresholds(
            IReadOnlyList<DrawPrimitive> all,
            ushort runway,
            (float X0, float Y0, float X1, float Y1, float W) geometry,
            (float X, float Y) threshold0,
            (float X, float Y) threshold1)
        {
            (float x0, float y0, float x1, float y1, float w) = geometry;
            string r = "runway " + runway;
            DrawPrimitive edges = Art.Single(all, SourceKind.RunwayMarking, runway, 0);
            Art.AssertSegment(edges, VisualId.RunwayEdgeLines, x0, y0, x1, y1, w, r + " edge lines");
            AssertCommon(edges, r + " edge lines");

            DrawPrimitive t0 = Art.Single(all, SourceKind.RunwayMarking, runway, 1);
            Art.AssertDot(t0, VisualId.RunwayThreshold, threshold0.X, threshold0.Y, w, r + " threshold at end 0");
            Art.AssertFacing(x1 - x0, y1 - y0, t0, r + " threshold at end 0 faces f = (dx, dy)");
            AssertCommon(t0, r + " threshold at end 0");

            DrawPrimitive t1 = Art.Single(all, SourceKind.RunwayMarking, runway, 2);
            Art.AssertDot(t1, VisualId.RunwayThreshold, threshold1.X, threshold1.Y, w, r + " threshold at end 1");
            Art.AssertFacing(x0 - x1, y0 - y1, t1, r + " threshold at end 1 faces f = (-dx, -dy)");
            AssertCommon(t1, r + " threshold at end 1");
        }

        /// <summary>Designator digits, Sub 3..6: (digit, centre) for end 0 tens, units, end 1 tens, units.</summary>
        private static void AssertDesignators(
            IReadOnlyList<DrawPrimitive> all,
            ushort runway,
            (float Dx, float Dy) forward0,
            float g,
            (int Digit, float X, float Y)[] digits)
        {
            Assert.Equal(4, digits.Length);
            for (int i = 0; i < 4; i++)
            {
                (int digit, float x, float y) = digits[i];
                string what = "runway " + runway + " designator Sub " + (3 + i);
                DrawPrimitive p = Art.Single(all, SourceKind.RunwayMarking, runway, 3 + i);
                Art.AssertDot(p, Art.Digit(digit), x, y, g, what);
                float sign = i < 2 ? 1f : -1f;
                Art.AssertFacing(sign * forward0.Dx, sign * forward0.Dy, p, what + " faces its end's forward");
                AssertCommon(p, what);
            }
        }

        private static void AssertDashes(IReadOnlyList<DrawPrimitive> all, ushort runway, float w, (float X0, float Y0, float X1, float Y1)[] dashes)
        {
            List<DrawPrimitive> markings = Markings(all, runway);
            Assert.True(markings.Count == 7 + dashes.Length, "runway " + runway + ": " + markings.Count + " markings, expected " + (7 + dashes.Length) + ": " + Prims.Show(markings));
            for (int k = 0; k < dashes.Length; k++)
            {
                (float x0, float y0, float x1, float y1) = dashes[k];
                string what = "runway " + runway + " dash " + k;
                DrawPrimitive p = Art.Single(all, SourceKind.RunwayMarking, runway, 7 + k);
                Art.AssertSegment(p, VisualId.RunwayCentreDash, x0, y0, x1, y1, w, what);
                AssertCommon(p, what);
            }
        }

        [Fact]
        public void test_scene_runway_markings_follow_the_integer_rule()
        {
            // The §15.12 fixture: runway 1 (-2000,0)-(0,0), W 45, threshold node
            // 1 at (0,0), active_direction_deg 270. L = 2000, end 1 active.
            var guard = new CallGuard();
            var host = new FakeHost(guard, 1UL);
            var fixture = new FakeAirside(guard, Phase1Sim.AirsideLayout());
            ISceneBuilder fb = RenderFactory.CreateSceneBuilder(new RenderSources(host, fixture, null), Phase1RenderLayout.Build());
            List<DrawPrimitive> f = Prims.Copy(fb.Build(ArtScene.Overview, Gfx.High()));

            AssertEdgesAndThresholds(f, 1, (-2000f, 0f, 0f, 0f, 45f), (-1978f, 0f), (-22f, 0f));
            AssertDesignators(f, 1, (2000f, 0f), 18f, new[] { (0, -1910f, 5f), (9, -1910f, -5f), (2, -90f, -5f), (7, -90f, 5f) });

            // n = 19 dashes from s_0 = 167, each W long with W gaps: s_k = 167 + 90k.
            var fixtureDashes = new (float, float, float, float)[19];
            for (int k = 0; k < 19; k++)
            {
                float s = 167 + (90 * k);
                fixtureDashes[k] = (-2000f + s, 0f, -2000f + s + 45f, 0f);
            }

            AssertDashes(f, 1, 45f, fixtureDashes);
            Assert.Equal(-2000f + 167f, Art.Single(f, SourceKind.RunwayMarking, 1, 7).A.X);
            Assert.Equal(-2000f + 1787f, Art.Single(f, SourceKind.RunwayMarking, 1, 25).A.X);
            Assert.Empty(Art.OfKind(f, SourceKind.RunwayMarking).FindAll(p => p.Source.Sub > 25));

            // Diagonal runway 2: (0,0)-(100,200), W 20, threshold at (0,0) so end
            // 0 is active, 27°: end 0 reads 03 and end 1 reads 21.
            //   L = isqrt(50000) = 223, G = 8, e = (2, -2), M = 60, R = 103, n = 3, s_0 = 61
            //   thresholds (1000/223, 2000/223) = (4,8) and (100 - 4, 200 - 8) = (96,192)
            //   end 0 digits at d = 40: (3600/223, 8200/223) = (16,36), (4400/223, 7800/223) = (19,34)
            //   end 1 digits: (100 - 16, 200 - 36) = (84,164), (100 - 19, 200 - 34) = (81,166)
            //   dashes at s = 61, 101, 141, each to s + 20
            // Swapping the geometry's ends puts the threshold at end 1, and
            // makes the designators follow it.
            var s2 = new ArtScene();
            s2.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold)
                .Node(2, 0, 130, TaxiNodeKind.RunwayThreshold)
                .Node(3, 50000, 50000, TaxiNodeKind.RunwayThreshold)
                .Node(4, 3000, 500, TaxiNodeKind.RunwayThreshold)
                .Runway(2, 1, 27, 0, 0, 100, 200, 20)
                .Runway(3, 2, 2, 0, 0, 0, 130, 20)
                .Runway(4, 3, 90, 50000, 50000, 50000, 50000, 30)
                .Runway(5, 4, 90, 3000, 500, 3000, 500, 30)
                .Geometry(6, 1000, 0, 1300, 0, 20)
                .Unpositioned(7)
                .RunwayDefOnly(7, 7, 90)
                .Geometry(7, 2000, 0, 2300, 0, 20)
                .Done();
            List<DrawPrimitive> all = s2.Frame(s2.Builder());

            AssertEdgesAndThresholds(all, 2, (0f, 0f, 100f, 200f, 20f), (4f, 8f), (96f, 192f));
            AssertDesignators(all, 2, (100f, 200f), 8f, new[] { (0, 16f, 36f), (3, 19f, 34f), (2, 84f, 164f), (1, 81f, 166f) });
            AssertDashes(all, 2, 20f, new[] { (27f, 54f, 36f, 72f), (45f, 90f, 54f, 108f), (63f, 126f, 72f, 144f) });

            // Short runway 3: (0,0)-(0,130), W 20, threshold at (0,130): end 1
            // active at 2°, which reads 36 (0 is written 36); end 0 reads 18.
            //   L = 130, M = 60, R = 10 < W: no dashes, edges, thresholds and designators kept.
            //   thresholds (0,10) and (0,120); G = 8, e = (2,-2), d = 40
            //   end 0, forward +Y, left is -X: (-2,40), (2,40); end 1: (2,90), (-2,90)
            AssertEdgesAndThresholds(all, 3, (0f, 0f, 0f, 130f, 20f), (0f, 10f), (0f, 120f));
            AssertDesignators(all, 3, (0f, 130f), 8f, new[] { (1, -2f, 40f), (8, 2f, 40f), (3, 2f, 90f), (6, -2f, 90f) });
            AssertDashes(all, 3, 20f, new (float, float, float, float)[0]);

            // Zero-length runways 4 and 5 (L = 0): the surface, and no markings.
            foreach (ushort zero in new ushort[] { 4, 5 })
            {
                Assert.Empty(Markings(all, zero));
                Assert.Equal(VisualId.RunwaySurface, Prims.Single(all, SourceKind.Runway, zero).Visual);
            }

            // Runway 6 has no RunwayDef, and runway 7's threshold node has no
            // position: no designators, but edges, thresholds and dashes stay.
            // L = 300, M = 60, R = 180, n = 5, s_0 = 60 + (180 - 180) / 2 = 60.
            foreach ((ushort id, float x) in new[] { ((ushort)6, 1000f), ((ushort)7, 2000f) })
            {
                AssertEdgesAndThresholds(all, id, (x, 0f, x + 300f, 0f, 20f), (x + 10f, 0f), (x + 290f, 0f));
                for (int sub = 3; sub <= 6; sub++)
                {
                    Assert.True(Prims.Of(Markings(all, id), SourceKind.RunwayMarking, id).FindAll(p => p.Source.Sub == sub).Count == 0, "runway " + id + " has a designator at Sub " + sub);
                }

                List<DrawPrimitive> dashes = Markings(all, id).FindAll(p => p.Source.Sub >= 7);
                Assert.True(dashes.Count == 5, "runway " + id + " dashes: " + Prims.Show(dashes));
                for (int k = 0; k < 5; k++)
                {
                    float s = 60 + (40 * k);
                    Art.AssertSegment(Art.Single(all, SourceKind.RunwayMarking, id, 7 + k), VisualId.RunwayCentreDash, x + s, 0f, x + s + 20f, 0f, 20f, "runway " + id + " dash " + k);
                }
            }

            // A queue colours the surface, never the markings.
            s2.Airside!.SetQueue(2, 3);
            List<DrawPrimitive> queued = s2.Frame(s2.Builder());
            Assert.Equal(ColourRole.RunwayQueued, Prims.Single(queued, SourceKind.Runway, 2).Colour);
            foreach (DrawPrimitive p in Markings(queued, 2))
            {
                Assert.True(p.Colour == ColourRole.RunwayMarking, "a queued runway's marking changed colour: " + Prims.Show(p));
            }

            // Within the Runway layer every surface comes before any marking.
            List<DrawPrimitive> layer = Prims.InLayer(queued, DrawLayer.Runway);
            int surfaces = Art.OfKind(queued, SourceKind.Runway).Count;
            for (int i = 0; i < layer.Count; i++)
            {
                Assert.True((i < surfaces) == (layer[i].Source.Kind == SourceKind.Runway), "Runway layer order: " + Prims.Show(layer));
            }

            Assert.Empty(guard.Violations);
            Assert.Empty(s2.Guard.Violations);
        }

        [Fact]
        public void test_scene_runway_designators_follow_active_direction_from_either_end()
        {
            // One horizontal runway per case, (0, 1000r)-(400, 1000r), W 20, with
            // its threshold node at end 0 (forward +X) or at end 1 (forward -X).
            // The active end's number is ((D + 5) / 10) mod 36 with D the degrees
            // made non-negative mod 360, 0 written as 36; the other end's uses D + 180.
            (int Deg, int Active, int Other)[] cases =
            {
                (270, 27, 9),
                (90, 9, 27),
                (2, 36, 18),
                (4, 36, 18),
                (5, 1, 19),
                (355, 36, 18),
                (354, 35, 17),
                (360, 36, 18),
                (0, 36, 18),
                (-5, 36, 18),
                (-90, 27, 9),
                (725, 1, 19),
                (184, 18, 36),
                (175, 18, 36),
                (174, 17, 35),
            };

            var s = new ArtScene();
            ushort id = 1;
            foreach ((int deg, _, _) in cases)
            {
                foreach (bool atEnd0 in new[] { true, false })
                {
                    int y = 1000 * id;
                    s.Node(id, atEnd0 ? 0 : 400, y, TaxiNodeKind.RunwayThreshold).Runway(id, id, deg, 0, y, 400, y, 20);
                    id++;
                }
            }

            s.Done();
            List<DrawPrimitive> all = s.Frame(s.Builder());
            id = 1;
            foreach ((int deg, int active, int other) in cases)
            {
                foreach (bool atEnd0 in new[] { true, false })
                {
                    int end0 = atEnd0 ? active : other;
                    int end1 = atEnd0 ? other : active;
                    int[] digits = { end0 / 10, end0 % 10, end1 / 10, end1 % 10 };
                    for (int i = 0; i < 4; i++)
                    {
                        DrawPrimitive p = Art.Single(all, SourceKind.RunwayMarking, id, 3 + i);
                        Assert.True(
                            p.Visual == Art.Digit(digits[i]),
                            deg + "° with the threshold at end " + (atEnd0 ? 0 : 1) + ": Sub " + (3 + i) + " is " + p.Visual + ", expected digit " + digits[i] + " (end 0 reads " + end0.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ", end 1 reads " + end1.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + ")");
                    }

                    id++;
                }
            }

            Assert.Empty(s.Guard.Violations);
        }
    }
}
