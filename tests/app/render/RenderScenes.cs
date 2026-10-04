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
            MovementKind kind = MovementKind.Departure)
        {
            return new AircraftTrack(
                new FlightId(flight),
                kind,
                phase,
                atNode.HasValue ? new TaxiNodeId(atNode.Value) : (TaxiNodeId?)null,
                onEdge.HasValue ? new TaxiEdgeId(onEdge.Value) : (TaxiEdgeId?)null,
                progress,
                stand.HasValue ? new StandId(stand.Value) : (StandId?)null,
                null,
                0UL,
                ulong.MaxValue,
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

        public readonly CallGuard Guard = new CallGuard();
        public readonly FakeHost Host;
        public readonly FakeAirside Airside;
        public readonly FakeFlow Flow;
        public readonly RenderLayout Layout;

        public MaxTierScene()
        {
            Host = new FakeHost(Guard, 1UL);
            Airside = new FakeAirside(Guard, MakeAirside(out List<TaxiNodePosition> taxi, out List<RunwayGeometry> runways));
            Flow = new FakeFlow(Guard) { RecordPromotions = false };
            var boxes = new List<FlowNodeBox>();
            for (uint k = 1; k <= Boxes; k++)
            {
                int i = (int)k - 1;
                int c = i % Columns;
                int r = i / Columns;
                boxes.Add(new FlowNodeBox(new NodeId(k), 20 * c, 20 * r, (20 * c) + 10, (20 * r) + 10, 100));
                int population = Promoted(k) ? PromotedPopulation : (int)((k * 37U) % 150U);
                Flow.Node(k, population, k % 4U == 0U ? new LaneState(8, 3) : (LaneState?)null);
            }

            Layout = new RenderLayout(taxi, runways, boxes, 30, 20, 1, 12);

            for (ushort s = 1; s <= Stands; s++)
            {
                Airside.Occupy(s, s);
            }

            var tracks = new List<AircraftTrack>();
            for (ushort s = 1; s <= Stands; s++)
            {
                tracks.Add(Tracks.Make(s, AircraftLegPhase.OnStand, atNode: StandNode(s), stand: s));
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

        public RenderSources Sources => new RenderSources(Host, Airside, Flow);

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
    }
}
