using System;
using System.Collections;
using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

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

    /// <summary>Turns polled sim state into a draw list. Spec: 15 §15.5, §15.6, §15.9, §15.14.</summary>
    internal sealed class SceneBuilder : ISceneBuilder
    {
        private const float FixedOne = 4294967296f; // 2^32, the Fx fraction

        private readonly ISimHost _host;
        private readonly IAirsideSystem? _airside;
        private readonly IFlowSystem? _flow;

        private readonly float _standSize;
        private readonly float _aircraftSize;
        private readonly float _agentSize;
        private readonly float _taxiwayWidth;

        private readonly RunwayGeometry[] _runways;
        private readonly EdgeShape[] _edges;
        private readonly StandShape[] _stands;
        private readonly FlowNodeBox[] _boxes;
        private readonly Dictionary<ushort, WorldPoint> _nodePositions = new Dictionary<ushort, WorldPoint>();
        private readonly Dictionary<ushort, int> _edgeIndex = new Dictionary<ushort, int>();

        // Per-rebuild scratch, one slot per box.
        private readonly int[] _population;
        private readonly int[] _laneCount; // -1 when TryGetLaneState rejects the node
        private readonly int[] _laneOpen;

        private readonly PrimitiveBuffer _buffer = new PrimitiveBuffer();

        private bool _hasFrame;
        private ulong _tick;
        private CameraView _camera;
        private GraphicsSettings _graphics;
        private RenderFrame _frame;

        public SceneBuilder(in RenderSources sources, in RenderLayout layout)
        {
            _host = sources.Host;
            _airside = sources.Airside;
            _flow = sources.Flow;
            _standSize = layout.StandSize;
            _aircraftSize = layout.AircraftSize;
            _agentSize = layout.AgentSize;
            _taxiwayWidth = layout.TaxiwayWidth;

            _boxes = _flow == null ? Array.Empty<FlowNodeBox>() : Promotion.SortedBoxes(layout);
            _population = new int[_boxes.Length];
            _laneCount = new int[_boxes.Length];
            _laneOpen = new int[_boxes.Length];

            if (_airside == null)
            {
                _runways = Array.Empty<RunwayGeometry>();
                _edges = Array.Empty<EdgeShape>();
                _stands = Array.Empty<StandShape>();
                return;
            }

            for (int i = 0; i < layout.TaxiNodes.Count; i++)
            {
                TaxiNodePosition p = layout.TaxiNodes[i];
                _nodePositions[p.Node.Value] = new WorldPoint(p.X, p.Y);
            }

            _runways = new RunwayGeometry[layout.Runways.Count];
            for (int i = 0; i < _runways.Length; i++)
            {
                _runways[i] = layout.Runways[i];
            }

            Array.Sort(_runways, (a, b) => a.Runway.Value.CompareTo(b.Runway.Value));

            AirsideLayout airside = _airside.Layout();

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
                _buffer.Add(new DrawPrimitive(
                    PrimitiveKind.Segment,
                    DrawLayer.Runway,
                    colour,
                    new WorldPoint(g.X0, g.Y0),
                    new WorldPoint(g.X1, g.Y1),
                    g.Width,
                    new SourceRef(SourceKind.Runway, g.Runway.Value, 0)));
            }
        }

        private void EmitTaxiways()
        {
            for (int i = 0; i < _edges.Length; i++)
            {
                EdgeShape e = _edges[i];
                if (_nodePositions.TryGetValue(e.From, out WorldPoint a) && _nodePositions.TryGetValue(e.To, out WorldPoint b))
                {
                    _buffer.Add(new DrawPrimitive(
                        PrimitiveKind.Segment,
                        DrawLayer.Taxiway,
                        ColourRole.Taxiway,
                        a,
                        b,
                        _taxiwayWidth,
                        new SourceRef(SourceKind.TaxiEdge, e.Id, 0)));
                }
            }
        }

        private void EmitStands()
        {
            if (_airside == null)
            {
                return;
            }

            float half = _standSize / 2f;
            for (int i = 0; i < _stands.Length; i++)
            {
                StandShape s = _stands[i];
                if (!_nodePositions.TryGetValue(s.Node, out WorldPoint c) || !_airside.TryGetStand(s.Id, out StandState state))
                {
                    continue;
                }

                _buffer.Add(new DrawPrimitive(
                    PrimitiveKind.Box,
                    DrawLayer.Stand,
                    state.Occupant.HasValue ? ColourRole.StandOccupied : ColourRole.StandFree,
                    new WorldPoint(c.X - half, c.Y - half),
                    new WorldPoint(c.X + half, c.Y + half),
                    0f,
                    new SourceRef(SourceKind.Stand, s.Id.Value, 0)));
            }
        }

        private void EmitLandsideBoxes()
        {
            for (int i = 0; i < _boxes.Length; i++)
            {
                FlowNodeBox b = _boxes[i];
                _buffer.Add(new DrawPrimitive(
                    PrimitiveKind.Box,
                    DrawLayer.LandsideNode,
                    ColourRole.LandsideNode,
                    new WorldPoint(b.MinX, b.MinY),
                    new WorldPoint(b.MaxX, b.MaxY),
                    0f,
                    new SourceRef(SourceKind.FlowNode, b.Node.Value, 0)));
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
                _buffer.Add(new DrawPrimitive(
                    PrimitiveKind.Box,
                    DrawLayer.QueueFill,
                    ColourRole.QueueFill,
                    new WorldPoint(b.MinX, b.MinY),
                    new WorldPoint(maxX, b.MaxY),
                    0f,
                    new SourceRef(SourceKind.QueueFill, b.Node.Value, 0)));
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
                    _buffer.Add(new DrawPrimitive(
                        PrimitiveKind.Dot,
                        DrawLayer.Lane,
                        k < _laneOpen[i] ? ColourRole.LaneOpen : ColourRole.LaneClosed,
                        new WorldPoint(x, y),
                        default,
                        _agentSize,
                        new SourceRef(SourceKind.Lane, b.Node.Value, k)));
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
                    _buffer.Add(new DrawPrimitive(
                        PrimitiveKind.Dot,
                        DrawLayer.Agent,
                        ColourRole.Agent,
                        AgentPosition(b, k),
                        default,
                        _agentSize,
                        new SourceRef(SourceKind.Agent, b.Node.Value, k)));
                }
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

                _buffer.Add(new DrawPrimitive(
                    PrimitiveKind.Dot,
                    DrawLayer.Aircraft,
                    colour,
                    at,
                    default,
                    _aircraftSize,
                    new SourceRef(SourceKind.Aircraft, track.Flight.Value, 0)));
            }
        }

        // 15 §15.5: on an edge, interpolate from AtNode (the entry node) to the edge's other endpoint; else AtNode; else off-graph.
        private bool TryAircraftPosition(in AircraftTrack track, out WorldPoint at)
        {
            at = default;
            if (!track.AtNode.HasValue || !_nodePositions.TryGetValue(track.AtNode.Value.Value, out WorldPoint from))
            {
                return false;
            }

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
            if (!_nodePositions.TryGetValue(otherNode, out WorldPoint to))
            {
                return false;
            }

            float t = track.EdgeProgress.Raw / FixedOne;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            at = new WorldPoint(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            return true;
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
