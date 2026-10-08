using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Render.Tests
{
    internal static class Tracks
    {
        public static AircraftTrack Make(
            ulong flight,
            AircraftLegPhase phase,
            ushort? atNode = null,
            ushort? onEdge = null,
            Fx progress = default,
            ushort? stand = null,
            MovementKind kind = MovementKind.Departure,
            ushort? runway = null,
            ulong phaseEnteredAt = 0UL,
            ulong dueAt = ulong.MaxValue)
        {
            return new AircraftTrack(
                new FlightId(flight),
                kind,
                phase,
                atNode.HasValue ? new TaxiNodeId(atNode.Value) : (TaxiNodeId?)null,
                onEdge.HasValue ? new TaxiEdgeId(onEdge.Value) : (TaxiEdgeId?)null,
                progress,
                stand.HasValue ? new StandId(stand.Value) : (StandId?)null,
                runway.HasValue ? new RunwayId(runway.Value) : (RunwayId?)null,
                phaseEnteredAt,
                dueAt,
                ulong.MaxValue,
                EventRef.None,
                EventRef.None,
                ulong.MaxValue);
        }
    }

    /// <summary>
    /// A small hand-made scene with round numbers, so every expected
    /// coordinate is exact in float.
    ///
    ///   runway 1: (-300,0)-(0,0), width 40, threshold node 1
    ///   taxi nodes: 1 (0,0), 2 (100,40), 11 (100,100), 12 (200,100)
    ///   edges: E1 1-2, E2 2-11, E3 2-12; stands S1 at node 11, S2 at node 12
    ///   flow boxes: hall 3 (0,200)-(100,240) fill 200;
    ///               queue 5 (200,200)-(300,260) fill 100, lanes 5 / 2 open;
    ///               queue 6 (400,200)-(500,260) fill 100, lanes 40 / 3 open
    /// </summary>
    internal sealed class SmallScene
    {
        public const ushort Runway = 1;
        public const ushort Threshold = 1;
        public const ushort Junction = 2;
        public const ushort StandNode1 = 11;
        public const ushort StandNode2 = 12;
        public const ushort E1 = 1;
        public const ushort E2 = 2;
        public const ushort E3 = 3;
        public const ushort S1 = 1;
        public const ushort S2 = 2;
        public const uint Hall = 3;
        public const uint Queue5 = 5;
        public const uint Queue6 = 6;
        public const int StandSize = 20;
        public const int AircraftSize = 10;
        public const int AgentSize = 2;
        public const int TaxiwayWidth = 8;

        public readonly CallGuard Guard = new CallGuard();
        public readonly FakeHost Host;
        public readonly FakeAirside? Airside;
        public readonly FakeFlow? Flow;
        public readonly RenderLayout Layout;

        public SmallScene(bool airside = true, bool flow = true, bool reversedLayoutLists = false)
        {
            Host = new FakeHost(Guard, 7UL);
            Airside = airside ? new FakeAirside(Guard, AirsideLayout()) : null;
            Flow = flow
                ? new FakeFlow(Guard)
                    .Node(Hall)
                    .Node(Queue5, 0, new LaneState(5, 2))
                    .Node(Queue6, 0, new LaneState(40, 3))
                : null;
            Layout = MakeLayout(reversedLayoutLists);
        }

        public RenderSources Sources => new RenderSources(Host, Airside, Flow);

        public static FlowNodeBox HallBox => new FlowNodeBox(new NodeId(Hall), 0, 200, 100, 240, 200);

        public static FlowNodeBox Queue5Box => new FlowNodeBox(new NodeId(Queue5), 200, 200, 300, 260, 100);

        public static FlowNodeBox Queue6Box => new FlowNodeBox(new NodeId(Queue6), 400, 200, 500, 260, 100);

        /// <summary>A camera that sees the whole scene from above the zoom threshold.</summary>
        public static CameraView Overview => Cam.At(100f, 120f, 1000f, 1f);

        public static AirsideLayout AirsideLayout()
        {
            var runways = new[] { new RunwayDef(new RunwayId(Runway), new TaxiNodeId(Threshold), 270, 30, 10U) };
            var nodes = new[]
            {
                new TaxiNodeDef(new TaxiNodeId(Threshold), TaxiNodeKind.RunwayThreshold),
                new TaxiNodeDef(new TaxiNodeId(Junction), TaxiNodeKind.Junction),
                new TaxiNodeDef(new TaxiNodeId(StandNode1), TaxiNodeKind.StandPosition),
                new TaxiNodeDef(new TaxiNodeId(StandNode2), TaxiNodeKind.StandPosition),
            };
            var edges = new[]
            {
                new TaxiEdgeDef(new TaxiEdgeId(E1), new TaxiNodeId(Threshold), new TaxiNodeId(Junction), 20U, true),
                new TaxiEdgeDef(new TaxiEdgeId(E2), new TaxiNodeId(Junction), new TaxiNodeId(StandNode1), 10U, true),
                new TaxiEdgeDef(new TaxiEdgeId(E3), new TaxiNodeId(Junction), new TaxiNodeId(StandNode2), 10U, true),
            };
            var stands = new[]
            {
                new StandDef(new StandId(S1), new TaxiNodeId(StandNode1), new ContentId("medium"), new NodeId(9)),
                new StandDef(new StandId(S2), new TaxiNodeId(StandNode2), new ContentId("medium"), new NodeId(9)),
            };
            return new AirsideLayout(runways, nodes, edges, stands);
        }

        public static RenderLayout MakeLayout(bool reversed = false)
        {
            var taxi = new List<TaxiNodePosition>
            {
                new TaxiNodePosition(new TaxiNodeId(Threshold), 0, 0),
                new TaxiNodePosition(new TaxiNodeId(Junction), 100, 40),
                new TaxiNodePosition(new TaxiNodeId(StandNode1), 100, 100),
                new TaxiNodePosition(new TaxiNodeId(StandNode2), 200, 100),
            };
            var runways = new List<RunwayGeometry> { new RunwayGeometry(new RunwayId(Runway), -300, 0, 0, 0, 40) };
            var boxes = new List<FlowNodeBox> { HallBox, Queue5Box, Queue6Box };
            if (reversed)
            {
                taxi.Reverse();
                boxes.Reverse();
            }

            return new RenderLayout(taxi, runways, boxes, StandSize, AircraftSize, AgentSize, TaxiwayWidth);
        }

        public ISceneBuilder Builder()
        {
            return RenderFactory.CreateSceneBuilder(Sources, Layout);
        }

        public IPromotionController Controller()
        {
            return RenderFactory.CreatePromotionController(Sources, Layout);
        }
    }

    /// <summary>
    /// 15 §15.11's max-tier fake sources: 3 runways, 60 stands all occupied,
    /// 100 tracked aircraft on the graph, 200 FlowNodeBoxes, and a camera
    /// below the zoom threshold that sees exactly 16 of them, each holding
    /// more than MAX_DRAWN_AGENTS_PER_NODE passengers. Fixture sizing only.
    ///
    /// Flow node k (1..200) is grid cell i = k − 1, column i % 20, row
    /// i / 20, box (20c, 20r)-(20c + 10, 20r + 10). The camera's view is
    /// [0, 70] × [0, 70], which touches columns and rows 0..3 (closed
    /// intervals) and misses column and row 4, which start at 80.
    /// Every fourth node is a Queue with 8 lanes, 3 open. Airside sits at
    /// x ≥ 5000, out of the camera's view; nothing culls it (15 §15.5).
    ///
    /// Q-130 (15 §15.11, §15.18): a version 2 layout with scenery (an apron,
    /// a terminal and a control tower, a pier per pier node and a bridge per
    /// stand), a schedule naming every tracked flight, a content index whose
    /// types cover all six size categories, and looks with four airlines.
    ///
    /// Q-132 (15 §15.23), with motion set: 20 approaching, 5 held and 3
    /// runway aircraft (flights 101..128, all drawn from tick 1 on, below),
    /// a walkway along the middle of each of the 16 promoted boxes, whose
    /// agents' cohorts 1..6 TryGetCohort knows, and a bridge with boarders
    /// on every stand: each bridge names its stand, the stands of pier 1
    /// hold arrivals whose rotation 1000 + s boards, the others departures
    /// that board themselves, each with 12 passengers at a gate. Only
    /// PierCamera sees bridges below the zoom threshold.
    /// </summary>
    internal sealed class MaxTierScene
    {
        public const int Runways = 3;
        public const int Piers = 6;
        public const int StandsPerPier = 10;
        public const int Stands = Piers * StandsPerPier;
        public const int Aircraft = 100;
        public const int Boxes = 200;
        public const int Columns = 20;
        public const int PromotedPopulation = 300;
        public const ushort Hub = 100;
        public const int Airlines = 4;

        // Q-132's motion load (15 §15.23).
        public const int Approaching = 20;
        public const int Held = 5;
        public const int OnRunway = 3;
        public const int MotionAircraft = Approaching + Held + OnRunway;
        public const ulong FirstApproaching = 101UL;
        public const ulong FirstHeld = FirstApproaching + Approaching;
        public const ulong FinalArrival = FirstHeld + Held;
        public const ulong RolledOutArrival = FinalArrival + 1UL;
        public const ulong ClimbingDeparture = FinalArrival + 2UL;
        public const int BoardersPerStand = 12;
        public const ulong RotationBase = 1000UL;

        public readonly CallGuard Guard = new CallGuard();
        public readonly FakeHost Host;
        public readonly FakeAirside Airside;
        public readonly FakeFlow Flow;
        public readonly FakeSchedule Schedule;
        public readonly FakeContent Content;
        public readonly RenderLooks Looks;
        public readonly RenderLayout Layout;
        public readonly bool Motion;

        public MaxTierScene(bool motion = false)
        {
            Motion = motion;
            Host = new FakeHost(Guard, 1UL);
            Airside = new FakeAirside(Guard, MakeAirside(out List<TaxiNodePosition> taxi, out List<RunwayGeometry> runways));
            Flow = new FakeFlow(Guard) { RecordPromotions = false, RecordQueries = false };
            Schedule = new FakeSchedule(Guard) { RecordCalls = false };
            Content = ArtContent.AllSizes(Guard);
            Looks = ArtLooks.WithAirlines(Airlines);
            var boxes = new List<FlowNodeBox>();
            var walkways = new List<LayoutWalkway>();
            for (uint k = 1; k <= Boxes; k++)
            {
                int i = (int)k - 1;
                int c = i % Columns;
                int r = i / Columns;
                boxes.Add(new FlowNodeBox(new NodeId(k), 20 * c, 20 * r, (20 * c) + 10, (20 * r) + 10, 100));
                int population = Promoted(k) ? PromotedPopulation : (int)((k * 37U) % 150U);
                Flow.Node(k, population, k % 4U == 0U ? new LaneState(8, 3) : (LaneState?)null);
                if (Promoted(k))
                {
                    walkways.Add(new LayoutWalkway(new NodeId(k), (20 * c) + 1, (20 * r) + 5, (20 * c) + 9, (20 * r) + 5, 4));
                }
            }

            Layout = motion
                ? new RenderLayout(taxi, runways, boxes, 30, 20, 1, 12, MakeAreas(), MakeBridges(withStands: true), walkways)
                : new RenderLayout(taxi, runways, boxes, 30, 20, 1, 12, MakeAreas(), MakeBridges(withStands: false));

            for (ushort s = 1; s <= Stands; s++)
            {
                Airside.Occupy(s, s);
            }

            // Every tracked flight is in the schedule. Airline k % 5 + 1 has a
            // livery for 1..4 and none for 5; type "t" + k % 8 resolves for
            // t0..t6 and is unknown for t7 (15 §15.16).
            for (ulong k = 1; k <= Aircraft; k++)
            {
                string type = "t" + (k % 8UL).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (motion && k <= StandsPerPier)
                {
                    Schedule.AddArrival(k, RotationBase + k, (uint)(k % 5UL) + 1U, type);
                }
                else
                {
                    Schedule.Add(k, (uint)(k % 5UL) + 1U, type);
                }
            }

            var tracks = new List<AircraftTrack>();
            for (ushort s = 1; s <= Stands; s++)
            {
                MovementKind kind = motion && s <= StandsPerPier ? MovementKind.Arrival : MovementKind.Departure;
                tracks.Add(Tracks.Make(s, AircraftLegPhase.OnStand, atNode: StandNode(s), stand: s, kind: kind));
                if (motion)
                {
                    Flow.Boarding(kind == MovementKind.Arrival ? RotationBase + s : s, BoardersPerStand);
                }
            }

            if (motion)
            {
                // The agents of a promoted box are cohorts 1..6 (FakeFlow's
                // cohorts of 50); their group walks its walkway in 20 ticks.
                for (ulong cohort = 1; cohort <= 6; cohort++)
                {
                    Flow.Cohort(cohort, 0UL, 20UL);
                }

                AddMotionTracks(tracks);
            }

            // 30 taxiing on stand edges, entered from their pier, half way along.
            for (int a = 0; a < 30; a++)
            {
                ushort stand = (ushort)(a + 1);
                ushort pier = (ushort)(Hub + 1 + ((stand - 1) / StandsPerPier));
                tracks.Add(Tracks.Make((ulong)(Stands + 1 + a), AircraftLegPhase.Taxiing, atNode: pier, onEdge: (ushort)(100 + stand), progress: Fx.FromRatio(1, 2)));
            }

            // 10 holding at the hub.
            for (int a = 0; a < 10; a++)
            {
                tracks.Add(Tracks.Make((ulong)(Stands + 31 + a), AircraftLegPhase.HeldOnTaxiway, atNode: Hub));
            }

            Airside.SetTracks(tracks.ToArray());
            Airside.SetQueue(1, 2);
        }

        public static CameraView Camera => Cam.At(35f, 35f, 70f, 1f);

        /// <summary>
        /// Below the zoom threshold over pier 1's ten bridges, x 5325..5550 and
        /// y 1540..1590: the view is [5287.5, 5587.5] × [1515, 1615], and pier
        /// 2's first bridge is at x 5625.
        /// </summary>
        public static CameraView PierCamera => Cam.At(5437.5f, 1565f, 100f, 3f);

        /// <summary>Aircraft drawn in every frame from tick 1 on (each gets one TryGetFlight, 15 §15.16).</summary>
        public int DrawnAircraft => Motion ? Aircraft + MotionAircraft : Aircraft;

        public RenderSources Sources => new RenderSources(Host, Airside, Flow, Schedule, Content);

        /// <summary>The three-argument builder, with this scene's looks (15 §15.9, Q-130).</summary>
        public ISceneBuilder Builder()
        {
            return RenderFactory.CreateSceneBuilder(Sources, Layout, Looks);
        }

        public static bool Promoted(uint node)
        {
            int i = (int)node - 1;
            return i % Columns < 4 && i / Columns < 4;
        }

        public static ushort StandNode(ushort stand)
        {
            return (ushort)(200 + stand);
        }

        private static AirsideLayout MakeAirside(out List<TaxiNodePosition> taxi, out List<RunwayGeometry> geometry)
        {
            var runways = new List<RunwayDef>();
            var nodes = new List<TaxiNodeDef>();
            var edges = new List<TaxiEdgeDef>();
            var stands = new List<StandDef>();
            taxi = new List<TaxiNodePosition>();
            geometry = new List<RunwayGeometry>();
            for (ushort r = 1; r <= Runways; r++)
            {
                runways.Add(new RunwayDef(new RunwayId(r), new TaxiNodeId(r), 270, 40, 10U));
                nodes.Add(new TaxiNodeDef(new TaxiNodeId(r), TaxiNodeKind.RunwayThreshold));
                taxi.Add(new TaxiNodePosition(new TaxiNodeId(r), 8000, 200 * r));
                geometry.Add(new RunwayGeometry(new RunwayId(r), 5000, 200 * r, 8000, 200 * r, 45));
                edges.Add(new TaxiEdgeDef(new TaxiEdgeId(r), new TaxiNodeId(r), new TaxiNodeId(Hub), 30U, true));
            }

            nodes.Add(new TaxiNodeDef(new TaxiNodeId(Hub), TaxiNodeKind.Junction));
            taxi.Add(new TaxiNodePosition(new TaxiNodeId(Hub), 6000, 1000));
            for (ushort p = 1; p <= Piers; p++)
            {
                ushort pier = (ushort)(Hub + p);
                nodes.Add(new TaxiNodeDef(new TaxiNodeId(pier), TaxiNodeKind.Junction));
                taxi.Add(new TaxiNodePosition(new TaxiNodeId(pier), 5000 + (300 * p), 1500));
                edges.Add(new TaxiEdgeDef(new TaxiEdgeId((ushort)(10 + p)), new TaxiNodeId(Hub), new TaxiNodeId(pier), 20U, true));
            }

            for (ushort s = 1; s <= Stands; s++)
            {
                int p = ((s - 1) / StandsPerPier) + 1;
                int j = ((s - 1) % StandsPerPier) + 1;
                ushort node = StandNode(s);
                nodes.Add(new TaxiNodeDef(new TaxiNodeId(node), TaxiNodeKind.StandPosition));
                taxi.Add(new TaxiNodePosition(new TaxiNodeId(node), 5000 + (300 * p) + (25 * j), 1600));
                edges.Add(new TaxiEdgeDef(new TaxiEdgeId((ushort)(100 + s)), new TaxiNodeId((ushort)(Hub + p)), new TaxiNodeId(node), (uint)(10 + j), true));
                stands.Add(new StandDef(new StandId(s), new TaxiNodeId(node), new ContentId("super"), new NodeId(9)));
            }

            return new AirsideLayout(runways, nodes, edges, stands);
        }

        private static List<LayoutArea> MakeAreas()
        {
            var areas = new List<LayoutArea>
            {
                new LayoutArea(1U, AreaKind.Apron, 5200, 1450, 7200, 1650),
                new LayoutArea(2U, AreaKind.Terminal, 5200, 1700, 7200, 1800),
                new LayoutArea(3U, AreaKind.ControlTower, 7300, 1700, 7340, 1740),
            };
            for (int p = 1; p <= Piers; p++)
            {
                int x = 5000 + (300 * p);
                areas.Add(new LayoutArea((uint)(10 + p), AreaKind.Pier, x - 10, 1520, x + 260, 1540));
            }

            return areas;
        }

        private static List<LayoutBridge> MakeBridges(bool withStands)
        {
            var bridges = new List<LayoutBridge>();
            for (int s = 1; s <= Stands; s++)
            {
                int p = ((s - 1) / StandsPerPier) + 1;
                int j = ((s - 1) % StandsPerPier) + 1;
                int x = 5000 + (300 * p) + (25 * j);
                bridges.Add(withStands
                    ? new LayoutBridge((uint)s, x, 1540, x, 1590, 3, new StandId((ushort)s))
                    : new LayoutBridge((uint)s, x, 1540, x, 1590, 3));
            }

            return bridges;
        }

        /// <summary>
        /// The motion load, off-graph (15 §15.20). The host starts at tick 1,
        /// so τ ≥ 0, and every one of them is drawn from then on:
        /// approaching arrivals have STA 1..15, so their window opened by τ = 0,
        /// and their runway is predicted (runway 1 has a queue of 2, so it is
        /// runway 2). At tick 1, the held ones fly their hold, FinalArrival is
        /// on its final (w &lt; 0.1), RolledOutArrival has O = 0 and is at its
        /// exit, and ClimbingDeparture has O = 0 and is at its climb end.
        /// </summary>
        private static void AddMotionTracks(List<AircraftTrack> tracks)
        {
            for (int a = 0; a < Approaching; a++)
            {
                tracks.Add(Tracks.Make(FirstApproaching + (ulong)a, AircraftLegPhase.AwaitingApproach, kind: MovementKind.Arrival, dueAt: 1UL + (ulong)(a % 15)));
            }

            for (int a = 0; a < Held; a++)
            {
                tracks.Add(Tracks.Make(FirstHeld + (ulong)a, AircraftLegPhase.HeldForRunway, kind: MovementKind.Arrival, runway: (ushort)((a % Runways) + 1), phaseEnteredAt: 0UL));
            }

            tracks.Add(Tracks.Make(FinalArrival, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 1, phaseEnteredAt: 0UL, dueAt: 10UL));
            tracks.Add(Tracks.Make(RolledOutArrival, AircraftLegPhase.OnRunway, kind: MovementKind.Arrival, runway: 2, phaseEnteredAt: 0UL, dueAt: 0UL));
            tracks.Add(Tracks.Make(ClimbingDeparture, AircraftLegPhase.OnRunway, kind: MovementKind.Departure, runway: 3, phaseEnteredAt: 0UL, dueAt: 0UL));
        }

        /// <summary>Flights drawn with Elevation &gt; 0 at tick 1 (15 §15.20): approaching, held, on the final, climbing.</summary>
        public static bool AirborneAtTickOne(ulong flight)
        {
            return (flight >= FirstApproaching && flight < FinalArrival) || flight == FinalArrival || flight == ClimbingDeparture;
        }
    }
}
