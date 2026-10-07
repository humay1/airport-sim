using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.16 "Facing", "Aircraft visual and livery" and "Passenger
    /// paint", with 15 §15.6's schedule and content calls, against fakes.
    /// </summary>
    public sealed class SceneAircraftArtTests
    {
        /// <summary>
        ///   nodes 1 (0,0), 2 (100,40), 3 (0,300), 4 (500,0), 9 (900,900),
        ///         11 (100,100), 12 (200,100), 13 (300,100), 14 (400,400),
        ///         15 and 16 both at (700,700); node 7 has no position
        ///   edges 4: 11-13 (listed first), 1: 1-2, 2: 11-2, 3: 2-12, 6: 15-16
        ///   runway 1: threshold 1, (-300,0)-(0,0)       threshold nearer P1
        ///   runway 2: threshold 3, (0,300)-(0,600)      threshold nearer P0
        ///   runway 3: threshold 4, (400,0)-(600,0)      threshold equidistant
        ///   runway 9: threshold 9, no geometry; runway 7: threshold 7, no geometry
        ///   runway 8: geometry (-300,-50)-(0,-50), no RunwayDef
        ///   stands 1 at 11, 2 at 12, 3 at 14, 4 at 15
        /// </summary>
        private static ArtScene FacingScene()
        {
            var s = new ArtScene();
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold)
                .Node(2, 100, 40)
                .Node(3, 0, 300, TaxiNodeKind.RunwayThreshold)
                .Node(4, 500, 0, TaxiNodeKind.RunwayThreshold)
                .Node(9, 900, 900, TaxiNodeKind.RunwayThreshold)
                .Unpositioned(7)
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
                .Edge(6, 15, 16)
                .Runway(1, 1, 270, -300, 0, 0, 0, 40)
                .Runway(2, 3, 360, 0, 300, 0, 600, 40)
                .Runway(3, 4, 90, 400, 0, 600, 0, 40)
                .RunwayDefOnly(9, 9, 90)
                .RunwayDefOnly(7, 7, 90)
                .Geometry(8, -300, -50, 0, -50, 40)
                .Stand(1, 11)
                .Stand(2, 12)
                .Stand(3, 14)
                .Stand(4, 15)
                .Done();
            return s;
        }

        [Fact]
        public void test_scene_aircraft_facing_follows_the_five_rules()
        {
            ArtScene s = FacingScene();
            MovementKind dep = MovementKind.Departure;
            MovementKind arr = MovementKind.Arrival;
            (AircraftTrack Track, float X, float Y, string Why)[] cases =
            {
                // Rule 1: on an edge, the other endpoint minus AtNode, whatever the phase.
                (Tracks.Make(1UL, AircraftLegPhase.Taxiing, atNode: 2, onEdge: 1, progress: Fx.FromRatio(1, 4)), -100f, -40f, "rule 1, entered edge 1 at node 2"),
                (Tracks.Make(2UL, AircraftLegPhase.Taxiing, atNode: 1, onEdge: 1, progress: Fx.FromRatio(1, 2)), 100f, 40f, "rule 1, entered edge 1 at node 1"),
                (Tracks.Make(3UL, AircraftLegPhase.Taxiing, atNode: 11, onEdge: 2, kind: dep, stand: 1), 0f, -60f, "rule 1, a pushback off stand 1 is drawn nose first"),
                (Tracks.Make(4UL, AircraftLegPhase.OnStand, atNode: 12, onEdge: 3, stand: 2), -100f, -60f, "rule 1 comes before rule 2"),

                // Rule 2: on stand, AtNode's nose-in vector.
                (Tracks.Make(10UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1), 0f, 60f, "rule 2, node 11: lowest-id incident edge 2, not edge 4"),
                (Tracks.Make(11UL, AircraftLegPhase.AwaitingPushbackClearance, atNode: 12, stand: 2), 100f, 60f, "rule 2, node 12 by edge 3"),
                (Tracks.Make(12UL, AircraftLegPhase.OnStand, atNode: 14, stand: 3), 0f, 1f, "rule 2, node 14 has no incident edge"),
                (Tracks.Make(13UL, AircraftLegPhase.OnStand, atNode: 15, stand: 4), 0f, 1f, "rule 2, node 15's difference is (0,0)"),
                (Tracks.Make(14UL, AircraftLegPhase.AwaitingPushbackClearance, atNode: 2), 100f, 40f, "rule 2, node 2 by edge 1"),

                // Rule 3: along the runway, away from its threshold.
                (Tracks.Make(20UL, AircraftLegPhase.OnRunway, atNode: 1, runway: 1), -300f, 0f, "rule 3, threshold nearer P1: P0 - P1"),
                (Tracks.Make(21UL, AircraftLegPhase.HeldForRunway, atNode: 3, runway: 2), 0f, 300f, "rule 3, threshold nearer P0: P1 - P0"),
                (Tracks.Make(22UL, AircraftLegPhase.OnRunway, atNode: 4, runway: 3), 200f, 0f, "rule 3, equidistant (<=): P1 - P0"),
                (Tracks.Make(23UL, AircraftLegPhase.OnRunway, atNode: 1), 0f, 0f, "rule 5, Runway unset"),
                (Tracks.Make(24UL, AircraftLegPhase.HeldForRunway, atNode: 9, runway: 9), 0f, 0f, "rule 5, runway 9 has no geometry"),
                (Tracks.Make(25UL, AircraftLegPhase.OnRunway, atNode: 1, runway: 77), 0f, 0f, "rule 5, runway 77 is unknown"),
                (Tracks.Make(26UL, AircraftLegPhase.OnRunway, atNode: 1, runway: 8), 0f, 0f, "rule 5, runway 8 has no RunwayDef, so no threshold"),
                (Tracks.Make(27UL, AircraftLegPhase.HeldForRunway, atNode: 1, runway: 7), 0f, 0f, "rule 5, runway 7's threshold has no position"),

                // Rule 4: toward the destination in a straight line.
                (Tracks.Make(30UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2, runway: 2, kind: dep), -100f, 260f, "rule 4, a departure heads for runway 2's threshold node 3"),
                (Tracks.Make(31UL, AircraftLegPhase.Taxiing, atNode: 2, stand: 2, kind: arr), 100f, 60f, "rule 4, an arrival heads for stand 2's node 12"),
                (Tracks.Make(32UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2, runway: 9, kind: dep), 800f, 860f, "rule 4, runway 9's threshold needs no geometry"),
                (Tracks.Make(33UL, AircraftLegPhase.Taxiing, atNode: 2, stand: 1, runway: 1, kind: dep), -100f, -40f, "rule 4, a departure follows its runway, not its stand"),
                (Tracks.Make(34UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2, stand: 1, runway: 1, kind: arr), 0f, 60f, "rule 4, an arrival follows its stand, not its runway"),
                (Tracks.Make(35UL, AircraftLegPhase.Taxiing, atNode: 3, runway: 2, kind: dep), 0f, 0f, "rule 4, already at the destination"),
                (Tracks.Make(36UL, AircraftLegPhase.Taxiing, atNode: 2, kind: dep), 0f, 0f, "rule 5, a departure with no Runway"),
                (Tracks.Make(37UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2, kind: arr), 0f, 0f, "rule 5, an arrival with no Stand"),
                (Tracks.Make(38UL, AircraftLegPhase.Taxiing, atNode: 2, stand: 99, kind: arr), 0f, 0f, "rule 5, stand 99 is unknown"),
                (Tracks.Make(39UL, AircraftLegPhase.Taxiing, atNode: 2, runway: 77, kind: dep), 0f, 0f, "rule 5, runway 77 is unknown"),
                (Tracks.Make(40UL, AircraftLegPhase.HeldOnTaxiway, atNode: 2, runway: 7, kind: dep), 0f, 0f, "rule 5, runway 7's threshold has no position"),
            };
            var tracks = new List<AircraftTrack>();
            foreach ((AircraftTrack t, _, _, _) in cases)
            {
                tracks.Add(t);
            }

            s.Airside!.SetTracks(tracks.ToArray());
            List<DrawPrimitive> all = s.Frame(s.Builder());
            foreach ((AircraftTrack t, float x, float y, string why) in cases)
            {
                DrawPrimitive p = Prims.Single(all, SourceKind.Aircraft, t.Flight.Value);
                Assert.Equal(PrimitiveKind.Dot, p.Kind);
                Art.AssertFacing(x, y, p, "flight " + t.Flight.Value + ", " + why);
            }

            Assert.Equal(cases.Length, Prims.InLayer(all, DrawLayer.Aircraft).Count);
            Assert.Empty(s.Guard.Violations);
        }

        /// <summary>
        /// Flights 1..7 fly t0..t6 (size ordinals 0..6); 8 flies t7, which
        /// content lacks; 9 flies "orphan", whose category "ghost" does not
        /// resolve; 10 flies "phantom", which AllOf lists and TryGet does not
        /// resolve; 11 is not in the schedule. 20..22 are in the schedule and
        /// never drawn: awaiting approach, departed, and off-graph.
        /// </summary>
        private static (ArtScene Scene, FakeSchedule Schedule, FakeContent Content) VisualScene()
        {
            var s = new ArtScene();
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold).Node(11, 100, 100, TaxiNodeKind.StandPosition).Edge(1, 1, 11).Stand(1, 11).Done();
            var schedule = new FakeSchedule(s.Guard);
            for (ulong f = 1; f <= 7; f++)
            {
                schedule.Add(f, 1U, "t" + (f - 1UL).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            schedule.Add(8UL, 1U, "t7").Add(9UL, 1U, "orphan").Add(10UL, 1U, "phantom");
            schedule.Add(20UL, 1U, "t5").Add(21UL, 1U, "t5").Add(22UL, 1U, "t5");
            FakeContent content = ArtContent.AllSizes(s.Guard).Aircraft("orphan", "ghost").Phantom(ContentKind.Aircraft, "phantom");

            var tracks = new List<AircraftTrack>();
            for (ulong f = 1; f <= 11; f++)
            {
                tracks.Add(Tracks.Make(f, AircraftLegPhase.OnStand, atNode: 11, stand: 1));
            }

            tracks.Add(Tracks.Make(20UL, AircraftLegPhase.AwaitingApproach, atNode: 1, kind: MovementKind.Arrival));
            tracks.Add(Tracks.Make(21UL, AircraftLegPhase.Departed, atNode: 1));
            tracks.Add(Tracks.Make(22UL, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival));
            s.Airside!.SetTracks(tracks.ToArray());
            return (s, schedule, content);
        }

        [Fact]
        public void test_scene_aircraft_visual_follows_size_category()
        {
            (ArtScene s, FakeSchedule schedule, FakeContent content) = VisualScene();
            VisualId[] expected =
            {
                VisualId.AircraftA, VisualId.AircraftB, VisualId.AircraftC, VisualId.AircraftD, VisualId.AircraftE, VisualId.AircraftF,
                VisualId.AircraftF, VisualId.AircraftC, VisualId.AircraftC, VisualId.AircraftC, VisualId.AircraftC,
            };
            string[] why =
            {
                "ordinal 0", "ordinal 1", "ordinal 2", "ordinal 3", "ordinal 4", "ordinal 5", "ordinal 6 is past F",
                "type not in content", "category does not resolve", "type definition does not resolve", "flight not in the schedule",
            };

            // Content is read at construction, and never again.
            Assert.Equal(0L, content.Calls);
            ISceneBuilder b = s.Builder(schedule: schedule, content: content);
            Assert.True(content.AllOfCalls > 0, "the builder did not read Content.AllOf at construction");
            long contentCalls = content.Calls;

            for (int frame = 0; frame < 3; frame++)
            {
                schedule.Asked.Clear();
                List<DrawPrimitive> all = s.Frame(b);
                for (int i = 0; i < expected.Length; i++)
                {
                    DrawPrimitive p = Prims.Single(all, SourceKind.Aircraft, (ulong)(i + 1));
                    Assert.True(p.Visual == expected[i], "flight " + (i + 1) + " (" + why[i] + "): " + p.Visual + ", expected " + expected[i]);
                }

                Assert.Equal(expected.Length, Prims.InLayer(all, DrawLayer.Aircraft).Count);

                // One TryGetFlight per drawn aircraft per rebuild, none for an undrawn one.
                var asked = new List<ulong>(schedule.Asked);
                asked.Sort();
                var drawn = new List<ulong>();
                for (ulong f = 1; f <= 11; f++)
                {
                    drawn.Add(f);
                }

                Assert.Equal(drawn, asked);

                // An unchanged frame re-reads nothing.
                schedule.Asked.Clear();
                b.Build(ArtScene.Overview, Gfx.High());
                Assert.Empty(schedule.Asked);
                Assert.Equal(contentCalls, content.Calls);
            }

            // Null Schedule, or null Content: AircraftC for every aircraft.
            foreach ((FakeSchedule? sched, FakeContent? cont, string what) in new[] { ((FakeSchedule?)null, (FakeContent?)content, "null Schedule"), (schedule, (FakeContent?)null, "null Content"), ((FakeSchedule?)null, (FakeContent?)null, "neither") })
            {
                List<DrawPrimitive> all = s.Frame(s.Builder(schedule: sched, content: cont));
                List<DrawPrimitive> aircraft = Prims.InLayer(all, DrawLayer.Aircraft);
                Assert.Equal(expected.Length, aircraft.Count);
                foreach (DrawPrimitive p in aircraft)
                {
                    Assert.True(p.Visual == VisualId.AircraftC, what + ": " + Prims.Show(p));
                }
            }

            Assert.True(s.Guard.Violations.Count == 0, "members outside 15 §15.6 were called: " + string.Join(", ", s.Guard.Violations));
        }

        [Fact]
        public void test_scene_aircraft_paint_follows_airline_livery()
        {
            var s = new ArtScene();
            s.Node(1, 0, 0, TaxiNodeKind.RunwayThreshold).Node(11, 100, 100, TaxiNodeKind.StandPosition).Edge(1, 1, 11).Stand(1, 11).Done();
            RenderLooks looks = ArtLooks.WithAirlines(4);
            var schedule = new FakeSchedule(s.Guard).Add(1UL, 1U, "t2").Add(2UL, 3U, "t2").Add(3UL, 9U, "t2").Add(5UL, 4U, "t2");
            Paint airline1 = ArtLooks.PaintOf(looks.Airlines[0].Livery);
            Paint airline3 = ArtLooks.PaintOf(looks.Airlines[2].Livery);
            Paint airline4 = ArtLooks.PaintOf(looks.Airlines[3].Livery);
            Paint fallback = ArtLooks.PaintOf(looks.DefaultLivery);
            Assert.Equal(1U, looks.Airlines[0].Airline.Value);
            Assert.Equal(3U, looks.Airlines[2].Airline.Value);

            // The region order is AircraftRegion's: Fuselage, Tail, Cheatline, Engines, Logo.
            Livery l1 = looks.Airlines[0].Livery;
            var spelled = new Paint(l1.Fuselage, l1.Tail, l1.Cheatline, l1.Engines, l1.Logo, (byte)l1.Mark);
            Assert.Equal(Prims.Show(spelled), Prims.Show(airline1));
            Assert.Equal(0, (int)AircraftRegion.Fuselage);
            Assert.Equal(4, (int)AircraftRegion.Logo);

            s.Airside!.SetTracks(
                Tracks.Make(1UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1),
                Tracks.Make(2UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1),
                Tracks.Make(3UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1),
                Tracks.Make(4UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1),
                Tracks.Make(5UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1));
            List<DrawPrimitive> all = s.Frame(s.Builder(looks, schedule, ArtContent.AllSizes(s.Guard)));
            Art.AssertPaint(airline1, Prims.Single(all, SourceKind.Aircraft, 1UL), "airline 1 has an entry");
            Art.AssertPaint(airline3, Prims.Single(all, SourceKind.Aircraft, 2UL), "airline 3 has an entry");
            Art.AssertPaint(fallback, Prims.Single(all, SourceKind.Aircraft, 3UL), "airline 9 has no entry: DefaultLivery");
            Art.AssertPaint(fallback, Prims.Single(all, SourceKind.Aircraft, 4UL), "flight 4 is not in the schedule: DefaultLivery");
            Art.AssertPaint(airline4, Prims.Single(all, SourceKind.Aircraft, 5UL), "airline 4 has an entry");

            // A null Schedule: DefaultLivery for every aircraft.
            List<DrawPrimitive> noSchedule = s.Frame(s.Builder(looks, null, ArtContent.AllSizes(s.Guard)));
            foreach (DrawPrimitive p in Prims.InLayer(noSchedule, DrawLayer.Aircraft))
            {
                Art.AssertPaint(fallback, p, "null Schedule");
            }

            // The two-argument builder uses DefaultLooks(): its default livery, no airline entries.
            Paint builtIn = ArtLooks.PaintOf(RenderFactory.DefaultLooks().DefaultLivery);
            List<DrawPrimitive> plain = s.Frame(s.Builder(schedule: schedule, content: ArtContent.AllSizes(s.Guard)));
            foreach (DrawPrimitive p in Prims.InLayer(plain, DrawLayer.Aircraft))
            {
                Art.AssertPaint(builtIn, p, "two-argument builder");
            }

            // Phase changes the colour, never the paint or the visual.
            ISceneBuilder b = s.Builder(looks, schedule, ArtContent.AllSizes(s.Guard));
            (AircraftTrack Track, ColourRole Colour)[] phases =
            {
                (Tracks.Make(1UL, AircraftLegPhase.OnStand, atNode: 11, stand: 1), ColourRole.AircraftOnStand),
                (Tracks.Make(1UL, AircraftLegPhase.Taxiing, atNode: 11, onEdge: 1, progress: Fx.FromRatio(1, 2), stand: 1), ColourRole.AircraftMoving),
                (Tracks.Make(1UL, AircraftLegPhase.HeldOnTaxiway, atNode: 1, runway: 1), ColourRole.AircraftHolding),
                (Tracks.Make(1UL, AircraftLegPhase.OnRunway, atNode: 1, runway: 1), ColourRole.AircraftMoving),
                (Tracks.Make(1UL, AircraftLegPhase.AwaitingPushbackClearance, atNode: 11, stand: 1), ColourRole.AircraftOnStand),
            };
            foreach ((AircraftTrack t, ColourRole colour) in phases)
            {
                s.Airside.SetTracks(t);
                DrawPrimitive p = Prims.Single(s.Frame(b), SourceKind.Aircraft, 1UL);
                Assert.True(p.Colour == colour, t.Phase + ": colour " + p.Colour + ", expected " + colour);
                Assert.Equal(VisualId.AircraftC, p.Visual);
                Art.AssertPaint(airline1, p, t.Phase + " keeps airline 1's paint");
            }

            Assert.True(s.Guard.Violations.Count == 0, "members outside 15 §15.6 were called: " + string.Join(", ", s.Guard.Violations));
        }

        [Fact]
        public void test_scene_passenger_paint_is_a_fixed_hash_of_the_agent()
        {
            var s = new ArtScene();
            s.Box(5, 0, 0, 100, 60, 100).Done();
            PassengerRef[] refs =
            {
                new PassengerRef(new CohortId(1UL), 0),
                new PassengerRef(new CohortId(1UL), 1),
                new PassengerRef(new CohortId(1UL), 49),
                new PassengerRef(new CohortId(2UL), 7),
                new PassengerRef(new CohortId(3UL), int.MinValue),
                new PassengerRef(new CohortId(3UL), 0),
                new PassengerRef(new CohortId(0x0123456789ABCDEFUL), -1),
                new PassengerRef(new CohortId(ulong.MaxValue), int.MaxValue),
            };
            s.Flow.SetAgents(5, refs);
            s.Flow.SetPromoted(new NodeId(5), true);
            CameraView cam = Cam.At(50f, 30f, 100f, 2f);
            RenderLooks looks = ArtLooks.WithAirlines(4);

            // Region r takes list_r[h_r mod Count], h_r = FNV-1a-32 of the 13 bytes.
            var expected = new List<string>();
            IReadOnlyList<Rgb>[] lists = { looks.Tops, looks.Bottoms, looks.Skins, looks.Hairs, looks.Bags };
            foreach (PassengerRef r in refs)
            {
                var regions = new Rgb[5];
                for (int region = 0; region < 5; region++)
                {
                    uint h = Fnv.Passenger(r.Cohort.Value, r.Index, region);
                    regions[region] = lists[region][(int)(h % (uint)lists[region].Count)];
                }

                expected.Add(Prims.Show(new Paint(regions[0], regions[1], regions[2], regions[3], regions[4], 0)));
            }

            // The hash is the spec's, not a stand-in: one known value.
            Assert.Equal(0x811C9DC5U, Fnv.Hash32(new byte[0]));
            Assert.Equal(0, (int)PassengerRegion.Top);
            Assert.Equal(4, (int)PassengerRegion.Bag);

            void AssertAgents(List<DrawPrimitive> all, int count, string what)
            {
                List<DrawPrimitive> agents = Prims.Of(all, SourceKind.Agent, 5UL);
                Assert.True(agents.Count == count, what + ": " + agents.Count + " agents, expected " + count);
                foreach (DrawPrimitive p in agents)
                {
                    int rank = p.Source.Sub;
                    Assert.True(rank >= 0 && rank < refs.Length, what + ": rank " + rank);
                    Assert.True(Prims.Show(p.Paint) == expected[rank], what + ": agent rank " + rank + " (cohort " + refs[rank].Cohort.Value + ", index " + refs[rank].Index + ") paint " + Prims.Show(p.Paint) + ", expected " + expected[rank]);
                    Assert.Equal(VisualId.Passenger, p.Visual);
                    Assert.Equal(ColourRole.Agent, p.Colour);
                    Art.AssertFacing(0f, 0f, p, what);
                }
            }

            ISceneBuilder b = RenderFactory.CreateSceneBuilder(s.Sources(), s.Layout, looks);
            AssertAgents(Prims.Copy(b.Build(cam, Gfx.High())), refs.Length, "first build");
            s.Host.Tick++;
            AssertAgents(Prims.Copy(b.Build(cam, Gfx.High())), refs.Length, "rebuild");
            AssertAgents(Prims.Copy(b.Build(cam, Gfx.Custom(true, 3))), 3, "MaxDrawnAgentsPerNode 3");
            AssertAgents(Prims.Copy(b.Build(cam, Gfx.Custom(true, 256, 15, 50, false))), refs.Length, "custom graphics");
            ISceneBuilder fresh = RenderFactory.CreateSceneBuilder(s.Sources(), s.Layout, ArtLooks.WithAirlines(4));
            AssertAgents(Prims.Copy(fresh.Build(cam, Gfx.High())), refs.Length, "fresh builder");

            // One entry per list (DefaultLooks): every passenger wears it.
            RenderLooks d = RenderFactory.DefaultLooks();
            string only = Prims.Show(new Paint(d.Tops[0], d.Bottoms[0], d.Skins[0], d.Hairs[0], d.Bags[0], 0));
            List<DrawPrimitive> plain = Prims.Copy(s.Builder().Build(cam, Gfx.High()));
            List<DrawPrimitive> dots = Prims.Of(plain, SourceKind.Agent, 5UL);
            Assert.Equal(refs.Length, dots.Count);
            foreach (DrawPrimitive p in dots)
            {
                Assert.Equal(only, Prims.Show(p.Paint));
            }

            Assert.Empty(s.Guard.Violations);
        }
    }
}
