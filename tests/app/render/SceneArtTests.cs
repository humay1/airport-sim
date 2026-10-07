using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.5's draw table with Q-130's rows and Visual column, and 15
    /// §15.16's scenery, taxiway and stand marking rules, against fake
    /// sources. Every expected coordinate is worked by hand from §15.16's
    /// integer arithmetic.
    /// </summary>
    public sealed class SceneArtTests
    {
        /// <summary>
        /// Every row of §15.5:
        ///   runway 1 (-300,0)-(0,0) width 40, threshold node 1 (0,0), 270°
        ///   nodes 1 (0,0), 2 (100,40), 11 (100,100), 12 (200,100); edges 1-2, 2-11, 2-12
        ///   stand 1 at node 11 (occupied, flight 101 on it), stand 2 at node 12
        ///   areas: apron 1, terminal 2, pier 3, control tower 4; bridge 1
        ///   hall 3 (0,200)-(100,240) with 10 passengers; queue 5 (200,200)-(300,260)
        ///   with 20 passengers and lanes 5 / 2 open, both promoted in the fake
        /// </summary>
        private static ArtScene DrawTableScene()
        {
            var s = new ArtScene { StandSize = 20, AircraftSize = 10, AgentSize = 2, TaxiwayWidth = 8 };
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold)
                .Node(2, 100, 40)
                .Node(11, 100, 100, TaxiNodeKind.StandPosition)
                .Node(12, 200, 100, TaxiNodeKind.StandPosition)
                .Edge(1, 1, 2)
                .Edge(2, 2, 11)
                .Edge(3, 2, 12)
                .Runway(1, 1, 270, -300, 0, 0, 0, 40)
                .Stand(1, 11)
                .Stand(2, 12)
                .Area(1, AreaKind.Apron, -400, -100, 400, 150)
                .Area(2, AreaKind.Terminal, 0, 180, 600, 300)
                .Area(3, AreaKind.Pier, 90, 120, 210, 130)
                .Area(4, AreaKind.ControlTower, 700, 180, 720, 200)
                .Bridge(1, 100, 120, 100, 108, 3)
                .Box(3, 0, 200, 100, 240, 200, 10)
                .Box(5, 200, 200, 300, 260, 100, 20, new LaneState(5, 2))
                .Done();
            s.Airside!.Occupy(1, 101UL);
            s.Airside.SetTracks(Tracks.Make(101UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1));
            s.Flow.SetPromoted(new NodeId(3), true);
            s.Flow.SetPromoted(new NodeId(5), true);
            return s;
        }

        /// <summary>A camera below the zoom threshold that sees the hall and queue 5.</summary>
        private static CameraView LandsideCamera => Cam.At(150f, 230f, 100f, 3f);

        [Fact]
        public void test_scene_visuals_follow_the_draw_table()
        {
            ArtScene s = DrawTableScene();
            ISceneBuilder b = s.Builder();
            List<DrawPrimitive> all = Prims.Copy(b.Build(LandsideCamera, Gfx.High()));
            RenderLooks d = RenderFactory.DefaultLooks();
            Paint aircraftPaint = ArtLooks.PaintOf(d.DefaultLivery);
            var agentPaint = new Paint(d.Tops[0], d.Bottoms[0], d.Skins[0], d.Hairs[0], d.Bags[0], 0);

            var seen = new HashSet<SourceKind>();
            foreach (DrawPrimitive p in all)
            {
                string what = Prims.Show(p);
                seen.Add(p.Source.Kind);
                PrimitiveKind kind;
                DrawLayer layer;
                VisualId visual;
                var colours = new List<ColourRole>();
                bool subZero = true;
                bool facingSet = false;
                bool painted = false;
                switch (p.Source.Kind)
                {
                    case SourceKind.Apron:
                        (kind, layer, visual) = (PrimitiveKind.Box, DrawLayer.Ground, VisualId.Apron);
                        colours.Add(ColourRole.Apron);
                        break;
                    case SourceKind.Building:
                        (kind, layer) = (PrimitiveKind.Box, DrawLayer.Ground);
                        visual = p.Source.Id == 2UL ? VisualId.TerminalBuilding : p.Source.Id == 3UL ? VisualId.Pier : VisualId.ControlTower;
                        Assert.True(p.Source.Id >= 2UL && p.Source.Id <= 4UL, "a building source names a non-building area: " + what);
                        colours.Add(ColourRole.Building);
                        break;
                    case SourceKind.Runway:
                        (kind, layer, visual) = (PrimitiveKind.Segment, DrawLayer.Runway, VisualId.RunwaySurface);
                        colours.Add(ColourRole.Runway);
                        break;
                    case SourceKind.RunwayMarking:
                        layer = DrawLayer.Runway;
                        colours.Add(ColourRole.RunwayMarking);
                        subZero = false;
                        int sub = p.Source.Sub;
                        if (sub == 0)
                        {
                            (kind, visual) = (PrimitiveKind.Segment, VisualId.RunwayEdgeLines);
                        }
                        else if (sub <= 2)
                        {
                            (kind, visual, facingSet) = (PrimitiveKind.Dot, VisualId.RunwayThreshold, true);
                        }
                        else if (sub <= 6)
                        {
                            // 270° from end 1: end 0 reads 09, end 1 reads 27.
                            int[] digits = { 0, 9, 2, 7 };
                            (kind, visual, facingSet) = (PrimitiveKind.Dot, Art.Digit(digits[sub - 3]), true);
                        }
                        else
                        {
                            (kind, visual) = (PrimitiveKind.Segment, VisualId.RunwayCentreDash);
                        }

                        break;
                    case SourceKind.TaxiEdge:
                        (kind, layer, visual) = (PrimitiveKind.Segment, DrawLayer.Taxiway, VisualId.TaxiwaySurface);
                        colours.Add(ColourRole.Taxiway);
                        break;
                    case SourceKind.TaxiNode:
                        (kind, layer, visual) = (PrimitiveKind.Dot, DrawLayer.Taxiway, VisualId.TaxiwayJunction);
                        colours.Add(ColourRole.Taxiway);
                        break;
                    case SourceKind.TaxiCentreline:
                        (kind, layer, visual) = (PrimitiveKind.Segment, DrawLayer.Taxiway, VisualId.TaxiwayCentreline);
                        colours.Add(ColourRole.TaxiwayMarking);
                        break;
                    case SourceKind.Stand:
                        (kind, layer, visual) = (PrimitiveKind.Box, DrawLayer.Stand, VisualId.StandPad);
                        colours.Add(p.Source.Id == 1UL ? ColourRole.StandOccupied : ColourRole.StandFree);
                        break;
                    case SourceKind.StandMarking:
                        (kind, layer, visual, facingSet) = (PrimitiveKind.Dot, DrawLayer.Stand, VisualId.StandLeadIn, true);
                        colours.Add(ColourRole.TaxiwayMarking);
                        break;
                    case SourceKind.StandNumber:
                        // Stand ids 1 and 2 are one digit each.
                        (kind, layer, visual, facingSet) = (PrimitiveKind.Dot, DrawLayer.Stand, Art.Digit((int)p.Source.Id), true);
                        colours.Add(ColourRole.TaxiwayMarking);
                        break;
                    case SourceKind.JetBridge:
                        (kind, layer, visual) = (PrimitiveKind.Segment, DrawLayer.Stand, VisualId.JetBridge);
                        colours.Add(ColourRole.Building);
                        break;
                    case SourceKind.FlowNode:
                        (kind, layer, visual) = (PrimitiveKind.Box, DrawLayer.LandsideNode, VisualId.TerminalZone);
                        colours.Add(ColourRole.LandsideNode);
                        break;
                    case SourceKind.QueueFill:
                        (kind, layer, visual) = (PrimitiveKind.Box, DrawLayer.QueueFill, VisualId.QueueFill);
                        colours.Add(ColourRole.QueueFill);
                        break;
                    case SourceKind.Lane:
                        (kind, layer, visual) = (PrimitiveKind.Dot, DrawLayer.Lane, VisualId.LanePip);
                        colours.Add(ColourRole.LaneOpen);
                        colours.Add(ColourRole.LaneClosed);
                        subZero = false;
                        break;
                    case SourceKind.Agent:
                        (kind, layer, visual, painted) = (PrimitiveKind.Dot, DrawLayer.Agent, VisualId.Passenger, true);
                        colours.Add(ColourRole.Agent);
                        subZero = false;
                        break;
                    case SourceKind.Aircraft:
                        // No schedule and no content: AircraftC and the default livery (15 §15.9).
                        (kind, layer, visual, facingSet, painted) = (PrimitiveKind.Dot, DrawLayer.Aircraft, VisualId.AircraftC, true, true);
                        colours.Add(ColourRole.AircraftOnStand);
                        break;
                    default:
                        Assert.Fail("unexpected source kind: " + what);
                        return;
                }

                if (p.Source.Kind == SourceKind.StandNumber)
                {
                    subZero = false;
                    Assert.Equal(0, p.Source.Sub);
                }

                Assert.True(p.Kind == kind, "kind " + p.Kind + ", expected " + kind + ": " + what);
                Assert.True(p.Layer == layer, "layer " + p.Layer + ", expected " + layer + ": " + what);
                Assert.True(p.Visual == visual, "visual " + p.Visual + ", expected " + visual + ": " + what);
                Assert.True(colours.Contains(p.Colour), "colour " + p.Colour + " not in the row's roles: " + what);
                if (subZero)
                {
                    Assert.True(p.Source.Sub == 0, "Sub must be 0 for this row: " + what);
                }

                if (!facingSet || p.Kind != PrimitiveKind.Dot)
                {
                    Art.AssertFacing(0f, 0f, p, "§15.16 sets no Facing here");
                }

                if (p.Source.Kind == SourceKind.Aircraft)
                {
                    Art.AssertPaint(aircraftPaint, p, "aircraft without a schedule: the default livery");
                }
                else if (p.Source.Kind == SourceKind.Agent)
                {
                    Art.AssertPaint(agentPaint, p, "agent under the default looks' one-entry lists");
                }
                else
                {
                    Assert.False(painted);
                    Art.AssertZeroPaint(p, "§15.16 sets no Paint here");
                }
            }

            SourceKind[] every =
            {
                SourceKind.Runway, SourceKind.TaxiEdge, SourceKind.Stand, SourceKind.FlowNode, SourceKind.QueueFill, SourceKind.Agent, SourceKind.Aircraft, SourceKind.Lane,
                SourceKind.RunwayMarking, SourceKind.TaxiNode, SourceKind.TaxiCentreline, SourceKind.Apron, SourceKind.Building, SourceKind.StandMarking, SourceKind.StandNumber, SourceKind.JetBridge,
            };
            foreach (SourceKind k in every)
            {
                Assert.True(seen.Contains(k), "the draw-table scene produced no " + k + " primitive: " + Prims.Show(all));
            }

            // Geometry of the new rows that §15.5 gives directly.
            DrawPrimitive apron = Prims.Single(all, SourceKind.Apron, 1UL);
            Prims.AssertPoint(-400f, -100f, apron.A, "apron min corner");
            Prims.AssertPoint(400f, 150f, apron.B, "apron max corner");
            DrawPrimitive tower = Prims.Single(all, SourceKind.Building, 4UL);
            Prims.AssertPoint(700f, 180f, tower.A, "control tower min corner");
            Prims.AssertPoint(720f, 200f, tower.B, "control tower max corner");
            Art.AssertSegment(Prims.Single(all, SourceKind.JetBridge, 1UL), VisualId.JetBridge, 100f, 120f, 100f, 108f, 3f, "bridge 1");
            Assert.Empty(Prims.Of(all, SourceKind.Apron, 2UL));
            Assert.Empty(Prims.Of(all, SourceKind.Building, 1UL));

            // The table's states still read by role: occupancy, lanes, queue.
            Assert.Equal(ColourRole.StandFree, Prims.Single(all, SourceKind.Stand, 2UL).Colour);
            Assert.Equal(20, Prims.Of(all, SourceKind.Agent, 5UL).Count);
            Assert.Equal(10, Prims.Of(all, SourceKind.Agent, 3UL).Count);
            string? why = Prims.OrderViolation(all);
            Assert.True(why == null, why);
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_scenery_follows_the_layout()
        {
            // Ids interleave across kinds, and the layout lists run backwards:
            // the draw order is §15.5's, never the layout's.
            var s = new ArtScene();
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold)
                .Node(11, 300, -150, TaxiNodeKind.StandPosition)
                .Edge(1, 1, 11)
                .Stand(1, 11)
                .Box(1, 0, 200, 80, 260, 100, 5)
                .Area(5, AreaKind.Pier, 280, -205, 780, -180)
                .Area(4, AreaKind.Apron, 20, -180, 900, 190)
                .Area(3, AreaKind.ControlTower, 920, 200, 940, 220)
                .Area(2, AreaKind.Apron, -500, -300, -100, -200)
                .Area(1, AreaKind.Terminal, -10, 190, 890, 275)
                .Bridge(7, 452, -180, 446, -163, 3)
                .Bridge(2, 308, -180, 306, -160, 4)
                .Done();

            (SourceKind Kind, ulong Id, VisualId Visual, float MinX, float MinY, float MaxX, float MaxY)[] areas =
            {
                (SourceKind.Apron, 2UL, VisualId.Apron, -500f, -300f, -100f, -200f),
                (SourceKind.Apron, 4UL, VisualId.Apron, 20f, -180f, 900f, 190f),
                (SourceKind.Building, 1UL, VisualId.TerminalBuilding, -10f, 190f, 890f, 275f),
                (SourceKind.Building, 3UL, VisualId.ControlTower, 920f, 200f, 940f, 220f),
                (SourceKind.Building, 5UL, VisualId.Pier, 280f, -205f, 780f, -180f),
            };

            // No airside and no flow module: the scenery alone, aprons before buildings.
            List<DrawPrimitive> alone = s.Frame(s.Builder(airside: false, flow: false));
            Assert.True(alone.Count == 7, "five areas and two bridges, nothing else: " + Prims.Show(alone));
            for (int i = 0; i < areas.Length; i++)
            {
                (SourceKind kind, ulong id, VisualId visual, float minX, float minY, float maxX, float maxY) = areas[i];
                DrawPrimitive p = alone[i];
                string what = "Ground primitive " + i + ": " + Prims.Show(p);
                Assert.True(p.Source.Kind == kind && p.Source.Id == id && p.Source.Sub == 0, what + ", expected " + kind + ":" + id + ":0");
                Assert.Equal(PrimitiveKind.Box, p.Kind);
                Assert.Equal(DrawLayer.Ground, p.Layer);
                Assert.Equal(kind == SourceKind.Apron ? ColourRole.Apron : ColourRole.Building, p.Colour);
                Assert.Equal(visual, p.Visual);
                Prims.AssertPoint(minX, minY, p.A, what + " min corner");
                Prims.AssertPoint(maxX, maxY, p.B, what + " max corner");
                Art.AssertFacing(0f, 0f, p, what);
                Art.AssertZeroPaint(p, what);
            }

            DrawPrimitive b2 = alone[5];
            DrawPrimitive b7 = alone[6];
            Assert.True(b2.Source.Kind == SourceKind.JetBridge && b2.Source.Id == 2UL && b2.Source.Sub == 0, "first bridge: " + Prims.Show(b2));
            Assert.True(b7.Source.Kind == SourceKind.JetBridge && b7.Source.Id == 7UL && b7.Source.Sub == 0, "second bridge: " + Prims.Show(b7));
            foreach ((DrawPrimitive p, float x0, float y0, float x1, float y1, float w) in new[] { (b2, 308f, -180f, 306f, -160f, 4f), (b7, 452f, -180f, 446f, -163f, 3f) })
            {
                Art.AssertSegment(p, VisualId.JetBridge, x0, y0, x1, y1, w, "bridge " + p.Source.Id);
                Assert.Equal(DrawLayer.Stand, p.Layer);
                Assert.Equal(ColourRole.Building, p.Colour);
                Art.AssertZeroPaint(p, "bridge " + p.Source.Id);
            }

            // The scenery is the same whichever modules are present.
            List<string> scenery = Prims.Lines(alone);
            foreach ((bool airside, bool flow) in new[] { (true, false), (false, true), (true, true) })
            {
                List<DrawPrimitive> all = s.Frame(s.Builder(airside, flow));
                var got = new List<DrawPrimitive>();
                foreach (DrawPrimitive p in all)
                {
                    if (p.Source.Kind == SourceKind.Apron || p.Source.Kind == SourceKind.Building || p.Source.Kind == SourceKind.JetBridge)
                    {
                        got.Add(p);
                    }
                }

                Assert.Equal(scenery, Prims.Lines(got));
                string? why = Prims.OrderViolation(all);
                Assert.True(why == null, why);
                if (airside)
                {
                    // A bridge shares the Stand layer and draws after every stand marking.
                    List<DrawPrimitive> stand = Prims.InLayer(all, DrawLayer.Stand);
                    Assert.Equal(SourceKind.JetBridge, stand[stand.Count - 1].Source.Kind);
                    Assert.Equal(SourceKind.Stand, stand[0].Source.Kind);
                }
            }

            // No scenery in the layout: nothing in Ground.
            var bare = new ArtScene().Done();
            Assert.Empty(bare.Frame(bare.Builder(airside: false, flow: false)));
            Assert.Empty(s.Guard.Violations);
        }

        /// <summary>
        /// nodes 1 (0,0), 2 (100,40), 11 (100,100), 12 (200,100), 13 (300,100),
        /// 14 (400,400), 15 and 16 both at (700,700), 3 (0,300) with no edge;
        /// edges 4: 11-13 (listed first), 1: 1-2, 2: 11-2, 3: 2-12, 6: 15-16.
        /// Incident edge counts: 1→1, 2→3, 11→2, 12→1, 13→1, 14→0, 15→1, 16→1, 3→0.
        /// </summary>
        private static ArtScene TaxiScene()
        {
            var s = new ArtScene { TaxiwayWidth = 8 };
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold)
                .Node(2, 100, 40)
                .Node(3, 0, 300, TaxiNodeKind.RunwayThreshold)
                .Node(11, 100, 100, TaxiNodeKind.StandPosition)
                .Node(12, 200, 100, TaxiNodeKind.StandPosition)
                .Node(13, 300, 100)
                .Node(14, 400, 400, TaxiNodeKind.StandPosition)
                .Node(15, 700, 700, TaxiNodeKind.StandPosition)
                .Node(16, 700, 700)
                .Edge(4, 11, 13)
                .Edge(1, 1, 2)
                .Edge(2, 11, 2)
                .Edge(3, 2, 12)
                .Edge(6, 15, 16);
            return s;
        }

        [Fact]
        public void test_scene_taxiway_junction_fill_and_centrelines()
        {
            ArtScene s = TaxiScene().Done();
            List<DrawPrimitive> all = s.Frame(s.Builder());

            // Fills at nodes with two or more incident edges only: 2 (three) and 11 (two).
            List<DrawPrimitive> fills = Art.OfKind(all, SourceKind.TaxiNode);
            Assert.True(fills.Count == 2, "junction fills: " + Prims.Show(fills));
            foreach ((DrawPrimitive p, ushort node, float x, float y) in new[] { (fills[0], (ushort)2, 100f, 40f), (fills[1], (ushort)11, 100f, 100f) })
            {
                string what = "junction fill at node " + node;
                Assert.Equal((ulong)node, p.Source.Id);
                Assert.Equal(0, p.Source.Sub);
                Art.AssertDot(p, VisualId.TaxiwayJunction, x, y, 8f, what);
                Assert.Equal(DrawLayer.Taxiway, p.Layer);
                Assert.Equal(ColourRole.Taxiway, p.Colour);
                Art.AssertFacing(0f, 0f, p, what);
                Art.AssertZeroPaint(p, what);
            }

            // One centreline per edge, on the edge's geometry (From to To).
            (ushort Edge, float X0, float Y0, float X1, float Y1)[] edges =
            {
                (1, 0f, 0f, 100f, 40f),
                (2, 100f, 100f, 100f, 40f),
                (3, 100f, 40f, 200f, 100f),
                (4, 100f, 100f, 300f, 100f),
                (6, 700f, 700f, 700f, 700f),
            };
            List<DrawPrimitive> lines = Art.OfKind(all, SourceKind.TaxiCentreline);
            Assert.True(lines.Count == edges.Length, "centrelines: " + Prims.Show(lines));
            foreach ((ushort edge, float x0, float y0, float x1, float y1) in edges)
            {
                DrawPrimitive surface = Prims.Single(all, SourceKind.TaxiEdge, edge);
                DrawPrimitive line = Prims.Single(all, SourceKind.TaxiCentreline, edge);
                string what = "edge " + edge;
                Art.AssertSegment(surface, VisualId.TaxiwaySurface, x0, y0, x1, y1, 8f, what + " surface");
                Art.AssertSegment(line, VisualId.TaxiwayCentreline, x0, y0, x1, y1, 8f, what + " centreline");
                Assert.Equal(ColourRole.Taxiway, surface.Colour);
                Assert.Equal(ColourRole.TaxiwayMarking, line.Colour);
                Assert.Equal(DrawLayer.Taxiway, line.Layer);
                Assert.Equal(0, line.Source.Sub);
                Art.AssertZeroPaint(line, what + " centreline");
            }

            // The Taxiway layer is every surface, then every fill, then every centreline.
            var expected = new List<string>();
            foreach (ushort e in new ushort[] { 1, 2, 3, 4, 6 })
            {
                expected.Add("TaxiEdge:" + e);
            }

            expected.Add("TaxiNode:2");
            expected.Add("TaxiNode:11");
            foreach (ushort e in new ushort[] { 1, 2, 3, 4, 6 })
            {
                expected.Add("TaxiCentreline:" + e);
            }

            var got = new List<string>();
            foreach (DrawPrimitive p in Prims.InLayer(all, DrawLayer.Taxiway))
            {
                got.Add(p.Source.Kind + ":" + p.Source.Id);
            }

            Assert.Equal(expected, got);

            // Without sim.airside there is no taxiway marking at all.
            List<DrawPrimitive> none = s.Frame(s.Builder(airside: false));
            Assert.Empty(Prims.InLayer(none, DrawLayer.Taxiway));
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_stand_lead_in_and_numbers()
        {
            // TaxiScene's stand nodes, plus three stands for the digit rule:
            //   stand 1 at node 11: lowest incident edge is 2 (11-2), not 4,
            //     though 4 is listed first: v = (100,100) - (100,40) = (0,60)
            //   stand 2 at node 12: edge 3 from node 2: v = (100,60)
            //   stand 3 at node 14: no incident edge: v = (0,1)
            //   stand 4 at node 15: edge 6 to node 16 at the same place: v = (0,1)
            //   stand 42 at node 21 (1000,0), edge 21 from node 20 (1000,-100): v = (0,100)
            //   stand 60513 at node 31 (2000,0), edge 31 from node 30 (2070,30): v = (-70,-30)
            ArtScene s = TaxiScene()
                .Node(20, 1000, -100)
                .Node(21, 1000, 0, TaxiNodeKind.StandPosition)
                .Node(30, 2070, 30)
                .Node(31, 2000, 0, TaxiNodeKind.StandPosition)
                .Edge(21, 20, 21)
                .Edge(31, 31, 30)
                .Stand(1, 11)
                .Stand(2, 12)
                .Stand(3, 14)
                .Stand(4, 15)
                .Stand(42, 21)
                .Stand(60513, 31)
                .Done();
            Assert.Equal(40, s.StandSize);
            List<DrawPrimitive> all = s.Frame(s.Builder());

            // Lead-ins: a Dot centred on the node, diameter StandSize, Facing the nose-in vector.
            (ushort Stand, float X, float Y, float Vx, float Vy)[] leadIns =
            {
                (1, 100f, 100f, 0f, 60f),
                (2, 200f, 100f, 100f, 60f),
                (3, 400f, 400f, 0f, 1f),
                (4, 700f, 700f, 0f, 1f),
                (42, 1000f, 0f, 0f, 100f),
                (60513, 2000f, 0f, -70f, -30f),
            };
            foreach ((ushort stand, float x, float y, float vx, float vy) in leadIns)
            {
                string what = "stand " + stand + " lead-in";
                DrawPrimitive p = Prims.Single(all, SourceKind.StandMarking, stand);
                Assert.Equal(0, p.Source.Sub);
                Art.AssertDot(p, VisualId.StandLeadIn, x, y, 40f, what);
                Assert.Equal(DrawLayer.Stand, p.Layer);
                Assert.Equal(ColourRole.TaxiwayMarking, p.Colour);
                Art.AssertFacing(vx, vy, p, what);
                Art.AssertZeroPaint(p, what);
            }

            // Numbers. StandSize 40: G = 6, d = 16, e_i = ((c - 1 - 2i) × 18) / 10.
            //   stand 1, v (0,60), Ls 60, c 1, e 0: (100 + 0, 100 + 960/60) = (100,116)
            //   stand 2, v (100,60), Ls 116: (200 + 1600/116, 100 + 960/116) = (213,108)
            //   stand 3, v (0,1), Ls 1: (400, 416); stand 4: (700, 716)
            //   stand 42, v (0,100), Ls 100, e 1 and -1: (1000 - 1, 16) and (1000 + 1, 16)
            //   stand 60513, v (-70,-30), Ls 76, e 7, 3, 0, -3, -7:
            //     x = 2000 + (-1120 + 30e) / 76, y = (-480 - 70e) / 76, truncating toward zero
            (ushort Stand, int Digit, float X, float Y)[][] numbers =
            {
                new[] { ((ushort)1, 1, 100f, 116f) },
                new[] { ((ushort)2, 2, 213f, 108f) },
                new[] { ((ushort)3, 3, 400f, 416f) },
                new[] { ((ushort)4, 4, 700f, 716f) },
                new[] { ((ushort)42, 4, 999f, 16f), ((ushort)42, 2, 1001f, 16f) },
                new[]
                {
                    ((ushort)60513, 6, 1989f, -12f),
                    ((ushort)60513, 0, 1987f, -9f),
                    ((ushort)60513, 5, 1986f, -6f),
                    ((ushort)60513, 1, 1985f, -3f),
                    ((ushort)60513, 3, 1983f, 0f),
                },
            };
            Vec[] facings = { new Vec(0, 60), new Vec(100, 60), new Vec(0, 1), new Vec(0, 1), new Vec(0, 100), new Vec(-70, -30) };
            for (int n = 0; n < numbers.Length; n++)
            {
                ushort stand = numbers[n][0].Stand;
                List<DrawPrimitive> digits = Prims.Of(all, SourceKind.StandNumber, stand);
                Assert.True(digits.Count == numbers[n].Length, "stand " + stand + ": " + digits.Count + " digits, expected " + numbers[n].Length + ": " + Prims.Show(digits));
                for (int i = 0; i < numbers[n].Length; i++)
                {
                    (_, int digit, float x, float y) = numbers[n][i];
                    string what = "stand " + stand + " digit " + i;
                    DrawPrimitive p = Art.Single(all, SourceKind.StandNumber, stand, i);
                    Art.AssertDot(p, Art.Digit(digit), x, y, 6f, what);
                    Assert.Equal(DrawLayer.Stand, p.Layer);
                    Assert.Equal(ColourRole.TaxiwayMarking, p.Colour);
                    Art.AssertFacing(facings[n].X, facings[n].Y, p, what);
                    Art.AssertZeroPaint(p, what);
                }
            }

            // In the Stand layer: every pad, then every lead-in, then every digit.
            var kinds = new List<SourceKind>();
            foreach (DrawPrimitive p in Prims.InLayer(all, DrawLayer.Stand))
            {
                if (kinds.Count == 0 || kinds[kinds.Count - 1] != p.Source.Kind)
                {
                    kinds.Add(p.Source.Kind);
                }
            }

            Assert.Equal(new[] { SourceKind.Stand, SourceKind.StandMarking, SourceKind.StandNumber }, kinds);
            string? why = Prims.OrderViolation(all);
            Assert.True(why == null, why);
            Assert.Empty(s.Guard.Violations);
        }

        [Fact]
        public void test_scene_stand_numbers_follow_the_fixture_and_stand_size()
        {
            // 15 §15.16's worked example: fixture stand 2 at node 12 (450,-150),
            // v = (-150,-150) from node 3 by edge 4, Ls = 212, G = 6, d = 16:
            // its one digit 2 at (439,-161), not 438.68. The other three follow
            // the same rule: stand 1 v (0,-150) → (300,-166); stand 3 v (0,-150)
            // → (600,-166); stand 4 v (150,-150) → (761,-161).
            var guard = new CallGuard();
            var host = new FakeHost(guard, 1UL);
            var airside = new FakeAirside(guard, Phase1Sim.AirsideLayout());
            RenderLayout layout = Phase1RenderLayout.Build();
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(new RenderSources(host, airside, null), layout);
            List<DrawPrimitive> all = Prims.Copy(b.Build(ArtScene.Overview, Gfx.High()));
            (ushort Stand, float X, float Y, float Vx, float Vy)[] fixture =
            {
                (1, 300f, -166f, 0f, -150f),
                (2, 439f, -161f, -150f, -150f),
                (3, 600f, -166f, 0f, -150f),
                (4, 761f, -161f, 150f, -150f),
            };
            foreach ((ushort stand, float x, float y, float vx, float vy) in fixture)
            {
                string what = "fixture stand " + stand;
                DrawPrimitive digit = Prims.Single(all, SourceKind.StandNumber, stand);
                Art.AssertDot(digit, Art.Digit(stand), x, y, 6f, what + " number");
                Art.AssertFacing(vx, vy, digit, what + " number");
                Art.AssertFacing(vx, vy, Prims.Single(all, SourceKind.StandMarking, stand), what + " lead-in");
            }

            // StandSize 5: G = 0, so no digits; the lead-in stays. StandSize 6: G = 1, digits drawn.
            foreach ((int size, int digits) in new[] { (5, 0), (6, 1), (11, 1), (12, 1) })
            {
                var small = new RenderLayout(layout.TaxiNodes, layout.Runways, layout.FlowNodes, size, layout.AircraftSize, layout.AgentSize, layout.TaxiwayWidth);
                host.Tick++;
                List<DrawPrimitive> got = Prims.Copy(RenderFactory.CreateSceneBuilder(new RenderSources(host, airside, null), small).Build(ArtScene.Overview, Gfx.High()));
                Assert.True(Art.OfKind(got, SourceKind.StandNumber).Count == 4 * digits, "StandSize " + size + ": " + Prims.Show(Art.OfKind(got, SourceKind.StandNumber)));
                Assert.Equal(4, Art.OfKind(got, SourceKind.StandMarking).Count);
                foreach (DrawPrimitive p in Art.OfKind(got, SourceKind.StandNumber))
                {
                    Assert.Equal((float)(size / 6), p.Size);
                }
            }

            Assert.Empty(guard.Violations);
        }

        private readonly struct Vec
        {
            public Vec(float x, float y)
            {
                X = x;
                Y = y;
            }

            public float X { get; }

            public float Y { get; }
        }
    }
}
