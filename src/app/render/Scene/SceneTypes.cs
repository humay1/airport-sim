using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Render
{
    /// <summary>A point in world units, +Y up the screen. Spec: 15 §15.9.</summary>
    public readonly struct WorldPoint
    {
        /// <summary>World X.</summary>
        public float X { get; }

        /// <summary>World Y.</summary>
        public float Y { get; }

        /// <summary>Constructs the point.</summary>
        public WorldPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>The camera: view rectangle is Centre ± (ViewHeight × Aspect / 2, ViewHeight / 2). Spec: 15 §15.9.</summary>
    public readonly struct CameraView
    {
        /// <summary>Centre of the view.</summary>
        public WorldPoint Centre { get; }

        /// <summary>Height of the view in world units, greater than zero.</summary>
        public float ViewHeight { get; }

        /// <summary>Width over height, greater than zero.</summary>
        public float Aspect { get; }

        /// <summary>Constructs the camera.</summary>
        public CameraView(WorldPoint centre, float viewHeight, float aspect)
        {
            Centre = centre;
            ViewHeight = viewHeight;
            Aspect = aspect;
        }
    }

    /// <summary>Draw layers, in draw order. Spec: 15 §15.9.</summary>
    public enum DrawLayer
    {
        /// <summary>Runways.</summary>
        Runway,

        /// <summary>Taxi edges.</summary>
        Taxiway,

        /// <summary>Stands.</summary>
        Stand,

        /// <summary>Landside node boxes.</summary>
        LandsideNode,

        /// <summary>Queue fill.</summary>
        QueueFill,

        /// <summary>Lane pips.</summary>
        Lane,

        /// <summary>Agent dots.</summary>
        Agent,

        /// <summary>Aircraft dots.</summary>
        Aircraft,
    }

    /// <summary>Shape of a primitive. Spec: 15 §15.9.</summary>
    public enum PrimitiveKind
    {
        /// <summary>Axis-aligned box from A (min) to B (max).</summary>
        Box,

        /// <summary>Segment from A to B, width Size.</summary>
        Segment,

        /// <summary>Dot centred on A, diameter Size.</summary>
        Dot,
    }

    /// <summary>The meaning of a colour; the backend owns the palette. Spec: 15 §15.9.</summary>
    public enum ColourRole
    {
        /// <summary>Runway with no queue.</summary>
        Runway,

        /// <summary>Runway with aircraft queued.</summary>
        RunwayQueued,

        /// <summary>Taxiway.</summary>
        Taxiway,

        /// <summary>Unoccupied stand.</summary>
        StandFree,

        /// <summary>Occupied stand.</summary>
        StandOccupied,

        /// <summary>Landside node box.</summary>
        LandsideNode,

        /// <summary>Queue fill.</summary>
        QueueFill,

        /// <summary>Agent dot.</summary>
        Agent,

        /// <summary>Aircraft rolling or taxiing.</summary>
        AircraftMoving,

        /// <summary>Aircraft held.</summary>
        AircraftHolding,

        /// <summary>Aircraft on a stand.</summary>
        AircraftOnStand,

        /// <summary>Open lane pip.</summary>
        LaneOpen,

        /// <summary>Closed lane pip.</summary>
        LaneClosed,
    }

    /// <summary>What a primitive was derived from. Spec: 15 §15.9.</summary>
    public enum SourceKind
    {
        /// <summary>A runway.</summary>
        Runway,

        /// <summary>A taxi edge.</summary>
        TaxiEdge,

        /// <summary>A stand.</summary>
        Stand,

        /// <summary>A flow node box.</summary>
        FlowNode,

        /// <summary>The queue fill of a flow node.</summary>
        QueueFill,

        /// <summary>An agent.</summary>
        Agent,

        /// <summary>An aircraft.</summary>
        Aircraft,

        /// <summary>A lane pip.</summary>
        Lane,
    }

    /// <summary>Identifies the source of a primitive; ordered by Kind, Id, Sub. Spec: 15 §15.5, §15.9.</summary>
    public readonly struct SourceRef
    {
        /// <summary>The kind of source.</summary>
        public SourceKind Kind { get; }

        /// <summary>The source's id value.</summary>
        public ulong Id { get; }

        /// <summary>Agent rank or lane index within its node; 0 otherwise.</summary>
        public int Sub { get; }

        /// <summary>Constructs the reference.</summary>
        public SourceRef(SourceKind kind, ulong id, int sub)
        {
            Kind = kind;
            Id = id;
            Sub = sub;
        }
    }

    /// <summary>One flat-colour shape in world coordinates. Spec: 15 §15.9.</summary>
    public readonly struct DrawPrimitive
    {
        /// <summary>Shape.</summary>
        public PrimitiveKind Kind { get; }

        /// <summary>Draw layer.</summary>
        public DrawLayer Layer { get; }

        /// <summary>Colour role.</summary>
        public ColourRole Colour { get; }

        /// <summary>Box: min corner. Segment: start. Dot: centre.</summary>
        public WorldPoint A { get; }

        /// <summary>Box: max corner. Segment: end. Dot: unused.</summary>
        public WorldPoint B { get; }

        /// <summary>Box: unused. Segment: width. Dot: diameter.</summary>
        public float Size { get; }

        /// <summary>Where it came from.</summary>
        public SourceRef Source { get; }

        /// <summary>Constructs the primitive.</summary>
        public DrawPrimitive(PrimitiveKind kind, DrawLayer layer, ColourRole colour, WorldPoint a, WorldPoint b, float size, SourceRef source)
        {
            Kind = kind;
            Layer = layer;
            Colour = colour;
            A = a;
            B = b;
            Size = size;
            Source = source;
        }
    }

    /// <summary>The graphics presets. Spec: 15 §15.14 (D10).</summary>
    public enum GraphicsPreset
    {
        /// <summary>Lowest cost.</summary>
        Low,

        /// <summary>Middle cost, the first-launch default.</summary>
        Medium,

        /// <summary>Highest cost, the Phase 1 behaviour.</summary>
        High,

        /// <summary>Any value changed by hand.</summary>
        Custom,
    }

    /// <summary>Presentation-only graphics settings. Spec: 15 §15.14.</summary>
    public readonly struct GraphicsSettings
    {
        /// <summary>The preset that produced the values.</summary>
        public GraphicsPreset Preset { get; }

        /// <summary>Whether agents are promoted and drawn.</summary>
        public bool DrawAgents { get; }

        /// <summary>Most agent dots per node, 1 to MAX_DRAWN_AGENTS_PER_NODE.</summary>
        public int MaxDrawnAgentsPerNode { get; }

        /// <summary>0 for uncapped, else 15 to 240 frames per second.</summary>
        public int FrameRateCap { get; }

        /// <summary>Render resolution, 50 to 100 percent.</summary>
        public int ResolutionScalePercent { get; }

        /// <summary>Whether the backend anti-aliases.</summary>
        public bool AntiAliasing { get; }

        /// <summary>Constructs the settings as given; use <c>RenderFactory.ValidateGraphics</c> to clamp.</summary>
        public GraphicsSettings(GraphicsPreset preset, bool drawAgents, int maxDrawnAgentsPerNode, int frameRateCap, int resolutionScalePercent, bool antiAliasing)
        {
            Preset = preset;
            DrawAgents = drawAgents;
            MaxDrawnAgentsPerNode = maxDrawnAgentsPerNode;
            FrameRateCap = frameRateCap;
            ResolutionScalePercent = resolutionScalePercent;
            AntiAliasing = antiAliasing;
        }
    }

    /// <summary>One built frame. Spec: 15 §15.9.</summary>
    public readonly struct RenderFrame
    {
        /// <summary>The tick the frame shows.</summary>
        public ulong Tick { get; }

        /// <summary>The camera it was built for.</summary>
        public CameraView Camera { get; }

        /// <summary>The draw list; valid until the next Build.</summary>
        public IReadOnlyList<DrawPrimitive> Primitives { get; }

        /// <summary>The settings the backend applies.</summary>
        public GraphicsSettings Graphics { get; }

        /// <summary>Constructs the frame.</summary>
        public RenderFrame(ulong tick, CameraView camera, IReadOnlyList<DrawPrimitive> primitives, GraphicsSettings graphics)
        {
            Tick = tick;
            Camera = camera;
            Primitives = primitives;
            Graphics = graphics;
        }
    }

    /// <summary>The read-only sim views the scene layer polls. Spec: 15 §15.9.</summary>
    public readonly struct RenderSources
    {
        /// <summary>The host, read for CurrentTick only.</summary>
        public ISimHost Host { get; }

        /// <summary>The airside system, or null when absent.</summary>
        public IAirsideSystem? Airside { get; }

        /// <summary>The flow system, or null when absent.</summary>
        public IFlowSystem? Flow { get; }

        /// <summary>Constructs the sources.</summary>
        public RenderSources(ISimHost host, IAirsideSystem? airside, IFlowSystem? flow)
        {
            Host = host;
            Airside = airside;
            Flow = flow;
        }
    }

    /// <summary>Builds the draw list. Spec: 15 §15.9.</summary>
    public interface ISceneBuilder
    {
        /// <summary>Returns the frame for the current tick; rebuilds only if tick, camera or graphics changed.</summary>
        RenderFrame Build(in CameraView camera, in GraphicsSettings graphics);
    }

    /// <summary>Drives render-driven promotion. Spec: 15 §15.7, §15.9.</summary>
    public interface IPromotionController
    {
        /// <summary>Promotes and demotes flow nodes for the camera and graphics; runs before Step.</summary>
        void Update(in CameraView camera, in GraphicsSettings graphics);
    }

    /// <summary>Game speeds; the value is the multiplier. Spec: 15 §15.8 (D4).</summary>
    public enum GameSpeed
    {
        /// <summary>Real time.</summary>
        X1 = 1,

        /// <summary>Twice real time.</summary>
        X2 = 2,

        /// <summary>Four times real time.</summary>
        X4 = 4,
    }

    /// <summary>Converts elapsed real time to ticks to Step. Spec: 15 §15.8.</summary>
    public interface ITickPacer
    {
        /// <summary>Ticks to Step this frame. Throws ArgumentOutOfRangeException on a negative elapsed or unknown speed.</summary>
        uint Advance(long elapsedRealMicroseconds, bool paused, GameSpeed speed);
    }
}
