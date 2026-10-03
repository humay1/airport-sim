using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.5 (what is drawn), §15.6 (sim queries and rebuild cadence) and
    /// the §15.14 invariant, against fake sources.
    /// </summary>
    public sealed class SceneTests
    {
        private static List<DrawPrimitive> Build(ISceneBuilder builder, in CameraView camera, in GraphicsSettings graphics)
        {
            RenderFrame frame = builder.Build(camera, graphics);
            return Prims.Copy(frame);
        }

        [Fact]
        public void test_scene_stand_colour_follows_occupancy()
        {
            var s = new SmallScene();
            s.Airside!.Occupy(SmallScene.S1, 42UL);
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(2, Prims.InLayer(all, DrawLayer.Stand).Count);
            DrawPrimitive s1 = Prims.Single(all, SourceKind.Stand, SmallScene.S1);
            DrawPrimitive s2 = Prims.Single(all, SourceKind.Stand, SmallScene.S2);
            Assert.Equal(PrimitiveKind.Box, s1.Kind);
            Assert.Equal(DrawLayer.Stand, s1.Layer);
            Assert.Equal(ColourRole.StandOccupied, s1.Colour);
            Assert.Equal(0, s1.Source.Sub);
            Prims.AssertPoint(90f, 90f, s1.A, "S1 min corner (centred on node 11 at (100,100), side 20)");
            Prims.AssertPoint(110f, 110f, s1.B, "S1 max corner");
            Assert.Equal(ColourRole.StandFree, s2.Colour);
            Prims.AssertPoint(190f, 90f, s2.A, "S2 min corner (centred on node 12 at (200,100), side 20)");
            Prims.AssertPoint(210f, 110f, s2.B, "S2 max corner");

            // The occupant moves: a new tick rebuilds and the colours follow.
            s.Airside.Occupy(SmallScene.S1, null);
            s.Airside.Occupy(SmallScene.S2, 43UL);
            s.Host.Tick++;
            all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(ColourRole.StandFree, Prims.Single(all, SourceKind.Stand, SmallScene.S1).Colour);
            Assert.Equal(ColourRole.StandOccupied, Prims.Single(all, SourceKind.Stand, SmallScene.S2).Colour);
        }

        [Fact]
        public void test_scene_runway_colour_follows_queue_length_and_taxiways_follow_edges()
        {
            var s = new SmallScene();
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            DrawPrimitive rwy = Prims.Single(all, SourceKind.Runway, SmallScene.Runway);
            Assert.Equal(PrimitiveKind.Segment, rwy.Kind);
            Assert.Equal(DrawLayer.Runway, rwy.Layer);
            Assert.Equal(ColourRole.Runway, rwy.Colour);
            Prims.AssertPoint(-300f, 0f, rwy.A, "runway start (X0,Y0)");
            Prims.AssertPoint(0f, 0f, rwy.B, "runway end (X1,Y1)");
            Assert.Equal(40f, rwy.Size);

            List<DrawPrimitive> taxi = Prims.InLayer(all, DrawLayer.Taxiway);
            Assert.Equal(3, taxi.Count);
            (ushort Edge, float X0, float Y0, float X1, float Y1)[] expected =
            {
                (SmallScene.E1, 0f, 0f, 100f, 40f),
                (SmallScene.E2, 100f, 40f, 100f, 100f),
                (SmallScene.E3, 100f, 40f, 200f, 100f),
            };
            foreach ((ushort edge, float x0, float y0, float x1, float y1) in expected)
            {
                DrawPrimitive e = Prims.Single(all, SourceKind.TaxiEdge, edge);
                Assert.Equal(PrimitiveKind.Segment, e.Kind);
                Assert.Equal(DrawLayer.Taxiway, e.Layer);
                Assert.Equal(ColourRole.Taxiway, e.Colour);
                Prims.AssertPoint(x0, y0, e.A, "edge " + edge + " start (From's position)");
                Prims.AssertPoint(x1, y1, e.B, "edge " + edge + " end (To's position)");
                Assert.Equal((float)SmallScene.TaxiwayWidth, e.Size);
            }

            s.Airside!.SetQueue(SmallScene.Runway, 2);
            s.Host.Tick++;
            all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(ColourRole.RunwayQueued, Prims.Single(all, SourceKind.Runway, SmallScene.Runway).Colour);

            s.Airside.SetQueue(SmallScene.Runway, 0);
            s.Host.Tick++;
            all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(ColourRole.Runway, Prims.Single(all, SourceKind.Runway, SmallScene.Runway).Colour);
        }

        [Fact]
        public void test_scene_aircraft_on_edge_interpolates_from_entry_node()
        {
            var s = new SmallScene();

            // E1 runs from node 1 (0,0) to node 2 (100,40). 12 §12.9: AtNode is
            // the node the aircraft entered the edge from, and EdgeProgress runs
            // from 0 there to 1 at the other endpoint, whichever of From/To that is.
            s.Airside!.SetTracks(
                Tracks.Make(101UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Junction, onEdge: SmallScene.E1, progress: Fx.FromRatio(1, 4)),
                Tracks.Make(102UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Threshold, onEdge: SmallScene.E1, progress: Fx.FromRatio(1, 2)),
                Tracks.Make(103UL, AircraftLegPhase.Taxiing, atNode: SmallScene.StandNode2, onEdge: SmallScene.E3, progress: Fx.Zero),
                Tracks.Make(104UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Junction, onEdge: SmallScene.E3, progress: Fx.FromRatio(3, 4)));
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(4, Prims.InLayer(all, DrawLayer.Aircraft).Count);

            DrawPrimitive a101 = Prims.Single(all, SourceKind.Aircraft, 101UL);
            Assert.Equal(PrimitiveKind.Dot, a101.Kind);
            Assert.Equal(DrawLayer.Aircraft, a101.Layer);
            Assert.Equal(ColourRole.AircraftMoving, a101.Colour);
            Assert.Equal((float)SmallScene.AircraftSize, a101.Size);
            Assert.Equal(0, a101.Source.Sub);
            Prims.AssertPoint(75f, 30f, a101.A, "a quarter of the way from node 2 (100,40) back to node 1 (0,0)");
            Prims.AssertPoint(50f, 20f, Prims.Single(all, SourceKind.Aircraft, 102UL).A, "half way from node 1 to node 2");
            Prims.AssertPoint(200f, 100f, Prims.Single(all, SourceKind.Aircraft, 103UL).A, "progress 0 sits on the entry node 12");
            Prims.AssertPoint(175f, 85f, Prims.Single(all, SourceKind.Aircraft, 104UL).A, "three quarters from node 2 (100,40) to node 12 (200,100)");
        }

        [Fact]
        public void test_scene_aircraft_colour_follows_phase()
        {
            var s = new SmallScene();
            s.Airside!.SetTracks(
                Tracks.Make(201UL, AircraftLegPhase.HeldForRunway, atNode: SmallScene.Threshold),
                Tracks.Make(202UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction),
                Tracks.Make(203UL, AircraftLegPhase.OnStand, atNode: SmallScene.StandNode1, stand: SmallScene.S1),
                Tracks.Make(204UL, AircraftLegPhase.AwaitingPushbackClearance, atNode: SmallScene.StandNode2, stand: SmallScene.S2),
                Tracks.Make(205UL, AircraftLegPhase.OnRunway, atNode: SmallScene.Threshold),
                Tracks.Make(206UL, AircraftLegPhase.Taxiing, atNode: SmallScene.Junction, onEdge: SmallScene.E2, progress: Fx.FromRatio(1, 2)),
                Tracks.Make(207UL, AircraftLegPhase.AwaitingApproach, atNode: SmallScene.Threshold),
                Tracks.Make(208UL, AircraftLegPhase.Departed, atNode: SmallScene.Threshold));
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Equal(ColourRole.AircraftHolding, Prims.Single(all, SourceKind.Aircraft, 201UL).Colour);
            Assert.Equal(ColourRole.AircraftHolding, Prims.Single(all, SourceKind.Aircraft, 202UL).Colour);
            Assert.Equal(ColourRole.AircraftOnStand, Prims.Single(all, SourceKind.Aircraft, 203UL).Colour);
            Assert.Equal(ColourRole.AircraftOnStand, Prims.Single(all, SourceKind.Aircraft, 204UL).Colour);
            Assert.Equal(ColourRole.AircraftMoving, Prims.Single(all, SourceKind.Aircraft, 205UL).Colour);
            Assert.Equal(ColourRole.AircraftMoving, Prims.Single(all, SourceKind.Aircraft, 206UL).Colour);
            Prims.AssertPoint(100f, 100f, Prims.Single(all, SourceKind.Aircraft, 203UL).A, "OnStand at its AtNode, node 11");
            Prims.AssertPoint(100f, 40f, Prims.Single(all, SourceKind.Aircraft, 202UL).A, "holding at its AtNode, node 2");

            // 15 §15.5: AwaitingApproach and Departed are not drawn, wherever they are.
            Assert.Empty(Prims.Of(all, SourceKind.Aircraft, 207UL));
            Assert.Empty(Prims.Of(all, SourceKind.Aircraft, 208UL));
            Assert.Equal(6, Prims.InLayer(all, DrawLayer.Aircraft).Count);
        }

        [Fact]
        public void test_scene_aircraft_off_graph_is_not_drawn()
        {
            var s = new SmallScene();

            // 12 §12.9: an arrival before Landed, and any aircraft on the runway,
            // has neither AtNode nor OnEdge.
            s.Airside!.SetTracks(
                Tracks.Make(301UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival),
                Tracks.Make(302UL, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival),
                Tracks.Make(303UL, AircraftLegPhase.OnRunway),
                Tracks.Make(304UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction));
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            List<DrawPrimitive> aircraft = Prims.InLayer(all, DrawLayer.Aircraft);
            Assert.True(aircraft.Count == 1, "only the on-graph aircraft 304 is drawn: " + Prims.Show(aircraft));
            Assert.Equal(304UL, aircraft[0].Source.Id);
            Assert.Equal(SourceKind.Aircraft, aircraft[0].Source.Kind);
        }

        [Fact]
        public void test_scene_queue_fill_scales_with_population_and_clamps()
        {
            var s = new SmallScene();
            ISceneBuilder b = s.Builder();

            // Hall 3: (0,200)-(100,240), FillCapacity 200. Queue 5: (200,200)-(300,260), FillCapacity 100.
            (int Hall, int Queue, float HallMaxX, float QueueMaxX)[] cases =
            {
                (50, 30, 25f, 230f),
                (100, 100, 50f, 300f),
                (200, 150, 100f, 300f),
                (1000, 99, 100f, 299f),
            };

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Empty(Prims.InLayer(all, DrawLayer.QueueFill));
            DrawPrimitive hallBox = Prims.Single(all, SourceKind.FlowNode, SmallScene.Hall);
            Assert.Equal(PrimitiveKind.Box, hallBox.Kind);
            Assert.Equal(DrawLayer.LandsideNode, hallBox.Layer);
            Assert.Equal(ColourRole.LandsideNode, hallBox.Colour);
            Prims.AssertPoint(0f, 200f, hallBox.A, "hall box min corner");
            Prims.AssertPoint(100f, 240f, hallBox.B, "hall box max corner");
            Assert.Equal(3, Prims.InLayer(all, DrawLayer.LandsideNode).Count);

            foreach ((int hall, int queue, float hallMaxX, float queueMaxX) in cases)
            {
                s.Flow!.SetPopulation(SmallScene.Hall, hall);
                s.Flow.SetPopulation(SmallScene.Queue5, queue);
                s.Host.Tick++;
                all = Build(b, SmallScene.Overview, Gfx.High());
                string what = "populations " + hall + "/" + queue;

                DrawPrimitive h = Prims.Single(all, SourceKind.QueueFill, SmallScene.Hall);
                Assert.Equal(PrimitiveKind.Box, h.Kind);
                Assert.Equal(DrawLayer.QueueFill, h.Layer);
                Assert.Equal(ColourRole.QueueFill, h.Colour);
                Prims.AssertPoint(0f, 200f, h.A, what + ": hall fill keeps MinX, MinY");
                Prims.AssertPoint(hallMaxX, 240f, h.B, what + ": hall fill width scales, MaxY kept");

                DrawPrimitive q = Prims.Single(all, SourceKind.QueueFill, SmallScene.Queue5);
                Prims.AssertPoint(200f, 200f, q.A, what + ": queue fill keeps MinX, MinY");
                Prims.AssertPoint(queueMaxX, 260f, q.B, what + ": queue fill width scales, MaxY kept");

                // Queue 6 is empty: no fill (15 §15.5, "if Population > 0").
                Assert.Empty(Prims.Of(all, SourceKind.QueueFill, SmallScene.Queue6));
            }
        }

        [Fact]
        public void test_scene_agents_capped_per_node_and_inside_box()
        {
            var s = new SmallScene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 300);
            s.Flow.SetPopulation(SmallScene.Hall, 40);
            IPromotionController c = s.Controller();
            ISceneBuilder b = s.Builder();

            // Sees only queue 5's box, below the zoom threshold.
            CameraView cam = Cam.On(SmallScene.Queue5Box, 60f);
            c.Update(cam, Gfx.High());
            Assert.True(s.Flow.IsPromoted(SmallScene.Queue5), "queue 5 should be promoted");
            Assert.False(s.Flow.IsPromoted(SmallScene.Hall), "the hall is out of view");

            List<DrawPrimitive> all = Build(b, cam, Gfx.High());
            List<DrawPrimitive> agents = Prims.InLayer(all, DrawLayer.Agent);
            Assert.Equal(RenderConst.MaxDrawnAgentsPerNode, agents.Count);
            var subs = new HashSet<int>();
            FlowNodeBox box = SmallScene.Queue5Box;
            foreach (DrawPrimitive a in agents)
            {
                Assert.Equal(PrimitiveKind.Dot, a.Kind);
                Assert.Equal(ColourRole.Agent, a.Colour);
                Assert.Equal(SourceKind.Agent, a.Source.Kind);
                Assert.Equal((ulong)SmallScene.Queue5, a.Source.Id);
                Assert.Equal((float)SmallScene.AgentSize, a.Size);
                Assert.True(Prims.Inside(a, box), "agent outside its box: " + Prims.Show(a));
                Assert.True(subs.Add(a.Source.Sub), "two agents with rank " + a.Source.Sub);
            }

            // A population under the cap draws one dot per passenger.
            s.Flow.SetPopulation(SmallScene.Queue5, 17);
            s.Host.Tick++;
            all = Build(b, cam, Gfx.High());
            Assert.Equal(17, Prims.InLayer(all, DrawLayer.Agent).Count);
        }

        [Fact]
        public void test_scene_agents_capped_by_graphics_setting()
        {
            var s = new SmallScene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 100);
            IPromotionController c = s.Controller();
            ISceneBuilder b = s.Builder();
            CameraView cam = Cam.On(SmallScene.Queue5Box, 60f);

            (int Population, int Cap, int Expected)[] cases =
            {
                (100, 10, 10),
                (100, 1, 1),
                (100, 256, 100),
                (100, 100, 100),
                (300, 64, 64),
                (300, 256, 256),
                (300, 32, 32),
            };

            foreach ((int population, int cap, int expected) in cases)
            {
                s.Flow.SetPopulation(SmallScene.Queue5, population);
                s.Host.Tick++;
                GraphicsSettings g = Gfx.Custom(true, cap);
                c.Update(cam, g);
                List<DrawPrimitive> agents = Prims.InLayer(Build(b, cam, g), DrawLayer.Agent);
                Assert.True(agents.Count == expected, "population " + population + ", MaxDrawnAgentsPerNode " + cap + ": " + agents.Count + " agent dots, expected " + expected);
                foreach (DrawPrimitive a in agents)
                {
                    Assert.True(Prims.Inside(a, SmallScene.Queue5Box), "agent outside its box: " + Prims.Show(a));
                }
            }
        }

        [Fact]
        public void test_scene_lane_pips_follow_lane_state_and_skip_non_queue_nodes()
        {
            var s = new SmallScene();
            ISceneBuilder b = s.Builder();

            List<DrawPrimitive> all = Build(b, SmallScene.Overview, Gfx.High());
            AssertPips(all, SmallScene.Queue5Box, 5, 2);
            AssertPips(all, SmallScene.Queue6Box, RenderConst.MaxDrawnLanesPerNode, 3);
            Assert.Empty(Prims.Of(all, SourceKind.Lane, SmallScene.Hall));
            Assert.Equal(5 + RenderConst.MaxDrawnLanesPerNode, Prims.InLayer(all, DrawLayer.Lane).Count);

            // A lane opened or closed by a command shows on the next rebuild.
            s.Flow!.Node(SmallScene.Queue5, 0, new LaneState(5, 5));
            s.Flow.Node(SmallScene.Queue6, 0, new LaneState(4, 0));
            s.Host.Tick++;
            all = Build(b, SmallScene.Overview, Gfx.High());
            AssertPips(all, SmallScene.Queue5Box, 5, 5);
            AssertPips(all, SmallScene.Queue6Box, 4, 0);

            // A node TryGetLaneState rejects gets none.
            s.Flow.Node(SmallScene.Queue6, 0, null);
            s.Host.Tick++;
            all = Build(b, SmallScene.Overview, Gfx.High());
            Assert.Empty(Prims.Of(all, SourceKind.Lane, SmallScene.Queue6));
        }

        private static void AssertPips(List<DrawPrimitive> all, in FlowNodeBox box, int count, int open)
        {
            List<DrawPrimitive> pips = Prims.Of(all, SourceKind.Lane, box.Node.Value);
            Assert.True(pips.Count == count, "node " + box.Node.Value + ": " + pips.Count + " pips, expected " + count + ": " + Prims.Show(pips));
            for (int k = 0; k < pips.Count; k++)
            {
                DrawPrimitive p = pips[k];
                Assert.Equal(PrimitiveKind.Dot, p.Kind);
                Assert.Equal(DrawLayer.Lane, p.Layer);
                Assert.Equal((float)SmallScene.AgentSize, p.Size);
                Assert.True(Prims.Inside(p, box), "pip outside its box: " + Prims.Show(p));
                ColourRole want = k < open ? ColourRole.LaneOpen : ColourRole.LaneClosed;
                Assert.True(p.Colour == want, "node " + box.Node.Value + " pip " + k + " of " + count + " with " + open + " open: " + Prims.Show(p));
                if (k > 0)
                {
                    Assert.True(pips[k - 1].Source.Sub < p.Source.Sub, "pip lane indices not ascending: " + Prims.Show(pips));
                }
            }
        }

        [Fact]
        public void test_scene_primitive_order_is_stable()
        {
            // Max tier: every layer populated.
            var m = new MaxTierScene();
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(m.Sources, m.Layout);
            c.Update(MaxTierScene.Camera, Gfx.High());
            List<DrawPrimitive> first = Build(b, MaxTierScene.Camera, Gfx.High());
            foreach (DrawLayer layer in new[] { DrawLayer.Runway, DrawLayer.Taxiway, DrawLayer.Stand, DrawLayer.LandsideNode, DrawLayer.QueueFill, DrawLayer.Lane, DrawLayer.Agent, DrawLayer.Aircraft })
            {
                Assert.True(Prims.InLayer(first, layer).Count > 0, "max-tier scene draws nothing in layer " + layer);
            }

            string? why = Prims.OrderViolation(first);
            Assert.True(why == null, why);

            // A rebuild of the same state, and a fresh builder over it, give the same list.
            m.Host.Tick++;
            List<DrawPrimitive> again = Build(b, MaxTierScene.Camera, Gfx.High());
            Assert.Equal(Prims.Lines(first), Prims.Lines(again));
            List<DrawPrimitive> fresh = Build(RenderFactory.CreateSceneBuilder(m.Sources, m.Layout), MaxTierScene.Camera, Gfx.High());
            Assert.Equal(Prims.Lines(first), Prims.Lines(fresh));

            // The order is the draw order, not the layout's list order.
            var forward = new SmallScene();
            var reversed = new SmallScene(reversedLayoutLists: true);
            foreach (SmallScene s in new[] { forward, reversed })
            {
                s.Airside!.Occupy(SmallScene.S2, 9UL);
                s.Airside.SetTracks(
                    Tracks.Make(12UL, AircraftLegPhase.OnStand, atNode: SmallScene.StandNode2, stand: SmallScene.S2),
                    Tracks.Make(11UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction));
                s.Flow!.SetPopulation(SmallScene.Queue6, 30);
                s.Flow.SetPopulation(SmallScene.Hall, 30);
            }

            List<DrawPrimitive> fwd = Build(forward.Builder(), SmallScene.Overview, Gfx.High());
            List<DrawPrimitive> rev = Build(reversed.Builder(), SmallScene.Overview, Gfx.High());
            why = Prims.OrderViolation(fwd);
            Assert.True(why == null, why);
            Assert.Equal(Prims.Lines(fwd), Prims.Lines(rev));
        }

        [Fact]
        public void test_scene_omits_primitives_of_absent_modules()
        {
            DrawLayer[] airsideLayers = { DrawLayer.Runway, DrawLayer.Taxiway, DrawLayer.Stand, DrawLayer.Aircraft };
            DrawLayer[] landsideLayers = { DrawLayer.LandsideNode, DrawLayer.QueueFill, DrawLayer.Lane, DrawLayer.Agent };

            // No airside: the airside half of the layout is not needed at all.
            var noAirside = new SmallScene(airside: false);
            noAirside.Flow!.SetPopulation(SmallScene.Queue5, 20);
            var landsideOnly = new RenderLayout(
                new List<TaxiNodePosition>(),
                new List<RunwayGeometry>(),
                noAirside.Layout.FlowNodes,
                SmallScene.StandSize,
                SmallScene.AircraftSize,
                SmallScene.AgentSize,
                SmallScene.TaxiwayWidth);
            var sources = new RenderSources(noAirside.Host, null, noAirside.Flow);
            IPromotionController c = RenderFactory.CreatePromotionController(sources, landsideOnly);
            CameraView cam = Cam.On(SmallScene.Queue5Box, 60f);
            c.Update(cam, Gfx.High());
            List<DrawPrimitive> all = Build(RenderFactory.CreateSceneBuilder(sources, landsideOnly), cam, Gfx.High());
            foreach (DrawLayer layer in airsideLayers)
            {
                Assert.Empty(Prims.InLayer(all, layer));
            }

            foreach (DrawLayer layer in landsideLayers)
            {
                Assert.True(Prims.InLayer(all, layer).Count > 0, "without sim.airside, layer " + layer + " should still be drawn");
            }

            // No flow: nothing landside, and the controller has nothing to promote.
            var noFlow = new SmallScene(flow: false);
            noFlow.Airside!.SetTracks(Tracks.Make(5UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction));
            noFlow.Controller().Update(cam, Gfx.High());
            all = Build(noFlow.Builder(), cam, Gfx.High());
            foreach (DrawLayer layer in landsideLayers)
            {
                Assert.Empty(Prims.InLayer(all, layer));
            }

            foreach (DrawLayer layer in airsideLayers)
            {
                Assert.True(Prims.InLayer(all, layer).Count > 0, "without sim.flow, layer " + layer + " should still be drawn");
            }

            // Neither: an empty draw list, still stamped with the tick and camera.
            var neither = new SmallScene(airside: false, flow: false);
            RenderFrame frame = neither.Builder().Build(cam, Gfx.High());
            Assert.Empty(frame.Primitives);
            Assert.Equal(neither.Host.Tick, frame.Tick);
            Assert.Empty(neither.Guard.Violations);
            Assert.Empty(noAirside.Guard.Violations);
            Assert.Empty(noFlow.Guard.Violations);
        }

        [Fact]
        public void test_scene_rebuilds_only_when_tick_or_camera_changes()
        {
            var s = new SmallScene();
            s.Flow!.SetPopulation(SmallScene.Queue5, 12);
            s.Airside!.SetTracks(Tracks.Make(5UL, AircraftLegPhase.HeldOnTaxiway, atNode: SmallScene.Junction));
            ISceneBuilder b = s.Builder();
            CameraView camA = SmallScene.Overview;
            CameraView camB = Cam.At(110f, 120f, 1000f, 1f);
            GraphicsSettings g = Gfx.High();

            RenderFrame f1 = b.Build(camA, g);
            List<string> lines1 = Prims.Lines(f1.Primitives);
            Assert.Equal(s.Host.Tick, f1.Tick);
            Assert.Equal(camA.Centre.X, f1.Camera.Centre.X);
            long reads = s.Host.CurrentTickReads;
            long air = s.Airside.QueryCalls;
            long flow = s.Flow.QueryCalls;
            Assert.True(reads >= 1, "Build reads CurrentTick every call (15 §15.6)");
            Assert.True(air > 0 && flow > 0, "the first Build must query airside and flow");

            // Same tick, camera and graphics: CurrentTick is read, nothing else, and the frame is unchanged.
            RenderFrame f2 = b.Build(camA, g);
            Assert.True(s.Host.CurrentTickReads > reads, "every Build reads CurrentTick");
            Assert.Equal(air, s.Airside.QueryCalls);
            Assert.Equal(flow, s.Flow.QueryCalls);
            Assert.Equal(lines1, Prims.Lines(f2.Primitives));
            Assert.Equal(f1.Tick, f2.Tick);

            // A new tick rebuilds.
            s.Host.Tick++;
            RenderFrame f3 = b.Build(camA, g);
            Assert.True(s.Airside.QueryCalls > air && s.Flow.QueryCalls > flow, "a tick change must rebuild");
            Assert.Equal(s.Host.Tick, f3.Tick);
            air = s.Airside.QueryCalls;
            flow = s.Flow.QueryCalls;
            b.Build(camA, g);
            Assert.Equal(air, s.Airside.QueryCalls);
            Assert.Equal(flow, s.Flow.QueryCalls);

            // A new camera rebuilds.
            RenderFrame f4 = b.Build(camB, g);
            Assert.True(s.Airside.QueryCalls > air && s.Flow.QueryCalls > flow, "a camera change must rebuild");
            Assert.Equal(camB.Centre.X, f4.Camera.Centre.X);
            air = s.Airside.QueryCalls;
            flow = s.Flow.QueryCalls;
            b.Build(camB, g);
            Assert.Equal(air, s.Airside.QueryCalls);
            Assert.Equal(flow, s.Flow.QueryCalls);

            // New graphics settings rebuild (15 §15.6, D10).
            b.Build(camB, Gfx.Custom(true, 8));
            Assert.True(s.Airside.QueryCalls > air && s.Flow.QueryCalls > flow, "a graphics change must rebuild");
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_rebuilds_when_graphics_settings_change()
        {
            var s = new SmallScene();
            ISceneBuilder b = s.Builder();
            CameraView cam = SmallScene.Overview;
            GraphicsSettings[] sequence =
            {
                Gfx.High(),
                Gfx.Custom(true, 256, 60, 100, true),
                Gfx.Custom(true, 256, 60, 75, true),
                Gfx.Custom(true, 256, 60, 75, false),
                Gfx.Custom(false, 256, 60, 75, false),
                Gfx.Custom(false, 32, 60, 75, false),
                new GraphicsSettings(GraphicsPreset.Low, false, 32, 60, 75, false),
            };

            long air = -1;
            long flow = -1;
            foreach (GraphicsSettings g in sequence)
            {
                RenderFrame f = b.Build(cam, g);
                Assert.True(s.Airside!.QueryCalls > air && s.Flow!.QueryCalls > flow, "graphics change to " + Gfx.Show(g) + " must rebuild");
                Assert.True(Gfx.Show(f.Graphics) == Gfx.Show(g), "RenderFrame.Graphics " + Gfx.Show(f.Graphics) + ", expected the settings passed in: " + Gfx.Show(g));
                air = s.Airside.QueryCalls;
                flow = s.Flow.QueryCalls;

                // Unchanged settings: no rebuild, and the frame still carries them.
                f = b.Build(cam, g);
                Assert.Equal(air, s.Airside.QueryCalls);
                Assert.Equal(flow, s.Flow.QueryCalls);
                Assert.Equal(Gfx.Show(g), Gfx.Show(f.Graphics));
            }
        }

        [Fact]
        public void test_scene_calls_only_listed_sim_members()
        {
            var m = new MaxTierScene();

            // The controller calls SetPromoted and nothing else (15 §15.6).
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            Assert.Equal(0L, m.Airside.LayoutCalls);
            Assert.Equal(0L, m.Airside.QueryCalls);
            Assert.Equal(0L, m.Flow.QueryCalls);
            Assert.Equal(0L, m.Host.CurrentTickReads);

            // The builder reads IAirsideSystem.Layout once, at construction.
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(m.Sources, m.Layout);
            Assert.Equal(1L, m.Airside.LayoutCalls);
            Assert.Equal(0L, m.Flow.SetPromotedCount);

            CameraView[] cameras = { MaxTierScene.Camera, Cam.Away(), Cam.At(35f, 35f, 500f), MaxTierScene.Camera };
            GraphicsSettings[] settings = { Gfx.High(), Gfx.Custom(false, 32, 60, 75, false), Gfx.Custom(true, 1, 15, 50, false) };
            for (int frame = 0; frame < 24; frame++)
            {
                CameraView cam = cameras[frame % cameras.Length];
                GraphicsSettings g = settings[frame % settings.Length];

                long air = m.Airside.QueryCalls;
                long flow = m.Flow.QueryCalls;
                long reads = m.Host.CurrentTickReads;
                c.Update(cam, g);
                Assert.Equal(air, m.Airside.QueryCalls);
                Assert.Equal(flow, m.Flow.QueryCalls);
                Assert.Equal(reads, m.Host.CurrentTickReads);

                if (frame % 2 == 0)
                {
                    m.Host.Tick++;
                }

                long promotions = m.Flow.SetPromotedCount;
                b.Build(cam, g);
                Assert.Equal(promotions, m.Flow.SetPromotedCount);
            }

            Assert.Equal(1L, m.Airside.LayoutCalls);
            Assert.True(m.Flow.SetPromotedCount > 0, "the controller never promoted anything");
            Assert.True(m.Guard.Violations.Count == 0, "members outside 15 §15.6 were called: " + string.Join(", ", m.Guard.Violations));
        }

        [Fact]
        public void test_scene_gameplay_primitives_identical_at_every_graphics_setting()
        {
            GraphicsSettings[] settings =
            {
                Gfx.High(),
                RenderFactory.GraphicsForPreset(GraphicsPreset.Low),
                RenderFactory.GraphicsForPreset(GraphicsPreset.Medium),
                RenderFactory.GraphicsForPreset(GraphicsPreset.High),
                Gfx.Custom(false, 1, 15, 50, false),
                Gfx.Custom(true, 1, 15, 50, false),
                Gfx.Custom(true, 256, 240, 100, true),
                Gfx.Custom(false, 256, 0, 100, true),
                Gfx.Custom(true, 64, 0, 50, true),
                Gfx.Custom(true, 255, 15, 99, false),
            };

            // One builder and controller switched through every setting, and a
            // fresh pair per setting: both must give the High non-Agent list.
            var shared = new MaxTierScene();
            IPromotionController sc = RenderFactory.CreatePromotionController(shared.Sources, shared.Layout);
            ISceneBuilder sb = RenderFactory.CreateSceneBuilder(shared.Sources, shared.Layout);
            sc.Update(MaxTierScene.Camera, Gfx.High());
            List<DrawPrimitive> reference = Build(sb, MaxTierScene.Camera, Gfx.High());
            List<string> expected = Prims.Lines(reference, skipAgents: true);
            Assert.Equal(16 * RenderConst.MaxDrawnAgentsPerNode, Prims.InLayer(reference, DrawLayer.Agent).Count);

            foreach (GraphicsSettings g in settings)
            {
                sc.Update(MaxTierScene.Camera, g);
                List<DrawPrimitive> switched = Build(sb, MaxTierScene.Camera, g);

                var fresh = new MaxTierScene();
                IPromotionController fc = RenderFactory.CreatePromotionController(fresh.Sources, fresh.Layout);
                ISceneBuilder fb = RenderFactory.CreateSceneBuilder(fresh.Sources, fresh.Layout);
                fc.Update(MaxTierScene.Camera, g);
                List<DrawPrimitive> alone = Build(fb, MaxTierScene.Camera, g);

                foreach (List<DrawPrimitive> got in new[] { switched, alone })
                {
                    List<string> lines = Prims.Lines(got, skipAgents: true);
                    Assert.True(lines.Count == expected.Count, Gfx.Show(g) + ": " + lines.Count + " non-Agent primitives, High has " + expected.Count);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        Assert.True(lines[i] == expected[i], Gfx.Show(g) + ": non-Agent primitive " + i + " is " + lines[i] + ", at High " + expected[i]);
                    }

                    int agents = Prims.InLayer(got, DrawLayer.Agent).Count;
                    int want = g.DrawAgents ? 16 * g.MaxDrawnAgentsPerNode : 0;
                    Assert.True(agents == want, Gfx.Show(g) + ": " + agents + " agent dots, expected " + want);
                }
            }
        }
    }
}
