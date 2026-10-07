using System;
using System.Collections;
using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

namespace AirportSim.App.Render
{
    /// <summary>
    /// The reusable draw-list buffer behind <see cref="RenderFrame.Primitives"/>. Grows by doubling, never shrinks,
    /// so a steady scene allocates nothing after its first build.
    /// </summary>
    internal sealed class PrimitiveBuffer : IReadOnlyList<DrawPrimitive>
    {
        private DrawPrimitive[] _items = new DrawPrimitive[256];
        private int _count;

        public int Count => _count;

        public DrawPrimitive this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        public void Clear()
        {
            _count = 0;
        }

        public void Add(in DrawPrimitive p)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[_count++] = p;
        }

        public IEnumerator<DrawPrimitive> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _items[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>Turns polled sim state into a draw list. Spec: 15 §15.5, §15.6, §15.9, §15.14, §15.16.</summary>
    internal sealed class SceneBuilder : ISceneBuilder
    {
        private const float FixedOne = 4294967296f; // 2^32, the Fx fraction

        private readonly ISimHost _host;
        private readonly IAirsideSystem? _airside;
        private readonly IFlowSystem? _flow;
        private readonly IScheduleSystem? _schedule;

        private readonly float _standSize;
        private readonly float _aircraftSize;
        private readonly float _agentSize;
        private readonly float _taxiwayWidth;

        private readonly RunwayGeometry[] _runways;
        private readonly EdgeShape[] _edges;
        private readonly StandShape[] _stands;
        private readonly FlowNodeBox[] _boxes;
        private readonly Dictionary<ushort, IntPoint> _nodePositions = new Dictionary<ushort, IntPoint>();
        private readonly Dictionary<ushort, int> _edgeIndex = new Dictionary<ushort, int>();
        private readonly Dictionary<ushort, RunwayDef> _runwayDefs = new Dictionary<ushort, RunwayDef>();
        private readonly Dictionary<ushort, RunwayGeometry> _runwayGeometry = new Dictionary<ushort, RunwayGeometry>();
        private readonly Dictionary<ushort, IntPoint> _noseIn = new Dictionary<ushort, IntPoint>();
        private readonly Dictionary<ContentId, VisualId> _aircraftVisual = new Dictionary<ContentId, VisualId>();

        // Scenery and markings are functions of the layout alone, so they are built once, already in draw order.
        private readonly DrawPrimitive[] _aprons;
        private readonly DrawPrimitive[] _buildings;
        private readonly DrawPrimitive[] _bridges;
        private readonly DrawPrimitive[] _runwayMarkings;
        private readonly DrawPrimitive[] _taxiSurfaces;
        private readonly DrawPrimitive[] _taxiJunctions;
        private readonly DrawPrimitive[] _taxiCentrelines;
        private readonly DrawPrimitive[] _standLeadIns;
        private readonly int[] _standLeadInOwner; // index into _stands
        private readonly DrawPrimitive[] _standDigits;
        private readonly int[] _standDigitOwner;

        // Looks, copied so a caller's later changes cannot reach the frame.
        private readonly Livery _defaultLivery;
        private readonly AirlineLivery[] _airlines;
        private readonly Rgb[][] _passengerColours;

        // Per-rebuild scratch, one slot per box or stand.
        private readonly int[] _population;
        private readonly int[] _laneCount; // -1 when TryGetLaneState rejects the node
        private readonly int[] _laneOpen;
        private readonly bool[] _standDrawn;

        private readonly PrimitiveBuffer _buffer = new PrimitiveBuffer();

        private bool _hasFrame;
        private ulong _tick;
        private CameraView _camera;
        private GraphicsSettings _graphics;
        private RenderFrame _frame;

        public SceneBuilder(in RenderSources sources, in RenderLayout layout, in RenderLooks looks)
        {
            _host = sources.Host;
            _airside = sources.Airside;
            _flow = sources.Flow;
            _schedule = sources.Schedule;
            _standSize = layout.StandSize;
            _aircraftSize = layout.AircraftSize;
            _agentSize = layout.AgentSize;
            _taxiwayWidth = layout.TaxiwayWidth;

            _defaultLivery = looks.DefaultLivery;
            _airlines = new AirlineLivery[looks.Airlines.Count];
            for (int i = 0; i < _airlines.Length; i++)
            {
                _airlines[i] = looks.Airlines[i];
            }

            Array.Sort(_airlines, (a, b) => a.Airline.Value.CompareTo(b.Airline.Value));
            _passengerColours = new[]
            {
                Copy(looks.Tops), Copy(looks.Bottoms), Copy(looks.Skins), Copy(looks.Hairs), Copy(looks.Bags),
            };

            _boxes = _flow == null ? Array.Empty<FlowNodeBox>() : Promotion.SortedBoxes(layout);
            _population = new int[_boxes.Length];
            _laneCount = new int[_boxes.Length];
            _laneOpen = new int[_boxes.Length];

            BuildScenery(layout, out _aprons, out _buildings, out _bridges);

            if (_airside == null)
            {
                _runways = Array.Empty<RunwayGeometry>();
                _edges = Array.Empty<EdgeShape>();
                _stands = Array.Empty<StandShape>();
                _runwayMarkings = Array.Empty<DrawPrimitive>();
                _taxiSurfaces = Array.Empty<DrawPrimitive>();
                _taxiJunctions = Array.Empty<DrawPrimitive>();
                _taxiCentrelines = Array.Empty<DrawPrimitive>();
                _standLeadIns = Array.Empty<DrawPrimitive>();
                _standLeadInOwner = Array.Empty<int>();
                _standDigits = Array.Empty<DrawPrimitive>();
                _standDigitOwner = Array.Empty<int>();
                _standDrawn = Array.Empty<bool>();
                return;
            }

            for (int i = 0; i < layout.TaxiNodes.Count; i++)
            {
                TaxiNodePosition p = layout.TaxiNodes[i];
                _nodePositions[p.Node.Value] = new IntPoint(p.X, p.Y);
            }

            _runways = new RunwayGeometry[layout.Runways.Count];
            for (int i = 0; i < _runways.Length; i++)
            {
                _runways[i] = layout.Runways[i];
            }

            Array.Sort(_runways, (a, b) => a.Runway.Value.CompareTo(b.Runway.Value));
            for (int i = 0; i < _runways.Length; i++)
            {
                _runwayGeometry[_runways[i].Runway.Value] = _runways[i];
            }

            AirsideLayout airside = _airside.Layout();
            for (int i = 0; i < airside.Runways.Count; i++)
            {
                _runwayDefs[airside.Runways[i].Id.Value] = airside.Runways[i];
            }

            _edges = new EdgeShape[airside.Edges.Count];
            for (int i = 0; i < _edges.Length; i++)
            {
                TaxiEdgeDef e = airside.Edges[i];
                _edges[i] = new EdgeShape(e.Id.Value, e.From.Value, e.To.Value);
            }

            Array.Sort(_edges, (a, b) => a.Id.CompareTo(b.Id));
            for (int i = 0; i < _edges.Length; i++)
            {
                _edgeIndex[_edges[i].Id] = i;
            }

            _stands = new StandShape[airside.Stands.Count];
            for (int i = 0; i < _stands.Length; i++)
            {
                StandDef s = airside.Stands[i];
                _stands[i] = new StandShape(s.Id, s.Node.Value);
            }

            Array.Sort(_stands, (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            _standDrawn = new bool[_stands.Length];

            BuildNoseIn();
            _runwayMarkings = BuildRunwayMarkings();
            BuildTaxiways(out _taxiSurfaces, out _taxiJunctions, out _taxiCentrelines);
            BuildStandMarkings(out _standLeadIns, out _standLeadInOwner, out _standDigits, out _standDigitOwner);

            if (sources.Content != null)
            {
                BuildAircraftVisuals(sources.Content);
            }
        }

        public RenderFrame Build(in CameraView camera, in GraphicsSettings graphics)
        {
            ulong tick = _host.CurrentTick;
            if (_hasFrame && tick == _tick && SameCamera(camera) && SameGraphics(graphics))
            {
                return _frame;
            }

            _buffer.Clear();
            ReadFlow();
            EmitGround();
            EmitRunways();
            EmitTaxiways();
            EmitStands();
            EmitLandsideBoxes();
            EmitQueueFill();
            EmitLanePips();
            EmitAgents(camera, graphics);
            EmitAircraft();

            _tick = tick;
            _camera = camera;
            _graphics = graphics;
            _frame = new RenderFrame(tick, camera, _buffer, graphics);
            _hasFrame = true;
            return _frame;
        }

        private bool SameCamera(in CameraView c)
        {
            return c.Centre.X == _camera.Centre.X
                && c.Centre.Y == _camera.Centre.Y
                && c.ViewHeight == _camera.ViewHeight
                && c.Aspect == _camera.Aspect;
        }

        private bool SameGraphics(in GraphicsSettings g)
        {
            return g.Preset == _graphics.Preset
                && g.DrawAgents == _graphics.DrawAgents
                && g.MaxDrawnAgentsPerNode == _graphics.MaxDrawnAgentsPerNode
                && g.FrameRateCap == _graphics.FrameRateCap
                && g.ResolutionScalePercent == _graphics.ResolutionScalePercent
                && g.AntiAliasing == _graphics.AntiAliasing;
        }

        private static Rgb[] Copy(IReadOnlyList<Rgb> list)
        {
            var copy = new Rgb[list.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = list[i];
            }

            return copy;
        }

        private static WorldPoint Pt(long x, long y)
        {
            return new WorldPoint((float)x, (float)y);
        }

        private static DrawPrimitive Prim(PrimitiveKind kind, DrawLayer layer, ColourRole colour, VisualId visual, WorldPoint a, WorldPoint b, float size, WorldPoint facing, Paint paint, SourceKind source, ulong id, int sub)
        {
            return new DrawPrimitive(kind, layer, colour, visual, a, b, size, facing, paint, new SourceRef(source, id, sub));
        }

        // The exact floor of the square root, in integers.
        private static long ISqrt(long n)
        {
            if (n <= 0)
            {
                return 0;
            }

            long x = n;
            long y = (x / 2) + (x % 2);
            while (y < x)
            {
                x = y;
                y = (x + (n / x)) / 2;
            }

            return x;
        }

        private static long Dist2(long ax, long ay, long bx, long by)
        {
            return ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by));
        }

        private static VisualId Digit(int d)
        {
            return (VisualId)((int)VisualId.MarkingDigit0 + d);
        }

        // Scenery: aprons before buildings, each by id; bridges by id (15 §15.16 draw order).
        private static void BuildScenery(in RenderLayout layout, out DrawPrimitive[] aprons, out DrawPrimitive[] buildings, out DrawPrimitive[] bridges)
        {
            var areas = new List<LayoutArea>();
            if (layout.Areas != null)
            {
                for (int i = 0; i < layout.Areas.Count; i++)
                {
                    areas.Add(layout.Areas[i]);
                }
            }

            areas.Sort((a, b) => a.Id.CompareTo(b.Id));
            var apronList = new List<DrawPrimitive>();
            var buildingList = new List<DrawPrimitive>();
            for (int i = 0; i < areas.Count; i++)
            {
                LayoutArea a = areas[i];
                WorldPoint min = Pt(a.MinX, a.MinY);
                WorldPoint max = Pt(a.MaxX, a.MaxY);
                if (a.Kind == AreaKind.Apron)
                {
                    apronList.Add(Prim(PrimitiveKind.Box, DrawLayer.Ground, ColourRole.Apron, VisualId.Apron, min, max, 0f, default, default, SourceKind.Apron, a.Id, 0));
                    continue;
                }

                VisualId visual = a.Kind == AreaKind.Terminal ? VisualId.TerminalBuilding : (a.Kind == AreaKind.Pier ? VisualId.Pier : VisualId.ControlTower);
                buildingList.Add(Prim(PrimitiveKind.Box, DrawLayer.Ground, ColourRole.Building, visual, min, max, 0f, default, default, SourceKind.Building, a.Id, 0));
            }

            var bridgeSource = new List<LayoutBridge>();
            if (layout.Bridges != null)
            {
                for (int i = 0; i < layout.Bridges.Count; i++)
                {
                    bridgeSource.Add(layout.Bridges[i]);
                }
            }

            bridgeSource.Sort((a, b) => a.Id.CompareTo(b.Id));
            var bridgeList = new List<DrawPrimitive>();
            for (int i = 0; i < bridgeSource.Count; i++)
            {
                LayoutBridge b = bridgeSource[i];
                bridgeList.Add(Prim(PrimitiveKind.Segment, DrawLayer.Stand, ColourRole.Building, VisualId.JetBridge, Pt(b.X0, b.Y0), Pt(b.X1, b.Y1), b.Width, default, default, SourceKind.JetBridge, b.Id, 0));
            }

            aprons = apronList.ToArray();
            buildings = buildingList.ToArray();
            bridges = bridgeList.ToArray();
        }

        // A node's nose-in vector: its position minus that of the other end of its lowest-id incident edge (15 §15.16).
        private void BuildNoseIn()
        {
            for (int i = 0; i < _edges.Length; i++)
            {
                EdgeShape e = _edges[i];
                SetNoseIn(e.From, e.To);
                SetNoseIn(e.To, e.From);
            }
        }

        private void SetNoseIn(ushort node, ushort other)
        {
            if (_noseIn.ContainsKey(node))
            {
                return;
            }

            IntPoint v = new IntPoint(0, 1);
            if (_nodePositions.TryGetValue(node, out IntPoint at) && _nodePositions.TryGetValue(other, out IntPoint from))
            {
                long dx = (long)at.X - from.X;
                long dy = (long)at.Y - from.Y;
                if (dx != 0 || dy != 0)
                {
                    v = new IntPoint(dx, dy);
                }
            }

            _noseIn[node] = v;
        }

        private IntPoint NoseIn(ushort node)
        {
            return _noseIn.TryGetValue(node, out IntPoint v) ? v : new IntPoint(0, 1);
        }

        // Whether end 0 is the active end: the threshold node is no farther from P0 than from P1.
        private static bool ActiveEndIsZero(in RunwayGeometry g, IntPoint threshold)
        {
            return Dist2(g.X0, g.Y0, threshold.X, threshold.Y) <= Dist2(g.X1, g.Y1, threshold.X, threshold.Y);
        }

        private DrawPrimitive[] BuildRunwayMarkings()
        {
            var list = new List<DrawPrimitive>();
            for (int i = 0; i < _runways.Length; i++)
            {
                AddRunwayMarkings(list, _runways[i]);
            }

            return list.ToArray();
        }

        // 15 §15.16 "Runway markings": integer arithmetic in long, truncating division, floats last.
        private void AddRunwayMarkings(List<DrawPrimitive> list, in RunwayGeometry g)
        {
            long dx = (long)g.X1 - g.X0;
            long dy = (long)g.Y1 - g.Y0;
            long w = g.Width;
            long len = ISqrt((dx * dx) + (dy * dy));
            if (len == 0)
            {
                return;
            }

            ulong id = g.Runway.Value;
            WorldPoint forward0 = Pt(dx, dy);
            WorldPoint forward1 = Pt(-dx, -dy);

            list.Add(Prim(PrimitiveKind.Segment, DrawLayer.Runway, ColourRole.RunwayMarking, VisualId.RunwayEdgeLines, Pt(g.X0, g.Y0), Pt(g.X1, g.Y1), g.Width, default, default, SourceKind.RunwayMarking, id, 0));
            for (int k = 0; k < 2; k++)
            {
                list.Add(Prim(PrimitiveKind.Dot, DrawLayer.Runway, ColourRole.RunwayMarking, VisualId.RunwayThreshold, EndPoint(g, dx, dy, len, k, w / 2, 0), default, g.Width, k == 0 ? forward0 : forward1, default, SourceKind.RunwayMarking, id, 1 + k));
            }

            if (_runwayDefs.TryGetValue(g.Runway.Value, out RunwayDef def) && _nodePositions.TryGetValue(def.ThresholdNode.Value, out IntPoint threshold))
            {
                bool activeIsZero = ActiveEndIsZero(g, threshold);
                int d = def.ActiveDirectionDeg % 360;
                if (d < 0)
                {
                    d += 360;
                }

                long glyph = (2 * w) / 5;
                for (int k = 0; k < 2; k++)
                {
                    int degrees = (k == 0) == activeIsZero ? d : (d + 180) % 360;
                    int number = ((degrees + 5) / 10) % 36;
                    if (number == 0)
                    {
                        number = 36;
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        int digit = i == 0 ? number / 10 : number % 10;
                        long e = ((1 - (2 * i)) * 3 * glyph) / 10;
                        list.Add(Prim(PrimitiveKind.Dot, DrawLayer.Runway, ColourRole.RunwayMarking, Digit(digit), EndPoint(g, dx, dy, len, k, 2 * w, e), default, glyph, k == 0 ? forward0 : forward1, default, SourceKind.RunwayMarking, id, 3 + (2 * k) + i));
                    }
                }
            }

            long margin = 3 * w;
            long rest = len - (2 * margin);
            long dashes = rest < w ? 0 : (rest + w) / (2 * w);
            for (long k = 0; k < dashes; k++)
            {
                long s = margin + ((rest - (((2 * dashes) - 1) * w)) / 2) + (2 * w * k);
                list.Add(Prim(PrimitiveKind.Segment, DrawLayer.Runway, ColourRole.RunwayMarking, VisualId.RunwayCentreDash, EndPoint(g, dx, dy, len, 0, s, 0), EndPoint(g, dx, dy, len, 0, s + w, 0), g.Width, default, default, SourceKind.RunwayMarking, id, (int)(7 + k)));
            }
        }

        // Point(k, d, e): d along end k's forward and e to its left.
        private static WorldPoint EndPoint(in RunwayGeometry g, long dx, long dy, long len, int end, long d, long e)
        {
            long fx = end == 0 ? dx : -dx;
            long fy = end == 0 ? dy : -dy;
            long ox = end == 0 ? g.X0 : g.X1;
            long oy = end == 0 ? g.Y0 : g.Y1;
            return Pt(ox + (((fx * d) - (fy * e)) / len), oy + (((fy * d) + (fx * e)) / len));
        }

        private void BuildTaxiways(out DrawPrimitive[] surfaces, out DrawPrimitive[] junctions, out DrawPrimitive[] centrelines)
        {
            var surfaceList = new List<DrawPrimitive>();
            var centrelineList = new List<DrawPrimitive>();
            var incident = new Dictionary<ushort, int>();
            for (int i = 0; i < _edges.Length; i++)
            {
                EdgeShape e = _edges[i];
                Count(incident, e.From);
                if (e.To != e.From)
                {
                    Count(incident, e.To);
                }

                if (_nodePositions.TryGetValue(e.From, out IntPoint a) && _nodePositions.TryGetValue(e.To, out IntPoint b))
                {
                    WorldPoint from = Pt(a.X, a.Y);
                    WorldPoint to = Pt(b.X, b.Y);
                    surfaceList.Add(Prim(PrimitiveKind.Segment, DrawLayer.Taxiway, ColourRole.Taxiway, VisualId.TaxiwaySurface, from, to, _taxiwayWidth, default, default, SourceKind.TaxiEdge, e.Id, 0));
                    centrelineList.Add(Prim(PrimitiveKind.Segment, DrawLayer.Taxiway, ColourRole.TaxiwayMarking, VisualId.TaxiwayCentreline, from, to, _taxiwayWidth, default, default, SourceKind.TaxiCentreline, e.Id, 0));
                }
            }

            var junctionNodes = new List<ushort>();
            foreach (KeyValuePair<ushort, int> pair in incident)
            {
                if (pair.Value >= 2 && _nodePositions.ContainsKey(pair.Key))
                {
                    junctionNodes.Add(pair.Key);
                }
            }

            junctionNodes.Sort();
            var junctionList = new List<DrawPrimitive>();
            for (int i = 0; i < junctionNodes.Count; i++)
            {
                IntPoint at = _nodePositions[junctionNodes[i]];
                junctionList.Add(Prim(PrimitiveKind.Dot, DrawLayer.Taxiway, ColourRole.Taxiway, VisualId.TaxiwayJunction, Pt(at.X, at.Y), default, _taxiwayWidth, default, default, SourceKind.TaxiNode, junctionNodes[i], 0));
            }

            surfaces = surfaceList.ToArray();
            junctions = junctionList.ToArray();
            centrelines = centrelineList.ToArray();
        }

        private static void Count(Dictionary<ushort, int> counts, ushort node)
        {
            counts.TryGetValue(node, out int n);
            counts[node] = n + 1;
        }

        // Lead-in and number digits of every stand with a position, as 15 §15.16 "Stand markings".
        private void BuildStandMarkings(out DrawPrimitive[] leadIns, out int[] leadInOwner, out DrawPrimitive[] digits, out int[] digitOwner)
        {
            var leadList = new List<DrawPrimitive>();
            var leadOwners = new List<int>();
            var digitList = new List<DrawPrimitive>();
            var digitOwners = new List<int>();
            long size = (long)_standSize;
            long glyph = size / 6;
            long d = (size * 13) / 32;
            for (int s = 0; s < _stands.Length; s++)
            {
                StandShape stand = _stands[s];
                if (!_nodePositions.TryGetValue(stand.Node, out IntPoint n))
                {
                    continue;
                }

                IntPoint v = NoseIn(stand.Node);
                WorldPoint facing = Pt(v.X, v.Y);
                leadList.Add(Prim(PrimitiveKind.Dot, DrawLayer.Stand, ColourRole.TaxiwayMarking, VisualId.StandLeadIn, Pt(n.X, n.Y), default, _standSize, facing, default, SourceKind.StandMarking, stand.Id.Value, 0));
                leadOwners.Add(s);
                if (glyph == 0)
                {
                    continue;
                }

                long ls = ISqrt((v.X * v.X) + (v.Y * v.Y));
                string text = stand.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                int count = text.Length;
                for (int i = 0; i < count; i++)
                {
                    long e = ((count - 1 - (2 * i)) * 3 * glyph) / 10;
                    long x = n.X + (((v.X * d) - (v.Y * e)) / ls);
                    long y = n.Y + (((v.Y * d) + (v.X * e)) / ls);
                    digitList.Add(Prim(PrimitiveKind.Dot, DrawLayer.Stand, ColourRole.TaxiwayMarking, Digit(text[i] - '0'), Pt(x, y), default, glyph, facing, default, SourceKind.StandNumber, stand.Id.Value, i));
                    digitOwners.Add(s);
                }
            }

            leadIns = leadList.ToArray();
            leadInOwner = leadOwners.ToArray();
            digits = digitList.ToArray();
            digitOwner = digitOwners.ToArray();
        }

        // Aircraft type to size-category visual: AircraftA + min(Ordinal, 5). Read once, here.
        private void BuildAircraftVisuals(IContentIndex content)
        {
            IReadOnlyList<ContentId> ids = content.AllOf(ContentKind.Aircraft);
            for (int i = 0; i < ids.Count; i++)
            {
                if (content.TryGet(ids[i], out AircraftDefinition aircraft) && content.TryGet(aircraft.SizeCategory, out SizeCategoryDefinition size))
                {
                    int ordinal = size.Ordinal < 0 ? 0 : (size.Ordinal > 5 ? 5 : size.Ordinal);
                    _aircraftVisual[ids[i]] = (VisualId)((int)VisualId.AircraftA + ordinal);
                }
            }
        }

        // Population and lane state of every box, once per rebuild.
        private void ReadFlow()
        {
            if (_flow == null)
            {
                return;
            }

            for (int i = 0; i < _boxes.Length; i++)
            {
                NodeId node = _boxes[i].Node;
                _population[i] = _flow.Population(node);
                if (_flow.TryGetLaneState(node, out LaneState lanes))
                {
                    _laneCount[i] = lanes.ServerCount;
                    _laneOpen[i] = lanes.ServersOpen;
                }
                else
                {
                    _laneCount[i] = -1;
                    _laneOpen[i] = 0;
                }
            }
        }

        private void EmitAll(DrawPrimitive[] list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                _buffer.Add(list[i]);
            }
        }

        private void EmitGround()
        {
            EmitAll(_aprons);
            EmitAll(_buildings);
        }

        private void EmitRunways()
        {
            if (_airside == null)
            {
                return;
            }

            for (int i = 0; i < _runways.Length; i++)
            {
                RunwayGeometry g = _runways[i];
                ColourRole colour = _airside.RunwayQueueLength(g.Runway) > 0 ? ColourRole.RunwayQueued : ColourRole.Runway;
                _buffer.Add(Prim(
                    PrimitiveKind.Segment,
                    DrawLayer.Runway,
                    colour,
                    VisualId.RunwaySurface,
                    new WorldPoint(g.X0, g.Y0),
                    new WorldPoint(g.X1, g.Y1),
                    g.Width,
                    default,
                    default,
                    SourceKind.Runway,
                    g.Runway.Value,
                    0));
            }

            EmitAll(_runwayMarkings);
        }

        private void EmitTaxiways()
        {
            EmitAll(_taxiSurfaces);
            EmitAll(_taxiJunctions);
            EmitAll(_taxiCentrelines);
        }

        private void EmitStands()
        {
            if (_airside != null)
            {
                float half = _standSize / 2f;
                for (int i = 0; i < _stands.Length; i++)
                {
                    StandShape s = _stands[i];
                    _standDrawn[i] = false;
                    if (!_nodePositions.TryGetValue(s.Node, out IntPoint at) || !_airside.TryGetStand(s.Id, out StandState state))
                    {
                        continue;
                    }

                    _standDrawn[i] = true;
                    _buffer.Add(Prim(
                        PrimitiveKind.Box,
                        DrawLayer.Stand,
                        state.Occupant.HasValue ? ColourRole.StandOccupied : ColourRole.StandFree,
                        VisualId.StandPad,
                        new WorldPoint(at.X - half, at.Y - half),
                        new WorldPoint(at.X + half, at.Y + half),
                        0f,
                        default,
                        default,
                        SourceKind.Stand,
                        s.Id.Value,
                        0));
                }

                for (int i = 0; i < _standLeadIns.Length; i++)
                {
                    if (_standDrawn[_standLeadInOwner[i]])
                    {
                        _buffer.Add(_standLeadIns[i]);
                    }
                }

                for (int i = 0; i < _standDigits.Length; i++)
                {
                    if (_standDrawn[_standDigitOwner[i]])
                    {
                        _buffer.Add(_standDigits[i]);
                    }
                }
            }

            EmitAll(_bridges);
        }

        private void EmitLandsideBoxes()
        {
            for (int i = 0; i < _boxes.Length; i++)
            {
                FlowNodeBox b = _boxes[i];
                _buffer.Add(Prim(
                    PrimitiveKind.Box,
                    DrawLayer.LandsideNode,
                    ColourRole.LandsideNode,
                    VisualId.TerminalZone,
                    new WorldPoint(b.MinX, b.MinY),
                    new WorldPoint(b.MaxX, b.MaxY),
                    0f,
                    default,
                    default,
                    SourceKind.FlowNode,
                    b.Node.Value,
                    0));
            }
        }

        private void EmitQueueFill()
        {
            for (int i = 0; i < _boxes.Length; i++)
            {
                int population = _population[i];
                if (population <= 0)
                {
                    continue;
                }

                FlowNodeBox b = _boxes[i];
                float maxX = population >= b.FillCapacity
                    ? b.MaxX
                    : b.MinX + ((float)((long)(b.MaxX - b.MinX) * population) / b.FillCapacity);
                _buffer.Add(Prim(
                    PrimitiveKind.Box,
                    DrawLayer.QueueFill,
                    ColourRole.QueueFill,
                    VisualId.QueueFill,
                    new WorldPoint(b.MinX, b.MinY),
                    new WorldPoint(maxX, b.MaxY),
                    0f,
                    default,
                    default,
                    SourceKind.QueueFill,
                    b.Node.Value,
                    0));
            }
        }

        // Pip k of n sits at the middle of the k-th of n equal slices across the box, on its mid-line.
        private void EmitLanePips()
        {
            for (int i = 0; i < _boxes.Length; i++)
            {
                int servers = _laneCount[i];
                if (servers <= 0)
                {
                    continue;
                }

                int pips = servers < RenderConstants.MAX_DRAWN_LANES_PER_NODE ? servers : RenderConstants.MAX_DRAWN_LANES_PER_NODE;
                FlowNodeBox b = _boxes[i];
                float width = b.MaxX - b.MinX;
                float y = (b.MinY + b.MaxY) / 2f;
                for (int k = 0; k < pips; k++)
                {
                    float x = b.MinX + (width * (k + 0.5f) / pips);
                    _buffer.Add(Prim(
                        PrimitiveKind.Dot,
                        DrawLayer.Lane,
                        k < _laneOpen[i] ? ColourRole.LaneOpen : ColourRole.LaneClosed,
                        VisualId.LanePip,
                        new WorldPoint(x, y),
                        default,
                        _agentSize,
                        default,
                        default,
                        SourceKind.Lane,
                        b.Node.Value,
                        k));
                }
            }
        }

        private void EmitAgents(in CameraView camera, in GraphicsSettings graphics)
        {
            if (_flow == null)
            {
                return;
            }

            int cap = graphics.MaxDrawnAgentsPerNode;
            if (cap > RenderConstants.MAX_DRAWN_AGENTS_PER_NODE)
            {
                cap = RenderConstants.MAX_DRAWN_AGENTS_PER_NODE;
            }

            for (int i = 0; i < _boxes.Length; i++)
            {
                FlowNodeBox b = _boxes[i];
                if (!Promotion.Desired(b, camera, graphics))
                {
                    continue;
                }

                IReadOnlyList<AgentView> agents = _flow.AgentsAt(b.Node);
                int n = agents.Count < cap ? agents.Count : cap;
                for (int k = 0; k < n; k++)
                {
                    _buffer.Add(Prim(
                        PrimitiveKind.Dot,
                        DrawLayer.Agent,
                        ColourRole.Agent,
                        VisualId.Passenger,
                        AgentPosition(b, k),
                        default,
                        _agentSize,
                        default,
                        PassengerPaint(agents[k].Ref),
                        SourceKind.Agent,
                        b.Node.Value,
                        k));
                }
            }
        }

        // 15 §15.16 "Passenger paint": FNV-1a-32 over Cohort (8 bytes LE), Index (4 bytes LE), then the region byte.
        private Paint PassengerPaint(in PassengerRef r)
        {
            uint h = 0x811C9DC5U;
            ulong cohort = r.Cohort.Value;
            for (int i = 0; i < 8; i++)
            {
                h = Fnv(h, (byte)(cohort >> (8 * i)));
            }

            uint index = unchecked((uint)r.Index);
            for (int i = 0; i < 4; i++)
            {
                h = Fnv(h, (byte)(index >> (8 * i)));
            }

            Rgb[] top = _passengerColours[0];
            Rgb[] bottom = _passengerColours[1];
            Rgb[] skin = _passengerColours[2];
            Rgb[] hair = _passengerColours[3];
            Rgb[] bag = _passengerColours[4];
            return new Paint(
                top[Fnv(h, 0) % (uint)top.Length],
                bottom[Fnv(h, 1) % (uint)bottom.Length],
                skin[Fnv(h, 2) % (uint)skin.Length],
                hair[Fnv(h, 3) % (uint)hair.Length],
                bag[Fnv(h, 4) % (uint)bag.Length],
                0);
        }

        private static uint Fnv(uint h, byte b)
        {
            unchecked
            {
                return (h ^ b) * 0x01000193U;
            }
        }

        // Agent k sits on a grid of pitch AgentSize filled row by row; rows wrap so every dot stays inside the box.
        private WorldPoint AgentPosition(in FlowNodeBox b, int k)
        {
            float pitch = _agentSize;
            float width = b.MaxX - b.MinX;
            float height = b.MaxY - b.MinY;
            int columns = Math.Max(1, (int)(width / pitch));
            int rows = Math.Max(1, (int)(height / pitch));
            int slot = k % (columns * rows);
            float x = Math.Min((((slot % columns) + 0.5f) * pitch), width);
            float y = Math.Min((((slot / columns) + 0.5f) * pitch), height);
            return new WorldPoint(b.MinX + x, b.MinY + y);
        }

        private void EmitAircraft()
        {
            if (_airside == null)
            {
                return;
            }

            IReadOnlyList<FlightId> flights = _airside.TrackedFlights();
            for (int i = 0; i < flights.Count; i++)
            {
                if (!_airside.TryGetTrack(flights[i], out AircraftTrack track))
                {
                    continue;
                }

                ColourRole colour;
                switch (track.Phase)
                {
                    case AircraftLegPhase.HeldForRunway:
                    case AircraftLegPhase.HeldOnTaxiway:
                        colour = ColourRole.AircraftHolding;
                        break;
                    case AircraftLegPhase.OnRunway:
                    case AircraftLegPhase.Taxiing:
                        colour = ColourRole.AircraftMoving;
                        break;
                    case AircraftLegPhase.OnStand:
                    case AircraftLegPhase.AwaitingPushbackClearance:
                        colour = ColourRole.AircraftOnStand;
                        break;
                    default:
                        continue;
                }

                if (!TryAircraftPosition(track, out WorldPoint at))
                {
                    continue;
                }

                VisualId visual = VisualId.AircraftC;
                Livery livery = _defaultLivery;
                if (_schedule != null && _schedule.TryGetFlight(track.Flight, out FlightRecord flight))
                {
                    if (_aircraftVisual.TryGetValue(flight.AircraftType, out VisualId mapped))
                    {
                        visual = mapped;
                    }

                    livery = LiveryOf(flight.Airline);
                }

                var paint = new Paint(livery.Fuselage, livery.Tail, livery.Cheatline, livery.Engines, livery.Logo, (byte)livery.Mark);
                _buffer.Add(Prim(
                    PrimitiveKind.Dot,
                    DrawLayer.Aircraft,
                    colour,
                    visual,
                    at,
                    default,
                    _aircraftSize,
                    AircraftFacing(track),
                    paint,
                    SourceKind.Aircraft,
                    track.Flight.Value,
                    0));
            }
        }

        // The airline's entry, found by binary search over the ascending ids, else the default livery.
        private Livery LiveryOf(AirlineId airline)
        {
            int lo = 0;
            int hi = _airlines.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                uint v = _airlines[mid].Airline.Value;
                if (v == airline.Value)
                {
                    return _airlines[mid].Livery;
                }

                if (v < airline.Value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return _defaultLivery;
        }

        // 15 §15.5: on an edge, interpolate from AtNode (the entry node) to the edge's other endpoint; else AtNode; else off-graph.
        private bool TryAircraftPosition(in AircraftTrack track, out WorldPoint at)
        {
            at = default;
            if (!track.AtNode.HasValue || !_nodePositions.TryGetValue(track.AtNode.Value.Value, out IntPoint fromPoint))
            {
                return false;
            }

            var from = new WorldPoint(fromPoint.X, fromPoint.Y);
            if (!track.OnEdge.HasValue)
            {
                at = from;
                return true;
            }

            if (!_edgeIndex.TryGetValue(track.OnEdge.Value.Value, out int index))
            {
                return false;
            }

            EdgeShape e = _edges[index];
            ushort otherNode = track.AtNode.Value.Value == e.From ? e.To : e.From;
            if (!_nodePositions.TryGetValue(otherNode, out IntPoint toPoint))
            {
                return false;
            }

            var to = new WorldPoint(toPoint.X, toPoint.Y);
            float t = track.EdgeProgress.Raw / FixedOne;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            at = new WorldPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            return true;
        }

        // 15 §15.16 "Aircraft facing": the first of five rules that applies, as an exact integer vector.
        private WorldPoint AircraftFacing(in AircraftTrack track)
        {
            if (!track.AtNode.HasValue || !_nodePositions.TryGetValue(track.AtNode.Value.Value, out IntPoint at))
            {
                return default;
            }

            if (track.OnEdge.HasValue)
            {
                if (_edgeIndex.TryGetValue(track.OnEdge.Value.Value, out int index))
                {
                    EdgeShape e = _edges[index];
                    ushort other = track.AtNode.Value.Value == e.From ? e.To : e.From;
                    if (_nodePositions.TryGetValue(other, out IntPoint to))
                    {
                        return Pt((long)to.X - at.X, (long)to.Y - at.Y);
                    }
                }

                return default;
            }

            switch (track.Phase)
            {
                case AircraftLegPhase.OnStand:
                case AircraftLegPhase.AwaitingPushbackClearance:
                    IntPoint v = NoseIn(track.AtNode.Value.Value);
                    return Pt(v.X, v.Y);
                case AircraftLegPhase.OnRunway:
                case AircraftLegPhase.HeldForRunway:
                    return RunwayFacing(track);
                case AircraftLegPhase.HeldOnTaxiway:
                case AircraftLegPhase.Taxiing:
                    return DestinationFacing(track, at);
                default:
                    return default;
            }
        }

        // Rule 3: along the runway, away from its threshold.
        private WorldPoint RunwayFacing(in AircraftTrack track)
        {
            if (!track.Runway.HasValue
                || !_runwayGeometry.TryGetValue(track.Runway.Value.Value, out RunwayGeometry g)
                || !_runwayDefs.TryGetValue(track.Runway.Value.Value, out RunwayDef def)
                || !_nodePositions.TryGetValue(def.ThresholdNode.Value, out IntPoint threshold))
            {
                return default;
            }

            return ActiveEndIsZero(g, threshold)
                ? Pt((long)g.X1 - g.X0, (long)g.Y1 - g.Y0)
                : Pt((long)g.X0 - g.X1, (long)g.Y0 - g.Y1);
        }

        // Rule 4: toward the destination in a straight line: a departure's runway threshold, an arrival's stand node.
        private WorldPoint DestinationFacing(in AircraftTrack track, IntPoint at)
        {
            ushort destination;
            if (track.Kind == MovementKind.Departure)
            {
                if (!track.Runway.HasValue || !_runwayDefs.TryGetValue(track.Runway.Value.Value, out RunwayDef def))
                {
                    return default;
                }

                destination = def.ThresholdNode.Value;
            }
            else
            {
                if (!track.Stand.HasValue || !TryStandNode(track.Stand.Value, out destination))
                {
                    return default;
                }
            }

            return _nodePositions.TryGetValue(destination, out IntPoint to)
                ? Pt((long)to.X - at.X, (long)to.Y - at.Y)
                : default;
        }

        private bool TryStandNode(StandId stand, out ushort node)
        {
            for (int i = 0; i < _stands.Length; i++)
            {
                if (_stands[i].Id.Value == stand.Value)
                {
                    node = _stands[i].Node;
                    return true;
                }
            }

            node = 0;
            return false;
        }

        // An integer position or vector. Positions are int32; vectors are differences of them, so long.
        private readonly struct IntPoint
        {
            public IntPoint(long x, long y)
            {
                X = x;
                Y = y;
            }

            public long X { get; }

            public long Y { get; }
        }

        private readonly struct EdgeShape
        {
            public EdgeShape(ushort id, ushort from, ushort to)
            {
                Id = id;
                From = from;
                To = to;
            }

            public ushort Id { get; }

            public ushort From { get; }

            public ushort To { get; }
        }

        private readonly struct StandShape
        {
            public StandShape(StandId id, ushort node)
            {
                Id = id;
                Node = node;
            }

            public StandId Id { get; }

            public ushort Node { get; }
        }
    }
}
