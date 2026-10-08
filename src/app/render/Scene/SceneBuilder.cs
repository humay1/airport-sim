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
        private readonly Dictionary<ushort, RunwayFrame> _runwayFrames = new Dictionary<ushort, RunwayFrame>();
        private readonly ushort[] _runwayOrder; // ascending RunwayId of the airside layout
        private readonly WalkwayShape[] _walkways;
        private readonly int[] _boxWalkway; // index into _walkways per box, or -1
        private readonly BridgeWalk[] _bridgeWalks;

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
        private long _subTick;
        private double _tau;
        private double _alpha;
        private bool _predictedKnown;
        private int _predicted; // index into _runwayOrder, or -1
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
            _walkways = BuildWalkways(layout, _boxes, out _boxWalkway);
            _bridgeWalks = BuildBridgeWalks(layout);

            if (_airside == null)
            {
                _runwayOrder = Array.Empty<ushort>();
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
            _runwayOrder = new ushort[airside.Runways.Count];
            for (int i = 0; i < airside.Runways.Count; i++)
            {
                _runwayDefs[airside.Runways[i].Id.Value] = airside.Runways[i];
                _runwayOrder[i] = airside.Runways[i].Id.Value;
            }

            Array.Sort(_runwayOrder);

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
            BuildRunwayFrames();
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
            return Build(camera, graphics, 0L);
        }

        public RenderFrame Build(in CameraView camera, in GraphicsSettings graphics, long subTickMicroseconds)
        {
            if (subTickMicroseconds < 0 || subTickMicroseconds >= RenderConstants.REAL_MICROSECONDS_PER_TICK_1X)
            {
                throw new ArgumentOutOfRangeException(nameof(subTickMicroseconds), "the sub-tick must be in [0, REAL_MICROSECONDS_PER_TICK_1X)");
            }

            ulong tick = _host.CurrentTick;
            if (_hasFrame && tick == _tick && subTickMicroseconds == _subTick && SameCamera(camera) && SameGraphics(graphics))
            {
                return _frame;
            }

            // 15 §15.19: tau, in double; alpha is the elapsed fraction of the next tick.
            _alpha = subTickMicroseconds / 100000.0;
            _tau = ((double)tick - 1) + _alpha;
            _predictedKnown = false;

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
            EmitBridgeWalkers(camera, graphics);
            EmitAircraft();

            _tick = tick;
            _subTick = subTickMicroseconds;
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

        private static DrawPrimitive Prim(PrimitiveKind kind, DrawLayer layer, ColourRole colour, VisualId visual, WorldPoint a, WorldPoint b, float size, WorldPoint facing, Paint paint, SourceKind source, ulong id, int sub, float elevation = 0f)
        {
            return new DrawPrimitive(kind, layer, colour, visual, a, b, size, facing, paint, new SourceRef(source, id, sub), elevation);
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
                int walkway = _boxWalkway[i];
                bool haveCohort = false;
                ulong cohort = 0;
                double progress = 0;
                for (int k = 0; k < n; k++)
                {
                    PassengerRef r = agents[k].Ref;
                    uint hash = PassengerHash(r.Cohort.Value, r.Index);
                    WorldPoint at;
                    WorldPoint facing = default;
                    if (walkway >= 0)
                    {
                        // 15 §15.21: one TryGetCohort per distinct cohort; the list is sorted by cohort.
                        if (!haveCohort || r.Cohort.Value != cohort)
                        {
                            haveCohort = true;
                            cohort = r.Cohort.Value;
                            progress = CohortProgress(r.Cohort);
                        }

                        WalkwayShape w = _walkways[walkway];
                        at = WalkwayPosition(w, progress, Fnv(hash, 5));
                        facing = w.Facing;
                    }
                    else
                    {
                        at = AgentPosition(b, k);
                    }

                    _buffer.Add(Prim(
                        PrimitiveKind.Dot,
                        DrawLayer.Agent,
                        ColourRole.Agent,
                        VisualId.Passenger,
                        at,
                        default,
                        _agentSize,
                        facing,
                        PaintFromHash(hash),
                        SourceKind.Agent,
                        b.Node.Value,
                        k));
                }
            }
        }

        // 15 §15.21 "The cohort's progress".
        private double CohortProgress(CohortId id)
        {
            if (!_flow!.TryGetCohort(id, out PassengerCohort c))
            {
                return 0;
            }

            if (c.DueAt <= c.EnteredNodeAt)
            {
                return 1;
            }

            double p = (_tau - (double)c.EnteredNodeAt) / (double)(c.DueAt - c.EnteredNodeAt);
            return p < 0 ? 0 : (p > 1 ? 1 : p);
        }

        // 15 §15.21 "Placement", in double and converted to float once.
        private WorldPoint WalkwayPosition(in WalkwayShape w, double p, uint h)
        {
            double e = ((((double)(h % 1024U)) / 1023.0) - 0.5) * Math.Max(0.0, w.Width - (double)_agentSize);
            double a = (((double)((h >> 10) % 1024U)) / 1023.0) * Math.Min(w.Length, 4.0 * w.Width);
            double s = (p * w.Length) - a;
            s = s < 0 ? 0 : (s > w.Length ? w.Length : s);
            double x = (w.X0 + (w.DirX * s)) + (w.NormX * e);
            double y = (w.Y0 + (w.DirY * s)) + (w.NormY * e);
            return new WorldPoint((float)x, (float)y);
        }

        // 15 §15.21 "Walkers on jet bridges (boarding)".
        private void EmitBridgeWalkers(in CameraView camera, in GraphicsSettings graphics)
        {
            if (_airside == null || _flow == null || !graphics.DrawAgents || camera.ViewHeight > (float)RenderConstants.AGENT_ZOOM_THRESHOLD)
            {
                return;
            }

            float halfW = camera.ViewHeight * camera.Aspect / 2f;
            float halfH = camera.ViewHeight / 2f;
            for (int i = 0; i < _bridgeWalks.Length; i++)
            {
                BridgeWalk b = _bridgeWalks[i];
                if (!(b.MaxX >= camera.Centre.X - halfW && b.MinX <= camera.Centre.X + halfW && b.MaxY >= camera.Centre.Y - halfH && b.MinY <= camera.Centre.Y + halfH))
                {
                    continue;
                }

                if (!_airside.TryGetStand(b.Stand, out StandState stand) || !stand.Occupant.HasValue)
                {
                    continue;
                }

                FlightId occupant = stand.Occupant.Value;
                if (!_airside.TryGetTrack(occupant, out AircraftTrack track) || track.Phase != AircraftLegPhase.OnStand)
                {
                    continue;
                }

                FlightId boarding;
                if (track.Kind == MovementKind.Departure)
                {
                    boarding = occupant;
                }
                else if (_schedule != null && _schedule.TryGetFlight(occupant, out FlightRecord record) && record.HasRotation)
                {
                    boarding = record.Rotation;
                }
                else
                {
                    continue;
                }

                int atGate = _flow.PopulationForFlight(boarding, FlowDirection.Departing) - (_flow.TryGetOutstanding(boarding, out OutstandingPassengers outstanding) ? outstanding.Count : 0);
                if (atGate <= 0)
                {
                    continue;
                }

                int nw = atGate < RenderConstants.MAX_BRIDGE_WALKERS ? atGate : RenderConstants.MAX_BRIDGE_WALKERS;
                if (graphics.MaxDrawnAgentsPerNode < nw)
                {
                    nw = graphics.MaxDrawnAgentsPerNode;
                }

                double dx = (double)b.X1 - b.X0;
                double dy = (double)b.Y1 - b.Y0;
                WorldPoint facing = Pt((long)b.X1 - b.X0, (long)b.Y1 - b.Y0);
                for (int k = 0; k < nw; k++)
                {
                    double c = (_tau / RenderConstants.BRIDGE_WALK_TICKS) + (k / (double)nw);
                    double m = Math.Floor(c);
                    double f = c - m;
                    long mL = (long)Math.Min(m, 9007199254740992.0);
                    int j = unchecked((int)((mL * RenderConstants.MAX_BRIDGE_WALKERS) + k));
                    _buffer.Add(Prim(
                        PrimitiveKind.Dot,
                        DrawLayer.Agent,
                        ColourRole.Agent,
                        VisualId.Passenger,
                        new WorldPoint((float)(b.X0 + (dx * f)), (float)(b.Y0 + (dy * f))),
                        default,
                        _agentSize,
                        facing,
                        PaintFromHash(PassengerHash(boarding.Value, j)),
                        SourceKind.BridgePassenger,
                        b.Id,
                        k));
                }
            }
        }

        // 15 §15.16 "Passenger paint": FNV-1a-32 over an id (8 bytes LE) and an index (4 bytes LE); the region byte comes after.
        private static uint PassengerHash(ulong id, int index)
        {
            uint h = 0x811C9DC5U;
            for (int i = 0; i < 8; i++)
            {
                h = Fnv(h, (byte)(id >> (8 * i)));
            }

            uint u = unchecked((uint)index);
            for (int i = 0; i < 4; i++)
            {
                h = Fnv(h, (byte)(u >> (8 * i)));
            }

            return h;
        }

        private Paint PaintFromHash(uint h)
        {
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
                    case AircraftLegPhase.AwaitingApproach:
                        colour = ColourRole.AircraftMoving;
                        break;
                    case AircraftLegPhase.OnStand:
                    case AircraftLegPhase.AwaitingPushbackClearance:
                        colour = ColourRole.AircraftOnStand;
                        break;
                    default:
                        continue;
                }

                float elevation = 0f;
                WorldPoint facing;
                WorldPoint at;
                if (track.AtNode.HasValue || track.OnEdge.HasValue)
                {
                    // An approach is off the graph by definition; a track claiming a node is not drawn (15 §15.5).
                    if (track.Phase == AircraftLegPhase.AwaitingApproach || !TryAircraftPosition(track, out at))
                    {
                        continue;
                    }

                    facing = AircraftFacing(track);
                }
                else if (!TryOffGraph(track, out at, out elevation, out facing))
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
                    facing,
                    paint,
                    SourceKind.Aircraft,
                    track.Flight.Value,
                    0,
                    elevation));
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
            // 15 §15.20: the sub-tick advances the factor along the edge's traversal time.
            double progress = track.EdgeProgress.Raw / 4294967296.0;
            long trav = unchecked((long)track.DueAt - (long)track.PhaseEnteredAt);
            if (trav > 0)
            {
                progress += _alpha / trav;
            }

            float t = (float)progress;
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

        // 15 §15.20 "The runway frame": per runway, from the layout alone, in double.
        private void BuildRunwayFrames()
        {
            for (int i = 0; i < _runways.Length; i++)
            {
                RunwayGeometry g = _runways[i];
                if (!_runwayDefs.TryGetValue(g.Runway.Value, out RunwayDef def)
                    || !_nodePositions.TryGetValue(def.ThresholdNode.Value, out IntPoint t)
                    || !_nodePositions.TryGetValue(def.ExitNode.Value, out IntPoint x))
                {
                    continue;
                }

                bool zero = ActiveEndIsZero(g, t);
                long nx = zero ? g.X0 : g.X1;
                long ny = zero ? g.Y0 : g.Y1;
                long fx = zero ? g.X1 : g.X0;
                long fy = zero ? g.Y1 : g.Y0;
                long dx = fx - nx;
                long dy = fy - ny;
                double lr = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
                if (lr == 0)
                {
                    continue;
                }

                double ux = dx / lr;
                double uy = dy / lr;
                double tdx = nx + (ux * (lr / 8));
                double tdy = ny + (uy * (lr / 8));
                double lox = nx + (ux * (lr * 3 / 4));
                double loy = ny + (uy * (lr * 3 / 4));
                _runwayFrames[g.Runway.Value] = new RunwayFrame(
                    t.X,
                    t.Y,
                    x.X,
                    x.Y,
                    ux,
                    uy,
                    tdx,
                    tdy,
                    lox,
                    loy,
                    tdx - (ux * RenderConstants.FINAL_FIX_M),
                    tdy - (uy * RenderConstants.FINAL_FIX_M),
                    tdx - (ux * RenderConstants.APPROACH_ENTRY_M),
                    tdy - (uy * RenderConstants.APPROACH_ENTRY_M),
                    Pt(dx, dy),
                    Pt(-dx, -dy));
            }
        }

        // The predicted runway of an approaching arrival (15 §15.20): the first in ascending id with the smallest queue; once per rebuild.
        private bool TryPredictedRunway(out RunwayFrame frame)
        {
            if (!_predictedKnown)
            {
                _predictedKnown = true;
                _predicted = -1;
                int best = 0;
                for (int i = 0; i < _runwayOrder.Length; i++)
                {
                    int q = _airside!.RunwayQueueLength(new RunwayId(_runwayOrder[i]));
                    if (_predicted < 0 || q < best)
                    {
                        _predicted = i;
                        best = q;
                    }
                }
            }

            if (_predicted >= 0 && _runwayFrames.TryGetValue(_runwayOrder[_predicted], out frame))
            {
                return true;
            }

            frame = default;
            return false;
        }

        // 15 §15.20 "Off-graph tracks": the row that matches the phase and kind, else not drawn.
        private bool TryOffGraph(in AircraftTrack track, out WorldPoint at, out float elevation, out WorldPoint facing)
        {
            at = default;
            elevation = 0f;
            facing = default;
            RunwayFrame r;
            if (track.Phase == AircraftLegPhase.AwaitingApproach)
            {
                if (track.Kind != MovementKind.Arrival || _tau < (double)track.DueAt - RenderConstants.APPROACH_TICKS || !TryPredictedRunway(out r))
                {
                    return false;
                }

                double v = Clamp01((_tau - ((double)track.DueAt - RenderConstants.APPROACH_TICKS)) / RenderConstants.APPROACH_TICKS);
                double px = r.AeX + ((r.FfX - r.AeX) * v);
                double py = r.AeY + ((r.FfY - r.AeY) * v);
                at = new WorldPoint((float)px, (float)py);
                elevation = (float)(Dist(px, py, r.TdX, r.TdY) / RenderConstants.GLIDE_RATIO);
                facing = r.FDep;
                return true;
            }

            if (!track.Runway.HasValue || !_runwayFrames.TryGetValue(track.Runway.Value.Value, out r))
            {
                return false;
            }

            if (track.Phase == AircraftLegPhase.HeldForRunway && track.Kind == MovementKind.Arrival)
            {
                // The square hold, flown from the final fix to the left of the outbound direction.
                double s = Math.Max(0.0, _tau - (double)track.PhaseEnteredAt) / RenderConstants.HOLD_LEG_TICKS;
                double fl = Math.Floor(s);
                int leg = (int)(fl % 4.0);
                double frac = s - fl;
                double lx = r.UY;
                double ly = -r.UX;
                const double side = RenderConstants.HOLD_LEG_M;
                HoldCorner(r, lx, ly, side, leg, out double ax, out double ay);
                HoldCorner(r, lx, ly, side, (leg + 1) % 4, out double bx, out double by);
                at = new WorldPoint((float)(ax + ((bx - ax) * frac)), (float)(ay + ((by - ay) * frac)));
                elevation = (float)((double)RenderConstants.FINAL_FIX_M / RenderConstants.GLIDE_RATIO);
                WorldPoint g = r.FArr;
                switch (leg)
                {
                    case 0:
                        facing = new WorldPoint(0f - g.Y, g.X);
                        break;
                    case 1:
                        facing = g;
                        break;
                    case 2:
                        facing = new WorldPoint(g.Y, 0f - g.X);
                        break;
                    default:
                        facing = r.FDep;
                        break;
                }

                return true;
            }

            if (track.Phase != AircraftLegPhase.OnRunway)
            {
                return false;
            }

            double o = (double)unchecked((long)track.DueAt - (long)track.PhaseEnteredAt);
            double w = o <= 0 ? 1.0 : Clamp01((_tau - (double)track.PhaseEnteredAt) / o);
            facing = r.FDep;
            if (track.Kind == MovementKind.Arrival)
            {
                if (w <= 0.5)
                {
                    double v = 2 * w;
                    double px = r.FfX + ((r.TdX - r.FfX) * v);
                    double py = r.FfY + ((r.TdY - r.FfY) * v);
                    at = new WorldPoint((float)px, (float)py);
                    elevation = (float)(Dist(px, py, r.TdX, r.TdY) / RenderConstants.GLIDE_RATIO);
                }
                else
                {
                    double v = (2 * w) - 1;
                    double k = 1 - ((1 - v) * (1 - v));
                    at = new WorldPoint((float)(r.TdX + ((r.XX - r.TdX) * k)), (float)(r.TdY + ((r.XY - r.TdY) * k)));
                }

                return true;
            }

            if (track.Kind != MovementKind.Departure)
            {
                return false;
            }

            if (w <= 0.5)
            {
                double v = 2 * w;
                double vv = v * v;
                at = new WorldPoint((float)(r.TX + ((r.LoX - r.TX) * vv)), (float)(r.TY + ((r.LoY - r.TY) * vv)));
            }
            else
            {
                double v = (2 * w) - 1;
                double d = RenderConstants.CLIMB_OUT_M * v;
                at = new WorldPoint((float)(r.LoX + (r.UX * d)), (float)(r.LoY + (r.UY * d)));
                elevation = (float)(d / RenderConstants.CLIMB_RATIO);
            }

            return true;
        }

        // C_k of the hold: C0 = FF, C1 = FF + l S, C2 = C1 - u S, C3 = FF - u S.
        private static void HoldCorner(in RunwayFrame r, double lx, double ly, double side, int k, out double x, out double y)
        {
            switch (k)
            {
                case 0:
                    x = r.FfX;
                    y = r.FfY;
                    break;
                case 1:
                    x = r.FfX + (lx * side);
                    y = r.FfY + (ly * side);
                    break;
                case 2:
                    x = (r.FfX + (lx * side)) - (r.UX * side);
                    y = (r.FfY + (ly * side)) - (r.UY * side);
                    break;
                default:
                    x = r.FfX - (r.UX * side);
                    y = r.FfY - (r.UY * side);
                    break;
            }
        }

        private static double Clamp01(double x)
        {
            return x < 0 ? 0 : (x > 1 ? 1 : x);
        }

        private static double Dist(double ax, double ay, double bx, double by)
        {
            return Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
        }

        private static WalkwayShape[] BuildWalkwayShapes(in RenderLayout layout)
        {
            var list = new List<LayoutWalkway>();
            if (layout.Walkways != null)
            {
                for (int i = 0; i < layout.Walkways.Count; i++)
                {
                    list.Add(layout.Walkways[i]);
                }
            }

            list.Sort((a, b) => a.Node.Value.CompareTo(b.Node.Value));
            var shapes = new WalkwayShape[list.Count];
            for (int i = 0; i < shapes.Length; i++)
            {
                shapes[i] = new WalkwayShape(list[i]);
            }

            return shapes;
        }

        private static WalkwayShape[] BuildWalkways(in RenderLayout layout, FlowNodeBox[] boxes, out int[] boxWalkway)
        {
            WalkwayShape[] shapes = BuildWalkwayShapes(layout);
            boxWalkway = new int[boxes.Length];
            for (int b = 0; b < boxes.Length; b++)
            {
                boxWalkway[b] = -1;
                for (int i = 0; i < shapes.Length; i++)
                {
                    if (shapes[i].Node == boxes[b].Node.Value && shapes[i].Valid)
                    {
                        boxWalkway[b] = i;
                        break;
                    }
                }
            }

            return shapes;
        }

        private static BridgeWalk[] BuildBridgeWalks(in RenderLayout layout)
        {
            var list = new List<LayoutBridge>();
            if (layout.Bridges != null)
            {
                for (int i = 0; i < layout.Bridges.Count; i++)
                {
                    if (layout.Bridges[i].Stand.HasValue)
                    {
                        list.Add(layout.Bridges[i]);
                    }
                }
            }

            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            var walks = new BridgeWalk[list.Count];
            for (int i = 0; i < walks.Length; i++)
            {
                walks[i] = new BridgeWalk(list[i]);
            }

            return walks;
        }

        private readonly struct RunwayFrame
        {
            public RunwayFrame(long tx, long ty, long xx, long xy, double ux, double uy, double tdx, double tdy, double lox, double loy, double ffx, double ffy, double aex, double aey, WorldPoint fDep, WorldPoint fArr)
            {
                TX = tx;
                TY = ty;
                XX = xx;
                XY = xy;
                UX = ux;
                UY = uy;
                TdX = tdx;
                TdY = tdy;
                LoX = lox;
                LoY = loy;
                FfX = ffx;
                FfY = ffy;
                AeX = aex;
                AeY = aey;
                FDep = fDep;
                FArr = fArr;
            }

            public double TX { get; }

            public double TY { get; }

            public double XX { get; }

            public double XY { get; }

            public double UX { get; }

            public double UY { get; }

            public double TdX { get; }

            public double TdY { get; }

            public double LoX { get; }

            public double LoY { get; }

            public double FfX { get; }

            public double FfY { get; }

            public double AeX { get; }

            public double AeY { get; }

            public WorldPoint FDep { get; }

            public WorldPoint FArr { get; }
        }

        // A walkway's constants, derived once: its length, unit direction and left normal (15 §15.21).
        private readonly struct WalkwayShape
        {
            public WalkwayShape(in LayoutWalkway w)
            {
                Node = w.Node.Value;
                Width = w.Width;
                X0 = w.X0;
                Y0 = w.Y0;
                double dx = (double)w.X1 - w.X0;
                double dy = (double)w.Y1 - w.Y0;
                Length = Math.Sqrt((dx * dx) + (dy * dy));
                Valid = Length > 0;
                DirX = Valid ? dx / Length : 0;
                DirY = Valid ? dy / Length : 0;
                NormX = -DirY;
                NormY = DirX;
                Facing = new WorldPoint((float)((long)w.X1 - w.X0), (float)((long)w.Y1 - w.Y0));
            }

            public uint Node { get; }

            public bool Valid { get; }

            public int Width { get; }

            public double X0 { get; }

            public double Y0 { get; }

            public double Length { get; }

            public double DirX { get; }

            public double DirY { get; }

            public double NormX { get; }

            public double NormY { get; }

            public WorldPoint Facing { get; }
        }

        private readonly struct BridgeWalk
        {
            public BridgeWalk(in LayoutBridge b)
            {
                Id = b.Id;
                Stand = b.Stand!.Value;
                X0 = b.X0;
                Y0 = b.Y0;
                X1 = b.X1;
                Y1 = b.Y1;
                MinX = Math.Min(b.X0, b.X1);
                MinY = Math.Min(b.Y0, b.Y1);
                MaxX = Math.Max(b.X0, b.X1);
                MaxY = Math.Max(b.Y0, b.Y1);
            }

            public uint Id { get; }

            public StandId Stand { get; }

            public int X0 { get; }

            public int Y0 { get; }

            public int X1 { get; }

            public int Y1 { get; }

            public int MinX { get; }

            public int MinY { get; }

            public int MaxX { get; }

            public int MaxY { get; }
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
