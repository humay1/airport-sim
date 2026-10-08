using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.19 (the render time τ and the sub-tick Build) and §15.20
    /// (taxi gliding, approach, hold, landing and takeoff), Q-132, against
    /// fake sources. Positions and elevations are compared within 15
    /// §15.23's 0.01 world units; everything else exactly.
    /// </summary>
    public sealed class SceneMotionTests
    {
        private const long TickMicros = RenderConstants.REAL_MICROSECONDS_PER_TICK_1X;

        private static List<DrawPrimitive> Build(ISceneBuilder b, in CameraView camera, in GraphicsSettings graphics, long sub)
        {
            return Prims.Copy(b.Build(camera, graphics, sub));
        }

        [Fact]
        public void test_scene_build_rejects_sub_tick_out_of_range()
        {
            var s = new SmallScene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 12);
            s.Airside!.SetTracks(Tracks.Make(5UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction));
            CameraView cam = SmallScene.Overview;
            GraphicsSettings g = Gfx.High();

            // A fresh builder, then one that has already built: the throw comes
            // before anything is read, CurrentTick included (15 §15.19).
            ISceneBuilder fresh = s.Builder();
            ISceneBuilder used = s.Builder();
            used.Build(cam, g, 0);
            foreach (ISceneBuilder b in new[] { fresh, used })
            {
                foreach (long bad in new[] { -1L, TickMicros, TickMicros + 1L, long.MinValue, long.MaxValue, -TickMicros })
                {
                    long reads = s.Host.CurrentTickReads;
                    long air = s.Airside.QueryCalls;
                    long flow = s.Flow.QueryCalls;
                    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => b.Build(cam, g, bad));
                    Assert.True(ex.ParamName == "subTickMicroseconds", "sub-tick " + bad + ": ParamName " + ex.ParamName);
                    Assert.True(s.Host.CurrentTickReads == reads, "sub-tick " + bad + ": CurrentTick was read before the throw");
                    Assert.True(s.Airside.QueryCalls == air && s.Flow.QueryCalls == flow, "sub-tick " + bad + ": the sim was queried before the throw");
                }
            }

            // The range's two ends are valid, and stamp the frame with CurrentTick.
            foreach (long ok in new[] { 0L, TickMicros - 1L })
            {
                RenderFrame f = fresh.Build(cam, g, ok);
                Assert.Equal(s.Host.Tick, f.Tick);
                Assert.True(Prims.InLayer(f.Primitives, DrawLayer.Aircraft).Count == 1, "sub-tick " + ok + ": the frame was not built");
            }

            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_two_argument_build_equals_sub_tick_zero()
        {
            // The small scene, with a gliding aircraft whose sub-tick term is not 0.
            var small = new SmallScene();
            small.Flow!.SetPopulation(SmallScene.Queue5, 30);
            small.Airside!.Occupy(SmallScene.S1, 3UL);
            small.Airside.SetTracks(
                Tracks.Make(1UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: Fx.FromRatio(1, 4), phaseEnteredAt: 1UL, dueAt: 21UL),
                Tracks.Make(2UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction),
                Tracks.Make(3UL, AircraftLegPhase.OnStand, atNode: SmallScene.StandNode1, stand: SmallScene.S1));
            CompareTwoAndThree("small scene", () => small.Builder(), SmallScene.Overview, small.Guard, () => small.Airside.QueryCalls + small.Flow.QueryCalls);

            // The playtest layout, with every off-graph row drawn.
            MotionScene m = MotionScene.Playtest();
            m.Host.Tick = 101UL;
            m.Airside.SetTracks(
                Tracks.Make(11UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: 105UL),
                Tracks.Make(12UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 50UL),
                Tracks.Make(13UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 97UL, dueAt: 107UL),
                Tracks.Make(14UL, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: 96UL, dueAt: 106UL));
            CompareTwoAndThree("playtest off-graph rows", () => m.Builder(), MotionScene.Overview, m.Guard, () => m.Airside.QueryCalls + m.Flow.QueryCalls);

            // The max-tier motion scene, with walkways and agents.
            var t = new MaxTierScene(motion: true);
            RenderFactory.CreatePromotionController(t.Sources, t.Layout).Update(MaxTierScene.Camera, Gfx.High());
            CompareTwoAndThree("max tier", () => t.Builder(), MaxTierScene.Camera, t.Guard, () => t.Airside.QueryCalls + t.Flow.QueryCalls);
        }

        private static void CompareTwoAndThree(string what, Func<ISceneBuilder> make, CameraView cam, CallGuard guard, Func<long> queries)
        {
            foreach (GraphicsSettings g in new[] { Gfx.High(), Gfx.Custom(true, 3, 15, 50, false), RenderFactory.GraphicsForPreset(GraphicsPreset.Low) })
            {
                // Two fresh builders over the same state give the same list.
                List<string> two = Prims.Lines(Prims.Copy(make().Build(cam, g)));
                List<string> three = Prims.Lines(Build(make(), cam, g, 0));
                Assert.True(two.Count > 0, what + ": empty frame");
                Assert.True(two.Count == three.Count, what + ", " + Gfx.Show(g) + ": " + two.Count + " primitives from Build(camera, graphics), " + three.Count + " at sub-tick 0");
                for (int i = 0; i < two.Count; i++)
                {
                    Assert.True(two[i] == three[i], what + ", " + Gfx.Show(g) + ": primitive " + i + " is " + two[i] + " from Build(camera, graphics), " + three[i] + " at sub-tick 0");
                }

                // On one builder they are the same call: neither rebuilds after the other.
                ISceneBuilder b = make();
                b.Build(cam, g);
                long q = queries();
                List<string> again = Prims.Lines(Build(b, cam, g, 0));
                Assert.True(queries() == q, what + ": Build(camera, graphics, 0) after Build(camera, graphics) rebuilt");
                Assert.Equal(two, again);
                Build(b, cam, g, 1);
                Build(b, cam, g, 0);
                q = queries();
                Assert.Equal(two, Prims.Lines(Prims.Copy(b.Build(cam, g))));
                Assert.True(queries() == q, what + ": Build(camera, graphics) after Build(camera, graphics, 0) rebuilt");
            }

            Assert.True(guard.Violations.Count == 0, what + ": members outside 15 §15.6 were called: " + string.Join(", ", guard.Violations));
        }

        [Fact]
        public void test_scene_rebuilds_when_sub_tick_changes()
        {
            var s = new SmallScene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 12);
            s.Airside!.SetTracks(Tracks.Make(9UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: Fx.FromRatio(5, 20), phaseEnteredAt: 1UL, dueAt: 21UL));
            s.Host.Tick = 7UL;
            ISceneBuilder b = s.Builder();
            CameraView cam = SmallScene.Overview;
            GraphicsSettings g = Gfx.High();
            long Queries() => s.Airside.QueryCalls + s.Flow.QueryCalls;

            List<string> at0 = Prims.Lines(Build(b, cam, g, 0));
            long q = Queries();
            Assert.True(q > 0, "the first Build must query the sim");

            // The same tick, camera, settings and sub-tick: the previous frame,
            // with CurrentTick read and nothing else (15 §15.6).
            long reads = s.Host.CurrentTickReads;
            RenderFrame same = b.Build(cam, g, 0);
            Assert.Equal(q, Queries());
            Assert.True(s.Host.CurrentTickReads > reads, "every Build reads CurrentTick");
            Assert.Equal(at0, Prims.Lines(same.Primitives));
            Assert.Equal(s.Host.Tick, same.Tick);

            // Each changed sub-tick rebuilds, and the aircraft has moved; a
            // repeat of it does not rebuild. RenderFrame.Tick stays CurrentTick.
            List<string>? previous = null;
            foreach (long sub in new[] { 1L, 2L, 50000L, TickMicros - 1L, 25000L })
            {
                q = Queries();
                RenderFrame f = b.Build(cam, g, sub);
                Assert.True(Queries() > q, "sub-tick " + sub + " did not rebuild");
                Assert.Equal(s.Host.Tick, f.Tick);
                List<string> lines = Prims.Lines(f.Primitives);
                Assert.NotEqual(at0, lines);
                if (previous != null)
                {
                    Assert.NotEqual(previous, lines);
                }

                previous = lines;
                q = Queries();
                Assert.Equal(lines, Prims.Lines(b.Build(cam, g, sub).Primitives));
                Assert.True(Queries() == q, "a repeated sub-tick " + sub + " rebuilt");
            }

            // Back to 0 rebuilds, to the first frame: nothing is remembered between Builds.
            q = Queries();
            Assert.Equal(at0, Prims.Lines(Build(b, cam, g, 0)));
            Assert.True(Queries() > q, "returning to sub-tick 0 did not rebuild");

            // A fresh builder at a sub-tick gives what the used one gives there.
            List<string> used = Prims.Lines(Build(b, cam, g, 37500));
            Assert.Equal(used, Prims.Lines(Build(s.Builder(), cam, g, 37500)));
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_taxiing_aircraft_glides_with_sub_tick()
        {
            // E1 runs from node 1 (0,0) to node 2 (100,40), 20 ticks. The last
            // executed tick is 45 (CurrentTick 46), so EdgeProgress is
            // FromRatio(45 − 40, 20), as S6 stores it (12 §12.9).
            var s = new SmallScene();
            s.Host.Tick = 46UL;
            const ulong Entered = 40UL;
            Fx progress = Fx.FromRatio(5, 20);
            s.Airside!.SetTracks(
                Tracks.Make(401UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: progress, phaseEnteredAt: Entered, dueAt: Entered + 20UL),
                Tracks.Make(402UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: progress, phaseEnteredAt: Entered, dueAt: Entered),
                Tracks.Make(403UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: progress, phaseEnteredAt: Entered, dueAt: Entered - 3UL),
                Tracks.Make(404UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Junction, onEdge: SmallScene.E1, progress: Fx.FromRatio(1, 2)),
                Tracks.Make(405UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: Fx.FromRatio(1, 2), phaseEnteredAt: Entered, dueAt: Entered + 1UL),
                Tracks.Make(406UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction, phaseEnteredAt: Entered, dueAt: Entered + 20UL),
                Tracks.Make(407UL, AircraftLegPhase.Taxiing, atNode: SmallScene.StandNode2, phaseEnteredAt: Entered, dueAt: Entered + 20UL));
            ISceneBuilder b = s.Builder();
            CameraView cam = SmallScene.Overview;
            GraphicsSettings g = Gfx.High();

            // α = 0 is the merged value exactly: (float)(Raw / 2^32) = 0.25.
            List<DrawPrimitive> zero = Build(b, cam, g, 0);
            DrawPrimitive z401 = Motion.Aircraft(zero, 401UL, "α = 0");
            Prims.AssertPoint(25f, 10f, z401.A, "α = 0: a quarter of the way along E1");
            Prims.AssertPoint(50f, 20f, Motion.Aircraft(zero, 404UL, "α = 0").A, "α = 0: half way back from node 2");
            Assert.Equal(Prims.Lines(zero), Prims.Lines(Prims.Copy(s.Builder().Build(cam, g))));

            // The factor rises with α: t = clamp(Raw / 2^32 + α / Trav, 0, 1).
            double raw = progress.Raw / 4294967296.0;
            float lastX = float.NegativeInfinity;
            foreach (long sub in new[] { 0L, 1L, 10000L, 25000L, 50000L, 75000L, 90000L, TickMicros - 1L })
            {
                List<DrawPrimitive> all = Build(b, cam, g, sub);
                string what = "α = " + sub.ToString(CultureInfo.InvariantCulture) + " / 100 000";
                DrawPrimitive p = Motion.Aircraft(all, 401UL, what);
                double t = Motion.Clamp01(raw + ((sub / 100000.0) / 20.0));
                Motion.Near(100.0 * t, 40.0 * t, p.A, what + ", Trav 20");
                Assert.True(sub == 0 || p.A.X > lastX, what + ": the aircraft did not advance along the edge");
                lastX = p.A.X;
                Art.AssertFacing(100f, 40f, p, what + ": the edge's direction");
                Assert.Equal(ColourRole.AircraftMoving, p.Colour);
                Assert.Equal(0f, p.Elevation);

                // Trav ≤ 0 (equal, or a signed negative difference) adds nothing, and
                // neither does a track at a node. An unscheduled DueAt never glides either.
                foreach (ulong still in new[] { 402UL, 403UL, 404UL, 406UL, 407UL })
                {
                    Assert.True(
                        Prims.Show(Motion.Aircraft(all, still, what)) == Prims.Show(Motion.Aircraft(zero, still, "α = 0")),
                        what + ": flight " + still + " moved: " + Prims.Show(Motion.Aircraft(all, still, what)));
                }

                // It clamps at 1: progress 1/2 with Trav 1 is at node 2 from α = 1/2 on.
                double t405 = Motion.Clamp01(0.5 + (sub / 100000.0));
                Motion.Near(100.0 * t405, 40.0 * t405, Motion.Aircraft(all, 405UL, what).A, what + ", Trav 1");
            }

            // Near α = 1 it nears the next tick's position, EdgeProgress 6/20 at sub-tick 0.
            List<DrawPrimitive> late = Build(b, cam, g, TickMicros - 1L);
            s.Host.Tick = 47UL;
            s.Airside.SetTracks(Tracks.Make(401UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: Fx.FromRatio(6, 20), phaseEnteredAt: Entered, dueAt: Entered + 20UL));
            DrawPrimitive next = Motion.Aircraft(Build(b, cam, g, 0), 401UL, "the next tick, α = 0");
            Prims.AssertPoint(30f, 12f, next.A, "the next tick, α = 0");
            Motion.Near(next.A.X, next.A.Y, Motion.Aircraft(late, 401UL, "α = 99 999").A, "α = 99 999 against the next tick");
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_arrival_appears_in_the_approach_window()
        {
            // 15 §15.20's table for the playtest layout, from the test's own formulas.
            RunwayFrame fr = MotionScene.Runway1(-1850, 0);
            Motion.Near(-250, 0, new WorldPoint((float)fr.TD.X, (float)fr.TD.Y), "the test's touchdown");
            Motion.Near(-1500, 0, new WorldPoint((float)fr.LO.X, (float)fr.LO.Y), "the test's lift-off");
            Motion.Near(1750, 0, new WorldPoint((float)fr.FF.X, (float)fr.FF.Y), "the test's final fix");
            Motion.Near(7750, 0, new WorldPoint((float)fr.AE.X, (float)fr.AE.Y), "the test's approach entry");
            Motion.Near(-5500, 0, new WorldPoint((float)fr.CE.X, (float)fr.CE.Y), "the test's climb end");

            MotionScene m = MotionScene.Playtest();
            const ulong Sta = 100UL;
            m.Airside.SetTracks(Tracks.Make(501UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta));
            ISceneBuilder b = m.Builder();

            // Not drawn before STA − APPROACH_TICKS (τ 85).
            Motion.NotDrawn(m.At(b, 1UL, 0), 501UL, "τ 0");
            Motion.NotDrawn(m.At(b, 85UL, 0), 501UL, "τ 84");
            Motion.NotDrawn(m.At(b, 85UL, TickMicros - 1L), 501UL, "τ 84.99999");

            // At AE at the window's start, elevation 400.
            DrawPrimitive start = Motion.Aircraft(m.At(b, 86UL, 0), 501UL, "τ 85");
            Motion.Near(7750, 0, start.A, "τ 85: the approach entry");
            Motion.NearElevation(400, start, "τ 85");
            Art.AssertFacing(-2000f, 0f, start, "τ 85: fDep");
            Assert.Equal(ColourRole.AircraftMoving, start.Colour);
            Assert.Equal(PrimitiveKind.Dot, start.Kind);
            Assert.Equal(DrawLayer.Aircraft, start.Layer);
            Assert.Equal(VisualId.AircraftC, start.Visual);
            Assert.Equal(30f, start.Size);
            Assert.Equal(0, start.Source.Sub);

            // Along the centreline in between, every sub-tick, and at FF at STA, elevation 100.
            foreach ((ulong tick, long sub) in new[] { (86UL, 25000L), (90UL, 0L), (93UL, 50000L), (100UL, 99999L) })
            {
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                DrawPrimitive p = Motion.Aircraft(m.At(b, tick, sub), 501UL, what);
                fr.Approach(tau, Sta).AssertOn(p, what);
                Assert.Equal(ColourRole.AircraftMoving, p.Colour);
            }

            Motion.Near(4750, 0, Motion.Aircraft(m.At(b, 93UL, 50000), 501UL, "τ 92.5").A, "τ 92.5: half way from AE to FF");
            DrawPrimitive end = Motion.Aircraft(m.At(b, 101UL, 0), 501UL, "τ 100 (STA)");
            Motion.Near(1750, 0, end.A, "τ = STA: the final fix");
            Motion.NearElevation(100, end, "τ = STA");
            Art.AssertFacing(-2000f, 0f, end, "τ = STA: fDep");
            DrawPrimitive after = Motion.Aircraft(m.At(b, 101UL, 75000), 501UL, "τ 100.75");
            Motion.Near(1750, 0, after.A, "past STA, v clamps at 1");
            Assert.Empty(m.Guard.Violations);

            // The predicted runway: over Layout().Runways in ascending RunwayId, the
            // first with the smallest RunwayQueueLength (12 §12.5's rule).
            var a = new ArtScene();
            a.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold).Node(2, 0, 1000, TaxiNodeKind.RunwayThreshold).Node(3, 0, 2000, TaxiNodeKind.RunwayThreshold).Node(9, 300, 0);
            a.Runway(1, 1, 270, -2000, 0, 0, 0, 45).Runway(2, 2, 270, -2000, 1000, 0, 1000, 45).Runway(3, 3, 270, -2000, 2000, 0, 2000, 45).Done();
            a.Airside!.SetTracks(Tracks.Make(601UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta));
            ISceneBuilder pb = a.Builder();
            (int Q1, int Q2, int Q3, float Y)[] cases =
            {
                (2, 1, 1, 1000f),
                (0, 0, 0, 0f),
                (1, 1, 0, 2000f),
                (1, 0, 0, 1000f),
                (5, 3, 4, 1000f),
                (0, 7, 0, 0f),
            };
            foreach ((int q1, int q2, int q3, float y) in cases)
            {
                a.Airside.SetQueue(1, q1);
                a.Airside.SetQueue(2, q2);
                a.Airside.SetQueue(3, q3);
                a.Host.Tick = 93UL;
                List<DrawPrimitive> all = Prims.Copy(pb.Build(ArtScene.Overview, Gfx.High(), 50000));
                a.Host.Tick = 94UL;
                pb.Build(ArtScene.Overview, Gfx.High(), 0);
                string what = string.Format(CultureInfo.InvariantCulture, "queues {0}/{1}/{2}", q1, q2, q3);
                Motion.Near(4750, y, Motion.Aircraft(all, 601UL, what).A, what + ": on the predicted runway's centreline");
            }

            // It is computed at most once per rebuild: the queue reads do not grow with the approaching arrivals.
            long Rebuild()
            {
                long before = a.Airside.QueueCalls;
                a.Host.Tick++;
                pb.Build(ArtScene.Overview, Gfx.High(), 50000);
                return a.Airside.QueueCalls - before;
            }

            long one = Rebuild();
            a.Airside.SetTracks(
                Tracks.Make(601UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta),
                Tracks.Make(602UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta + 1UL),
                Tracks.Make(603UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta + 2UL),
                Tracks.Make(604UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: Sta + 3UL));
            long four = Rebuild();
            Assert.True(one == four, "RunwayQueueLength calls per rebuild: " + one + " with one approaching arrival, " + four + " with four");
            Assert.Empty(a.Guard.Violations);
        }

        [Fact]
        public void test_scene_held_arrival_flies_the_square_hold()
        {
            RunwayFrame fr = MotionScene.Runway1(-1850, 0);
            P2[] c = fr.HoldCorners();
            float[,] spec = { { 1750, 0 }, { 1750, 1000 }, { 2750, 1000 }, { 2750, 0 } };
            for (int k = 0; k < 4; k++)
            {
                Motion.Near(spec[k, 0], spec[k, 1], new WorldPoint((float)c[k].X, (float)c[k].Y), "the test's corner C" + k);
            }

            MotionScene m = MotionScene.Playtest();
            const ulong Entered = 50UL;
            m.Airside.SetTracks(Tracks.Make(701UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: Entered));
            ISceneBuilder b = m.Builder();

            // (tick, sub, corner or null, leg facing): leg 0 (0, 2000), leg 1 g = fArr
            // (2000, 0), leg 2 (0, −2000), leg 3 fDep (−2000, 0).
            (ulong Tick, long Sub, int Corner, float Fx, float Fy)[] points =
            {
                (51UL, 0L, 0, 0f, 2000f),
                (52UL, 50000L, -1, 0f, 2000f),
                (54UL, 0L, 1, 2000f, 0f),
                (55UL, 25000L, -1, 2000f, 0f),
                (57UL, 0L, 2, 0f, -2000f),
                (58UL, 50000L, -1, 0f, -2000f),
                (60UL, 0L, 3, -2000f, 0f),
                (61UL, 50000L, -1, -2000f, 0f),
                (63UL, 0L, 0, 0f, 2000f),
                (63UL, 75000L, -1, 0f, 2000f),
                (51UL + 1200UL, 50000L, -1, 0f, 2000f),
                (50UL, 0L, 0, 0f, 2000f),
                (1UL, 0L, 0, 0f, 2000f),
            };
            foreach ((ulong tick, long sub, int corner, float fx, float fy) in points)
            {
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                DrawPrimitive p = Motion.Aircraft(m.At(b, tick, sub), 701UL, what);
                Expected e = fr.Hold(tau, Entered);
                e.AssertOn(p, what);
                Art.AssertFacing(fx, fy, p, what + ": the leg's facing");
                Motion.NearElevation(100, p, what);
                Assert.Equal(ColourRole.AircraftHolding, p.Colour);
                if (corner >= 0)
                {
                    Motion.Near(spec[corner, 0], spec[corner, 1], p.A, what + ": corner C" + corner);
                }
            }

            // The wrap after four legs, and τ < PhaseEnteredAt held at FF.
            Motion.Near(1750, 250, Motion.Aircraft(m.At(b, 63UL, 75000), 701UL, "τ 62.75").A, "τ 62.75: a quarter up leg 0 again");
            Motion.Near(1750, 500, Motion.Aircraft(m.At(b, 52UL, 50000), 701UL, "τ 51.5").A, "τ 51.5: half way up leg 0");
            Motion.Near(1750, 0, Motion.Aircraft(m.At(b, 30UL, 50000), 701UL, "τ 29.5").A, "τ before PhaseEnteredAt: at FF");
            Assert.Empty(m.Guard.Violations);
        }

        [Fact]
        public void test_scene_arrival_lands_and_rolls_out_to_the_exit_node()
        {
            RunwayFrame fr = MotionScene.Runway1(-1850, 0);
            MotionScene m = MotionScene.Playtest();
            const ulong Entered = 200UL;
            const ulong Due = 210UL;
            m.Airside.SetTracks(
                Tracks.Make(801UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: Entered, dueAt: Due),
                Tracks.Make(802UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: Entered, dueAt: Entered),
                Tracks.Make(803UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: Entered, dueAt: Entered - 3UL),
                Tracks.Make(804UL, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: Entered, dueAt: Due),
                Tracks.Make(805UL, AircraftLegPhase.HeldOnTaxiway, kind: MovementKind.Arrival, atNode: 4));
            ISceneBuilder b = m.Builder();

            // w = 0 at FF, w = 1/2 at TD with elevation 0, w = 1 at X (node 4, (−1850, 0)).
            DrawPrimitive w0 = Motion.Aircraft(m.At(b, 201UL, 0), 801UL, "w 0");
            Motion.Near(1750, 0, w0.A, "w 0: the final fix");
            Motion.NearElevation(100, w0, "w 0");
            DrawPrimitive quarter = Motion.Aircraft(m.At(b, 203UL, 50000), 801UL, "w 1/4");
            Motion.Near(750, 0, quarter.A, "w 1/4: half way down the final");
            Motion.NearElevation(50, quarter, "w 1/4");
            DrawPrimitive td = Motion.Aircraft(m.At(b, 206UL, 0), 801UL, "w 1/2");
            Motion.Near(-250, 0, td.A, "w 1/2: touchdown");
            Motion.NearElevation(0, td, "w 1/2");
            Motion.Near(-1450, 0, Motion.Aircraft(m.At(b, 208UL, 50000), 801UL, "w 3/4").A, "w 3/4: TD + (X − TD) × 3/4");
            List<DrawPrimitive> atEnd = m.At(b, 211UL, 0);
            DrawPrimitive x = Motion.Aircraft(atEnd, 801UL, "w 1");
            Motion.Near(-1850, 0, x.A, "w 1: the exit node");
            Motion.NearElevation(0, x, "w 1");
            Motion.Near(x.A.X, x.A.Y, Motion.Aircraft(atEnd, 805UL, "at node 4").A, "the rollout ends where OffRunway places it");
            Motion.Near(-1850, 0, Motion.Aircraft(m.At(b, 230UL, 50000), 801UL, "w past 1").A, "w clamps at 1");

            // Every sub-tick over the final and rollout: the formulas, elevation
            // strictly decreasing over the final and 0 after touchdown, the
            // rollout moving toward X, facing fDep like a departure's.
            double lastElevation = double.PositiveInfinity;
            double lastX = double.PositiveInfinity;
            for (long step = 0; step <= 40; step++)
            {
                ulong tick = 201UL + (ulong)(step / 4);
                long sub = (step % 4) * 25000L;
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                List<DrawPrimitive> all = m.At(b, tick, sub);
                DrawPrimitive p = Motion.Aircraft(all, 801UL, what);
                fr.Landing(tau, Entered, Due).AssertOn(p, what);
                Assert.Equal(ColourRole.AircraftMoving, p.Colour);
                DrawPrimitive dep = Motion.Aircraft(all, 804UL, what);
                Assert.True(p.Facing.X == dep.Facing.X && p.Facing.Y == dep.Facing.Y, what + ": the arrival faces " + Prims.Show(p) + ", the departure " + Prims.Show(dep));
                Assert.True(p.A.X <= lastX, what + ": the arrival moved back");
                lastX = p.A.X;
                if (tau < 205)
                {
                    Assert.True(p.Elevation < lastElevation, what + ": elevation did not decrease over the final");
                    lastElevation = p.Elevation;
                }
                else
                {
                    Assert.True(p.Elevation == 0f, what + ": elevation after touchdown " + p.Elevation.ToString("R", CultureInfo.InvariantCulture));
                }

                // O ≤ 0 (equal, and a negative difference): at X at once.
                Motion.Near(-1850, 0, Motion.Aircraft(all, 802UL, what).A, what + ": O = 0");
                Motion.Near(-1850, 0, Motion.Aircraft(all, 803UL, what).A, what + ": O < 0");
            }

            Assert.Empty(m.Guard.Violations);

            // With ExitNode = ThresholdNode (12 §12.13's fixture), X = T = (0, 0):
            // the rollout runs back a short way from TD to the threshold.
            RunwayFrame back = MotionScene.Runway1(0, 0);
            MotionScene p1 = MotionScene.Phase1();
            p1.Airside.SetTracks(Tracks.Make(811UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: Entered, dueAt: Due));
            ISceneBuilder pb = p1.Builder();
            Motion.Near(750, 0, Motion.Aircraft(p1.At(pb, 203UL, 50000), 811UL, "no exit, w 1/4").A, "no exit, w 1/4: the same final");
            Motion.Near(-62.5, 0, Motion.Aircraft(p1.At(pb, 208UL, 50000), 811UL, "no exit, w 3/4").A, "no exit, w 3/4: TD + (T − TD) × 3/4");
            DrawPrimitive home = Motion.Aircraft(p1.At(pb, 211UL, 0), 811UL, "no exit, w 1");
            Motion.Near(0, 0, home.A, "no exit, w 1: the threshold");
            Art.AssertFacing(-2000f, 0f, home, "no exit: fDep");
            foreach ((ulong tick, long sub) in new[] { (201UL, 0L), (204UL, 75000L), (206UL, 25000L), (209UL, 0L), (210UL, 99999L) })
            {
                double tau = Motion.Tau(tick, sub);
                back.Landing(tau, Entered, Due).AssertOn(Motion.Aircraft(p1.At(pb, tick, sub), 811UL, "no exit"), "no exit, τ " + tau.ToString("R", CultureInfo.InvariantCulture));
            }

            Assert.Empty(p1.Guard.Violations);
        }

        [Fact]
        public void test_scene_departure_rolls_lifts_off_and_climbs()
        {
            RunwayFrame fr = MotionScene.Runway1(-1850, 0);
            MotionScene m = MotionScene.Playtest();
            const ulong Entered = 300UL;
            const ulong Due = 310UL;
            m.Airside.SetTracks(Tracks.Make(901UL, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: Entered, dueAt: Due));
            ISceneBuilder b = m.Builder();

            // w = 0 at T, w = 1/2 at LO with elevation 0, w = 1 at CE with elevation 400.
            DrawPrimitive t = Motion.Aircraft(m.At(b, 301UL, 0), 901UL, "w 0");
            Motion.Near(0, 0, t.A, "w 0: the threshold");
            Motion.NearElevation(0, t, "w 0");
            Motion.Near(-375, 0, Motion.Aircraft(m.At(b, 303UL, 50000), 901UL, "w 1/4").A, "w 1/4: T + (LO − T) × 1/4");
            DrawPrimitive lo = Motion.Aircraft(m.At(b, 306UL, 0), 901UL, "w 1/2");
            Motion.Near(-1500, 0, lo.A, "w 1/2: lift-off");
            Motion.NearElevation(0, lo, "w 1/2");
            DrawPrimitive mid = Motion.Aircraft(m.At(b, 308UL, 50000), 901UL, "w 3/4");
            Motion.Near(-3500, 0, mid.A, "w 3/4: LO + u × 2000");
            Motion.NearElevation(200, mid, "w 3/4");
            DrawPrimitive ce = Motion.Aircraft(m.At(b, 311UL, 0), 901UL, "w 1");
            Motion.Near(-5500, 0, ce.A, "w 1: the climb end");
            Motion.NearElevation(400, ce, "w 1");
            Art.AssertFacing(-2000f, 0f, ce, "fDep");
            Motion.Near(-5500, 0, Motion.Aircraft(m.At(b, 400UL, 50000), 901UL, "w past 1").A, "w clamps at 1");

            double lastX = double.PositiveInfinity;
            double lastElevation = 0;
            for (long step = 0; step <= 40; step++)
            {
                ulong tick = 301UL + (ulong)(step / 4);
                long sub = (step % 4) * 25000L;
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                DrawPrimitive p = Motion.Aircraft(m.At(b, tick, sub), 901UL, what);
                fr.Takeoff(tau, Entered, Due).AssertOn(p, what);
                Assert.Equal(ColourRole.AircraftMoving, p.Colour);
                Assert.True(p.A.X <= lastX, what + ": the departure moved back");
                lastX = p.A.X;
                if (tau <= 305)
                {
                    Assert.True(p.Elevation == 0f, what + ": airborne before lift-off");
                }
                else
                {
                    Assert.True(p.Elevation > lastElevation, what + ": not climbing");
                    lastElevation = p.Elevation;
                }
            }

            Assert.Empty(m.Guard.Violations);

            // The roll starts at T, the threshold node, which need not be the
            // runway's end N: here T = (−30, 0), so N = (0, 0).
            var a = new ArtScene();
            a.Node(1, -30, 0, TaxiNodeKind.RunwayThreshold).Node(2, 300, 0).Runway(1, 1, 270, -2000, 0, 0, 0, 45).Done();
            a.Airside!.SetTracks(Tracks.Make(902UL, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: Entered, dueAt: Due));
            var offset = new RunwayFrame(-2000, 0, 0, 0, -30, 0, -30, 0);
            ISceneBuilder ab = a.Builder();
            foreach ((ulong tick, long sub) in new[] { (301UL, 0L), (303UL, 50000L), (306UL, 0L), (308UL, 50000L), (311UL, 0L) })
            {
                a.Host.Tick = tick;
                double tau = Motion.Tau(tick, sub);
                string what = "T (−30, 0), τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                offset.Takeoff(tau, Entered, Due).AssertOn(Motion.Aircraft(Prims.Copy(ab.Build(ArtScene.Overview, Gfx.High(), sub)), 902UL, what), what);
            }

            a.Host.Tick = 301UL;
            Motion.Near(-30, 0, Motion.Aircraft(Prims.Copy(ab.Build(ArtScene.Overview, Gfx.High(), 0)), 902UL, "w 0").A, "w 0 at T, not N");
            Assert.Empty(a.Guard.Violations);
        }

        [Fact]
        public void test_scene_off_graph_aircraft_without_a_runway_frame_is_not_drawn()
        {
            // The four rows at τ 100: an approach inside its window, a hold, a
            // final and a takeoff roll. Flight 99 is on the graph, so every frame
            // is built.
            AircraftTrack[] Tracks4(ushort? runway)
            {
                return new[]
                {
                    Tracks.Make(91UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: 105UL),
                    Tracks.Make(92UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival, runway: runway, phaseEnteredAt: 50UL),
                    Tracks.Make(93UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: runway, phaseEnteredAt: 98UL, dueAt: 108UL),
                    Tracks.Make(94UL, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: runway, phaseEnteredAt: 98UL, dueAt: 108UL),
                    Tracks.Make(99UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2),
                };
            }

            // A scene with runway 1 from (−2000,0) to (0,0), threshold node 1 at
            // (0,0) and exit node 4 at (−1850,0), unless the case removes one.
            ArtScene Make(bool def = true, bool geometry = true, bool threshold = true, bool exit = true, bool zeroLength = false)
            {
                var a = new ArtScene();
                if (threshold)
                {
                    a.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold);
                }
                else
                {
                    a.Unpositioned(1, TaxiNodeKind.RunwayThreshold);
                }

                if (exit)
                {
                    a.Node(4, -1850, 0);
                }
                else
                {
                    a.Unpositioned(4, TaxiNodeKind.Junction);
                }

                a.Node(2, 300, 0);
                if (def)
                {
                    a.RunwayWithExit(1, 1, 4, 0, 0, 0, 0, 45, geometry: false);
                }

                if (geometry)
                {
                    if (zeroLength)
                    {
                        a.Geometry(1, -500, 0, -500, 0, 45);
                    }
                    else
                    {
                        a.Geometry(1, -2000, 0, 0, 0, 45);
                    }
                }

                return a.Done();
            }

            List<DrawPrimitive> Frame(ArtScene a, ushort? runway)
            {
                a.Airside!.SetTracks(Tracks4(runway));
                a.Host.Tick = 101UL;
                return Prims.Copy(a.Builder().Build(ArtScene.Overview, Gfx.High(), 0));
            }

            // Control: with the whole frame, all four rows are drawn.
            ArtScene ok = Make();
            List<DrawPrimitive> all = Frame(ok, 1);
            foreach (ulong f in new[] { 91UL, 92UL, 93UL, 94UL, 99UL })
            {
                Motion.Aircraft(all, f, "the complete frame");
            }

            (string What, ArtScene Scene, ushort? Runway)[] cases =
            {
                ("an unset Runway, and no runway to predict", Make(def: false, geometry: false), null),
                ("an unset Runway beside a complete runway 1", Make(), null),
                ("missing geometry", Make(geometry: false), 1),
                ("a missing RunwayDef", Make(def: false), 1),
                ("a missing threshold position", Make(threshold: false), 1),
                ("a missing exit position", Make(exit: false), 1),
                ("Lr = 0", Make(zeroLength: true), 1),
            };
            foreach ((string what, ArtScene a, ushort? runway) in cases)
            {
                List<DrawPrimitive> got = Frame(a, runway);
                Motion.Aircraft(got, 99UL, what + ": the on-graph aircraft");
                foreach (ulong f in new[] { 92UL, 93UL, 94UL })
                {
                    Motion.NotDrawn(got, f, what);
                }

                // The approach row predicts its runway whatever the tracks say, so
                // only the cases that break the predicted runway's frame hide it.
                if (what != "an unset Runway beside a complete runway 1")
                {
                    Motion.NotDrawn(got, 91UL, what + ", the approach row");
                }
                else
                {
                    Motion.Aircraft(got, 91UL, what + ": the approach predicts runway 1");
                }

                Assert.True(a.Guard.Violations.Count == 0, what + ": " + string.Join(", ", a.Guard.Violations));
            }

            // With the whole frame, an off-graph track whose phase and kind match
            // no row is not drawn either (15 §15.20: a Departed track, for one).
            ok.Airside!.SetTracks(
                Tracks.Make(81UL, AircraftLegPhase.Departed, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: 98UL, dueAt: 108UL),
                Tracks.Make(82UL, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Departure, runway: 1, dueAt: 105UL),
                Tracks.Make(83UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: 50UL),
                Tracks.Make(84UL, AircraftLegPhase.HeldOnTaxiway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 50UL),
                Tracks.Make(85UL, AircraftLegPhase.Taxiing, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 98UL, dueAt: 108UL),
                Tracks.Make(86UL, AircraftLegPhase.OnStand, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 50UL),
                Tracks.Make(87UL, AircraftLegPhase.AwaitingPushbackClearance, kind: MovementKind.Departure, runway: 1, phaseEnteredAt: 50UL),
                Tracks.Make(99UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2));
            ok.Host.Tick = 101UL;
            List<DrawPrimitive> rowless = Prims.Copy(ok.Builder().Build(ArtScene.Overview, Gfx.High(), 50000));
            Assert.True(Prims.InLayer(rowless, DrawLayer.Aircraft).Count == 1, "only the on-graph aircraft 99 is drawn: " + Prims.Show(Prims.InLayer(rowless, DrawLayer.Aircraft)));
            Motion.Aircraft(rowless, 99UL, "no matching row");
            Assert.Empty(ok.Guard.Violations);

            // The predicted runway is the one that must have the frame: with
            // runway 2 (no geometry) less queued than runway 1, nothing is drawn.
            var two = new ArtScene();
            two.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold).Node(5, 0, 1000, TaxiNodeKind.RunwayThreshold).Node(2, 300, 0);
            two.Runway(1, 1, 270, -2000, 0, 0, 0, 45).RunwayDefOnly(2, 5, 270).Done();
            two.Airside!.SetQueue(1, 3);
            two.Airside.SetQueue(2, 0);
            List<DrawPrimitive> predicted = Frame(two, 1);
            Motion.NotDrawn(predicted, 91UL, "the predicted runway 2 has no geometry");
            Motion.Aircraft(predicted, 92UL, "runway 1's hold");
            two.Airside.SetQueue(2, 4);
            Motion.Aircraft(Frame(two, 1), 91UL, "runway 1 predicted once it is the less queued");
        }

        [Fact]
        public void test_scene_elevation_is_zero_except_airborne_aircraft()
        {
            var m = new MaxTierScene(motion: true);
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = m.Builder();
            foreach (CameraView cam in new[] { MaxTierScene.Camera, MaxTierScene.PierCamera })
            {
                c.Update(cam, Gfx.High());
                foreach (long sub in new[] { 0L, 25000L, 50000L, TickMicros - 1L })
                {
                    List<DrawPrimitive> all = Build(b, cam, Gfx.High(), sub);
                    string what = "sub-tick " + sub.ToString(CultureInfo.InvariantCulture);
                    Assert.True(Prims.InLayer(all, DrawLayer.Aircraft).Count == m.DrawnAircraft, what + ": " + Prims.InLayer(all, DrawLayer.Aircraft).Count + " aircraft drawn, expected " + m.DrawnAircraft);
                    int airborne = 0;
                    foreach (DrawPrimitive p in all)
                    {
                        if (p.Source.Kind == SourceKind.Aircraft && MaxTierScene.AirborneAtTickOne(p.Source.Id))
                        {
                            Assert.True(p.Elevation > 0f, what + ": airborne aircraft on the ground: " + Prims.Show(p));
                            airborne++;
                        }
                        else
                        {
                            Assert.True(p.Elevation == 0f, what + ": Elevation on a primitive that is not airborne: " + Prims.Show(p));
                        }
                    }

                    Assert.Equal(MaxTierScene.Approaching + MaxTierScene.Held + 2, airborne);
                }
            }

            Assert.True(m.Guard.Violations.Count == 0, string.Join(", ", m.Guard.Violations));
        }
    }
}
