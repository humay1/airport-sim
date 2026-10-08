using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.21 (Q-132): agents placed on a corridor's walkway by their
    /// cohort's progress, and boarding walkers on jet bridges, against fake
    /// sources. Centres are compared within 15 §15.23's 0.01 world units;
    /// counts, sources, facings and paint exactly.
    /// </summary>
    public sealed class SceneWalkerTests
    {
        /// <summary>15 §15.16's passenger paint over a hash input (cohort or flight value, index or j).</summary>
        private static Paint PassengerPaint(in RenderLooks looks, ulong first, int second)
        {
            IReadOnlyList<Rgb>[] lists = { looks.Tops, looks.Bottoms, looks.Skins, looks.Hairs, looks.Bags };
            var regions = new Rgb[5];
            for (int r = 0; r < 5; r++)
            {
                uint h = Fnv.Passenger(first, second, r);
                regions[r] = lists[r][(int)(h % (uint)lists[r].Count)];
            }

            return new Paint(regions[0], regions[1], regions[2], regions[3], regions[4], 0);
        }

        // ---- walkways ------------------------------------------------------

        // Corridor 4's box is (−10,−10)–(40,50), and its walkway runs from
        // (0,0) to (30,40), width 10: Lw = 50, d = (0.6, 0.8), n = (−0.8, 0.6).
        // Box 5, (100,0)–(160,40), has no walkway.
        private const uint Corridor = 4;
        private const uint Plain = 5;
        private const int AgentSize = 2;

        private static readonly PassengerRef[] CorridorAgents =
        {
            new PassengerRef(new CohortId(1UL), 0),
            new PassengerRef(new CohortId(1UL), 1),
            new PassengerRef(new CohortId(1UL), 2),
            new PassengerRef(new CohortId(2UL), 0),
            new PassengerRef(new CohortId(2UL), 5),
            new PassengerRef(new CohortId(3UL), 1),
            new PassengerRef(new CohortId(7UL), 3),
        };

        private static readonly PassengerRef[] PlainAgents =
        {
            new PassengerRef(new CohortId(1UL), 0),
            new PassengerRef(new CohortId(1UL), 1),
            new PassengerRef(new CohortId(4UL), 0),
        };

        private static CameraView WalkwayCamera => Cam.At(75f, 20f, 110f, 2f);

        private static ArtScene WalkwayScene(bool walkway)
        {
            var s = new ArtScene { AgentSize = AgentSize };
            s.Box(Corridor, -10, -10, 40, 50, 100).Box(Plain, 100, 0, 160, 40, 100);
            if (walkway)
            {
                s.Walkway(Corridor, 0, 0, 30, 40, 10);
            }

            s.Done();
            s.Flow.SetAgents(Corridor, CorridorAgents);
            s.Flow.SetAgents(Plain, PlainAgents);
            s.Flow.SetPromoted(new NodeId(Corridor), true);
            s.Flow.SetPromoted(new NodeId(Plain), true);

            // Cohort 1 walks from tick 10 to 30; cohort 2 has DueAt = EnteredNodeAt
            // and cohort 3 DueAt < EnteredNodeAt (p = 1); 7 is unknown (p = 0).
            s.Flow.Cohort(1UL, 10UL, 30UL).Cohort(2UL, 20UL, 20UL).Cohort(3UL, 25UL, 15UL).Cohort(4UL, 0UL, 5UL);
            return s;
        }

        /// <summary>15 §15.21's placement of one agent at τ, with the cohort's progress p.</summary>
        private static P2 Placement(in PassengerRef r, double p)
        {
            var a = new P2(0, 0);
            var b = new P2(30, 40);
            double lw = (b - a).Length;
            var d = new P2((b.X - a.X) / lw, (b.Y - a.Y) / lw);
            var n = new P2(-d.Y, d.X);
            const int Width = 10;
            uint h = Fnv.Passenger(r.Cohort.Value, r.Index, 5);
            double e = (((h % 1024U) / 1023.0) - 0.5) * Math.Max(0, Width - AgentSize);
            double trail = (((h >> 10) % 1024U) / 1023.0) * Math.Min(lw, 4 * Width);
            double s = Math.Max(0, Math.Min(lw, (p * lw) - trail));
            return a + (d * s) + (n * e);
        }

        private static double Progress(ulong cohort, double tau)
        {
            switch (cohort)
            {
                case 1UL: return Motion.Clamp01((tau - 10) / (30.0 - 10.0));
                case 2UL: return 1;
                case 3UL: return 1;
                default: return 0;
            }
        }

        [Fact]
        public void test_scene_walkway_agents_follow_their_cohort_progress()
        {
            ArtScene s = WalkwayScene(walkway: true);
            RenderLooks looks = ArtLooks.WithAirlines(4);
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(s.Sources(), s.Layout, looks);
            CameraView cam = WalkwayCamera;

            var lastS = new double[CorridorAgents.Length];
            for (int i = 0; i < lastS.Length; i++)
            {
                lastS[i] = double.NegativeInfinity;
            }

            // τ from 5 (before cohort 1 enters) to 39 (past its DueAt), every half tick.
            for (long step = 0; step <= 68; step++)
            {
                ulong tick = 6UL + (ulong)(step / 2);
                long sub = (step % 2) * 50000L;
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                s.Host.Tick = tick;
                int asked = s.Flow.CohortsAsked.Count;
                List<DrawPrimitive> all = Prims.Copy(b.Build(cam, Gfx.High(), sub));

                // One TryGetCohort per distinct cohort of the walkway node's drawn
                // agents, in list order; none for the box without a walkway.
                Assert.Equal(new ulong[] { 1UL, 2UL, 3UL, 7UL }, s.Flow.CohortsAsked.Skip(asked).ToArray());

                List<DrawPrimitive> agents = Prims.Of(all, SourceKind.Agent, Corridor);
                Assert.True(agents.Count == CorridorAgents.Length, what + ": " + agents.Count + " walkway agents");
                for (int k = 0; k < CorridorAgents.Length; k++)
                {
                    DrawPrimitive p = Art.Single(all, SourceKind.Agent, Corridor, k);
                    PassengerRef r = CorridorAgents[k];
                    string who = what + ", rank " + k + " (cohort " + r.Cohort.Value + ", index " + r.Index + ")";
                    P2 want = Placement(r, Progress(r.Cohort.Value, tau));
                    Motion.Near(want.X, want.Y, p.A, who);

                    // Inside the walkway's rectangle: along d in [0, Lw], across within Width / 2.
                    double along = (p.A.X * 0.6) + (p.A.Y * 0.8);
                    double across = (-p.A.X * 0.8) + (p.A.Y * 0.6);
                    Assert.True(along >= -Motion.Tolerance && along <= 50 + Motion.Tolerance && Math.Abs(across) <= 5 + Motion.Tolerance, who + ": outside the walkway, along " + along + ", across " + across);

                    // s never decreases as τ grows.
                    Assert.True(along >= lastS[k] - Motion.Tolerance, who + ": went back along the walkway from " + lastS[k] + " to " + along);
                    lastS[k] = along;

                    Assert.Equal(PrimitiveKind.Dot, p.Kind);
                    Assert.Equal(DrawLayer.Agent, p.Layer);
                    Assert.Equal(ColourRole.Agent, p.Colour);
                    Assert.Equal(VisualId.Passenger, p.Visual);
                    Assert.Equal((float)AgentSize, p.Size);
                    Assert.Equal(0f, p.Elevation);
                    Art.AssertFacing(30f, 40f, p, who + ": the walkway's direction");
                    Art.AssertPaint(PassengerPaint(looks, r.Cohort.Value, r.Index), p, who);
                }
            }

            // Cohort 1 at τ 20 is half way: the leader's s is 25 less its trail.
            s.Host.Tick = 21UL;
            List<DrawPrimitive> half = Prims.Copy(b.Build(cam, Gfx.High(), 0));
            P2 mid = Placement(CorridorAgents[0], 0.5);
            Motion.Near(mid.X, mid.Y, Art.Single(half, SourceKind.Agent, Corridor, 0).A, "τ 20, p 1/2");

            // The box without a walkway is unchanged: the same primitives as in a
            // layout with no walkway at all, facing (0, 0).
            ArtScene plain = WalkwayScene(walkway: false);
            plain.Host.Tick = 21UL;
            List<DrawPrimitive> reference = Prims.Copy(RenderFactory.CreateSceneBuilder(plain.Sources(), plain.Layout, looks).Build(cam, Gfx.High(), 0));
            Assert.Equal(Prims.Lines(Prims.Of(reference, SourceKind.Agent, Plain)), Prims.Lines(Prims.Of(half, SourceKind.Agent, Plain)));
            foreach (DrawPrimitive p in Prims.Of(half, SourceKind.Agent, Plain))
            {
                Art.AssertFacing(0f, 0f, p, "an agent in a box");
                Assert.True(Prims.Inside(p, new FlowNodeBox(new NodeId(Plain), 100, 0, 160, 40, 100)), "outside its box: " + Prims.Show(p));
            }

            Assert.Empty(plain.Flow.CohortsAsked);

            // The cap truncates the list first, so only the drawn agents' cohorts are read.
            s.Host.Tick = 22UL;
            int before = s.Flow.CohortsAsked.Count;
            List<DrawPrimitive> capped = Prims.Copy(b.Build(cam, Gfx.Custom(true, 2), 0));
            Assert.Equal(2, Prims.Of(capped, SourceKind.Agent, Corridor).Count);
            Assert.Equal(new ulong[] { 1UL }, s.Flow.CohortsAsked.Skip(before).ToArray());
            for (int k = 0; k < 2; k++)
            {
                P2 want = Placement(CorridorAgents[k], Progress(1UL, 21.0));
                Motion.Near(want.X, want.Y, Art.Single(capped, SourceKind.Agent, Corridor, k).A, "capped, rank " + k);
            }

            // DrawAgents false: no agent, and no cohort read.
            before = s.Flow.CohortsAsked.Count;
            List<DrawPrimitive> low = Prims.Copy(b.Build(cam, RenderFactory.GraphicsForPreset(GraphicsPreset.Low), 0));
            Assert.Empty(Prims.InLayer(low, DrawLayer.Agent));
            Assert.Equal(before, s.Flow.CohortsAsked.Count);

            string? order = Prims.OrderViolation(half);
            Assert.True(order == null, order);
            Assert.True(s.Guard.Violations.Count == 0, string.Join(", ", s.Guard.Violations));
        }

        // ---- jet bridges ---------------------------------------------------

        // Stands 1, 2, 3 at nodes 11 (100,100), 12 (200,100), 13 (300,100).
        // Bridge 1 serves stand 1, (100,160)→(100,120); bridge 2 stand 2,
        // (200,160)→(230,120); bridge 3 stand 3, (300,160)→(300,120); bridge 4
        // serves no stand; bridge 5 names stand 9, which the airside lacks.
        // Stand 1 holds departure 501, stand 2 arrival 502 (rotation 602),
        // stand 3 the rotation-less arrival 503.
        private const ulong Departure = 501UL;
        private const ulong Arrival = 502UL;
        private const ulong Rotation = 602UL;
        private const ulong RotationLess = 503UL;

        private sealed class BridgeScene
        {
            public readonly ArtScene Scene;
            public readonly FakeSchedule Schedule;
            public readonly RenderLooks Looks = ArtLooks.WithAirlines(4);

            public BridgeScene()
            {
                Scene = new ArtScene { AgentSize = AgentSize };
                Scene.Node(2, 200, 0)
                    .Node(11, 100, 100, TaxiNodeKind.StandPosition)
                    .Node(12, 200, 100, TaxiNodeKind.StandPosition)
                    .Node(13, 300, 100, TaxiNodeKind.StandPosition)
                    .Edge(1, 2, 11).Edge(2, 2, 12).Edge(3, 2, 13)
                    .Stand(1, 11).Stand(2, 12).Stand(3, 13)
                    .Bridge(1U, 100, 160, 100, 120, 3, 1)
                    .Bridge(2U, 200, 160, 230, 120, 3, 2)
                    .Bridge(3U, 300, 160, 300, 120, 3, 3)
                    .Bridge(4U, 400, 160, 400, 120, 3)
                    .Bridge(5U, 420, 160, 420, 120, 3, 9)
                    .Done();
                Scene.Airside!.Occupy(1, Departure);
                Scene.Airside.Occupy(2, Arrival);
                Scene.Airside.Occupy(3, RotationLess);
                SetTracks();
                Schedule = new FakeSchedule(Scene.Guard).AddArrival(Arrival, Rotation).AddArrival(RotationLess, null).Add(Departure, 1U, "t2");
                Scene.Flow.Boarding(Departure, 5).Boarding(Rotation, 20, 7).Boarding(RotationLess, 9).Boarding(Arrival, 9);
            }

            public void SetTracks(AircraftLegPhase departurePhase = AircraftLegPhase.OnStand)
            {
                Scene.Airside!.SetTracks(
                    Tracks.Make(Departure, departurePhase, atNode: 11, stand: 1),
                    Tracks.Make(Arrival, AircraftLegPhase.OnStand, atNode: 12, stand: 2, kind: MovementKind.Arrival),
                    Tracks.Make(RotationLess, AircraftLegPhase.OnStand, atNode: 13, stand: 3, kind: MovementKind.Arrival));
            }

            /// <summary>View [50, 450] × [80, 180], below the zoom threshold: every bridge.</summary>
            public static CameraView Camera => Cam.At(250f, 130f, 100f, 4f);

            public ISceneBuilder Builder(bool airside = true, bool flow = true, bool schedule = true)
            {
                return RenderFactory.CreateSceneBuilder(Scene.Sources(airside, flow, schedule ? Schedule : null, null), Scene.Layout, Looks);
            }

            public List<DrawPrimitive> Frame(ISceneBuilder b, in CameraView camera, in GraphicsSettings graphics, ulong tick, long sub)
            {
                Scene.Host.Tick = tick;
                return Prims.Copy(b.Build(camera, graphics, sub));
            }
        }

        /// <summary>15 §15.21's walkers of one bridge at τ, checked one by one.</summary>
        private static void AssertWalkers(List<DrawPrimitive> all, uint bridge, int x0, int y0, int x1, int y1, ulong boarding, int nw, double tau, in RenderLooks looks, string what)
        {
            List<DrawPrimitive> found = Prims.Of(all, SourceKind.BridgePassenger, bridge);
            Assert.True(found.Count == nw, what + ": bridge " + bridge + " has " + found.Count + " walkers, expected " + nw + ": " + Prims.Show(found));
            for (int k = 0; k < nw; k++)
            {
                DrawPrimitive p = Art.Single(all, SourceKind.BridgePassenger, bridge, k);
                string who = what + ", bridge " + bridge + ", walker " + k;
                double c = (tau / Motion.BridgeWalkTicks) + (k / (double)nw);
                double m = Math.Floor(c);
                Motion.Near(x0 + ((x1 - x0) * (c - m)), y0 + ((y1 - y0) * (c - m)), p.A, who);
                long mL = (long)Math.Min(m, 9007199254740992.0);
                int j = unchecked((int)((mL * Motion.MaxBridgeWalkers) + k));
                Art.AssertPaint(PassengerPaint(looks, boarding, j), p, who + " (j " + j + ")");
                Assert.Equal(PrimitiveKind.Dot, p.Kind);
                Assert.Equal(DrawLayer.Agent, p.Layer);
                Assert.Equal(ColourRole.Agent, p.Colour);
                Assert.Equal(VisualId.Passenger, p.Visual);
                Assert.Equal((float)AgentSize, p.Size);
                Assert.Equal(0f, p.Elevation);
                Art.AssertFacing(x1 - x0, y1 - y0, p, who);
            }
        }

        private static void AssertNoWalkers(List<DrawPrimitive> all, uint bridge, string what)
        {
            List<DrawPrimitive> found = Prims.Of(all, SourceKind.BridgePassenger, bridge);
            Assert.True(found.Count == 0, what + ": bridge " + bridge + " should have no walkers: " + Prims.Show(found));
        }

        [Fact]
        public void test_scene_bridge_walkers_follow_the_boarding_flight()
        {
            var s = new BridgeScene();
            ISceneBuilder b = s.Builder();
            CameraView cam = BridgeScene.Camera;
            GraphicsSettings high = Gfx.High();

            // A departure on stand boards itself: Bg = 5, so 5 walkers. An arrival
            // with a rotation boards its rotation: Bg = 20 − 7 = 13, so
            // MAX_BRIDGE_WALKERS. A rotation-less arrival, a bridge with no stand
            // and a stand the airside lacks give none. τ = −1 (tick 0) has m = −1.
            foreach ((ulong tick, long sub) in new[] { (0UL, 0L), (7UL, 25000L), (8UL, 0L), (100UL, 99999L), (1UL << 62, 50000L) })
            {
                double tau = Motion.Tau(tick, sub);
                string what = "τ " + tau.ToString("R", CultureInfo.InvariantCulture);
                int asked = s.Scene.Flow.PopulationForFlightAsked.Count;
                List<DrawPrimitive> all = s.Frame(b, cam, high, tick, sub);
                AssertWalkers(all, 1U, 100, 160, 100, 120, Departure, 5, tau, s.Looks, what);
                AssertWalkers(all, 2U, 200, 160, 230, 120, Rotation, Motion.MaxBridgeWalkers, tau, s.Looks, what);
                AssertNoWalkers(all, 3U, what + ", a rotation-less arrival");
                AssertNoWalkers(all, 4U, what + ", a bridge with no stand");
                AssertNoWalkers(all, 5U, what + ", a stand TryGetStand does not know");
                Assert.Equal(13, Prims.Of(all, SourceKind.BridgePassenger, 1U).Count + Prims.Of(all, SourceKind.BridgePassenger, 2U).Count);
                string? order = Prims.OrderViolation(all);
                Assert.True(order == null, what + ": " + order);

                // Departing passengers of the boarding flight, never another direction or flight.
                var boarding = s.Scene.Flow.PopulationForFlightAsked.Skip(asked).ToList();
                Assert.Contains((Departure, FlowDirection.Departing), boarding);
                Assert.Contains((Rotation, FlowDirection.Departing), boarding);
                Assert.True(boarding.All(q => q.Direction == FlowDirection.Departing && (q.Flight == Departure || q.Flight == Rotation)), what + ": PopulationForFlight asked " + string.Join(", ", boarding));
            }

            // The cap: min(Bg, MAX_BRIDGE_WALKERS, MaxDrawnAgentsPerNode).
            List<DrawPrimitive> capped = s.Frame(b, cam, Gfx.Custom(true, 3), 9UL, 0);
            AssertWalkers(capped, 1U, 100, 160, 100, 120, Departure, 3, Motion.Tau(9UL, 0), s.Looks, "cap 3");
            AssertWalkers(capped, 2U, 200, 160, 230, 120, Rotation, 3, Motion.Tau(9UL, 0), s.Looks, "cap 3");

            // Condition 1, each failing alone.
            Assert.Empty(Art.OfKind(Prims.Copy(s.Builder(airside: false).Build(cam, high, 0)), SourceKind.BridgePassenger));
            Assert.Empty(Art.OfKind(Prims.Copy(s.Builder(flow: false).Build(cam, high, 0)), SourceKind.BridgePassenger));
            Assert.Empty(Art.OfKind(s.Frame(b, cam, Gfx.Custom(false, 256), 10UL, 0), SourceKind.BridgePassenger));
            Assert.Empty(Art.OfKind(s.Frame(b, cam, RenderFactory.GraphicsForPreset(GraphicsPreset.Low), 10UL, 0), SourceKind.BridgePassenger));
            Assert.Empty(Art.OfKind(s.Frame(b, Cam.At(250f, 130f, 121f, 4f), high, 10UL, 0), SourceKind.BridgePassenger));
            Assert.Equal(13, Art.OfKind(s.Frame(b, Cam.At(250f, 130f, RenderConst.AgentZoomThreshold, 4f), high, 10UL, 0), SourceKind.BridgePassenger).Count);

            // The view: bridge 1 (x = 100) out of [110, 210] has none; one that
            // touches the view's edge (closed, as 15 §15.7) is in it.
            List<DrawPrimitive> right = s.Frame(b, Cam.At(160f, 140f, 100f, 1f), high, 10UL, 0);
            AssertNoWalkers(right, 1U, "bridge 1 out of view");
            Assert.Equal(Motion.MaxBridgeWalkers, Prims.Of(right, SourceKind.BridgePassenger, 2U).Count);
            List<DrawPrimitive> touching = s.Frame(b, Cam.At(150f, 140f, 100f, 1f), high, 10UL, 0);
            Assert.Equal(5, Prims.Of(touching, SourceKind.BridgePassenger, 1U).Count);
            Assert.Equal(Motion.MaxBridgeWalkers, Prims.Of(touching, SourceKind.BridgePassenger, 2U).Count);

            // Condition 2, the stand test, each failing alone for bridge 1.
            s.Scene.Airside!.Occupy(1, null);
            AssertNoWalkers(s.Frame(b, cam, high, 11UL, 0), 1U, "stand 1 free");
            s.Scene.Airside.Occupy(1, 999UL);
            AssertNoWalkers(s.Frame(b, cam, high, 12UL, 0), 1U, "stand 1's occupant has no track");
            s.Scene.Airside.Occupy(1, Departure);
            s.SetTracks(AircraftLegPhase.AwaitingPushbackClearance);
            AssertNoWalkers(s.Frame(b, cam, high, 13UL, 0), 1U, "the departure is not OnStand");
            s.SetTracks();
            Assert.Equal(5, Prims.Of(s.Frame(b, cam, high, 14UL, 0), SourceKind.BridgePassenger, 1U).Count);

            // Condition 3: an arrival needs the schedule and a rotation.
            List<DrawPrimitive> noSchedule = Prims.Copy(s.Builder(schedule: false).Build(cam, high, 0));
            AssertNoWalkers(noSchedule, 2U, "no Schedule");
            Assert.Equal(5, Prims.Of(noSchedule, SourceKind.BridgePassenger, 1U).Count);
            var unknown = new FakeSchedule(s.Scene.Guard).AddArrival(RotationLess, null);
            List<DrawPrimitive> notFound = Prims.Copy(RenderFactory.CreateSceneBuilder(s.Scene.Sources(true, true, unknown, null), s.Scene.Layout, s.Looks).Build(cam, high, 0));
            AssertNoWalkers(notFound, 2U, "TryGetFlight does not find the arrival");
            Assert.Equal(5, Prims.Of(notFound, SourceKind.BridgePassenger, 1U).Count);

            // Condition 4: Bg = PopulationForFlight − outstanding must be above 0.
            foreach ((int departing, int? outstanding, int walkers) in new[] { (5, (int?)5, 0), (5, 7, 0), (0, null, 0), (5, 4, 1), (3, null, 3), (12, 2, 8) })
            {
                s.Scene.Flow.Boarding(Departure, departing, outstanding);
                List<DrawPrimitive> all = s.Frame(b, cam, high, 20UL + (ulong)departing + (ulong)(outstanding ?? 50), 0);
                string what = "departing " + departing + ", outstanding " + (outstanding.HasValue ? outstanding.Value.ToString(CultureInfo.InvariantCulture) : "none");
                if (walkers == 0)
                {
                    AssertNoWalkers(all, 1U, what);
                }
                else
                {
                    AssertWalkers(all, 1U, 100, 160, 100, 120, Departure, walkers, Motion.Tau(s.Scene.Host.Tick, 0), s.Looks, what);
                }
            }

            Assert.True(s.Scene.Guard.Violations.Count == 0, string.Join(", ", s.Scene.Guard.Violations));
        }
    }
}
