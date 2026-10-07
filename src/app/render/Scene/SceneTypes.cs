using System.Collections.Generic;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

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
        /// <summary>Aprons and buildings; the first layer, so they lie under everything else (Q-130).</summary>
        Ground,

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

        /// <summary>Runway markings (Q-130).</summary>
        RunwayMarking,

        /// <summary>Taxiway, stand and lead-in markings (Q-130).</summary>
        TaxiwayMarking,

        /// <summary>Paved apron (Q-130).</summary>
        Apron,

        /// <summary>Terminal, pier, control tower and jet bridge (Q-130).</summary>
        Building,
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

        /// <summary>A runway marking (Q-130).</summary>
        RunwayMarking,

        /// <summary>A taxi node's junction fill (Q-130).</summary>
        TaxiNode,

        /// <summary>A taxi edge's centreline (Q-130).</summary>
        TaxiCentreline,

        /// <summary>A layout apron (Q-130).</summary>
        Apron,

        /// <summary>A layout terminal, pier or control tower (Q-130).</summary>
        Building,

        /// <summary>A stand's lead-in (Q-130).</summary>
        StandMarking,

        /// <summary>A digit of a stand's number (Q-130).</summary>
        StandNumber,

        /// <summary>A layout jet bridge (Q-130).</summary>
        JetBridge,
    }

    /// <summary>What a primitive is, never how it is drawn. Spec: 15 §15.9, §15.16 (Q-130).</summary>
    public enum VisualId
    {
        /// <summary>Runway pavement.</summary>
        RunwaySurface,

        /// <summary>Runway edge lines.</summary>
        RunwayEdgeLines,

        /// <summary>Runway threshold bars.</summary>
        RunwayThreshold,

        /// <summary>One centreline dash.</summary>
        RunwayCentreDash,

        /// <summary>Taxiway pavement.</summary>
        TaxiwaySurface,

        /// <summary>Taxiway junction fill.</summary>
        TaxiwayJunction,

        /// <summary>Taxiway centreline.</summary>
        TaxiwayCentreline,

        /// <summary>Paved apron.</summary>
        Apron,

        /// <summary>Terminal building.</summary>
        TerminalBuilding,

        /// <summary>Pier.</summary>
        Pier,

        /// <summary>Control tower.</summary>
        ControlTower,

        /// <summary>Jet bridge.</summary>
        JetBridge,

        /// <summary>Stand pad.</summary>
        StandPad,

        /// <summary>Stand lead-in line.</summary>
        StandLeadIn,

        /// <summary>Painted digit 0.</summary>
        MarkingDigit0,

        /// <summary>Painted digit 1.</summary>
        MarkingDigit1,

        /// <summary>Painted digit 2.</summary>
        MarkingDigit2,

        /// <summary>Painted digit 3.</summary>
        MarkingDigit3,

        /// <summary>Painted digit 4.</summary>
        MarkingDigit4,

        /// <summary>Painted digit 5.</summary>
        MarkingDigit5,

        /// <summary>Painted digit 6.</summary>
        MarkingDigit6,

        /// <summary>Painted digit 7.</summary>
        MarkingDigit7,

        /// <summary>Painted digit 8.</summary>
        MarkingDigit8,

        /// <summary>Painted digit 9.</summary>
        MarkingDigit9,

        /// <summary>Landside zone.</summary>
        TerminalZone,

        /// <summary>Queue fill.</summary>
        QueueFill,

        /// <summary>Lane pip.</summary>
        LanePip,

        /// <summary>A passenger.</summary>
        Passenger,

        /// <summary>Aircraft of size category A.</summary>
        AircraftA,

        /// <summary>Aircraft of size category B.</summary>
        AircraftB,

        /// <summary>Aircraft of size category C.</summary>
        AircraftC,

        /// <summary>Aircraft of size category D.</summary>
        AircraftD,

        /// <summary>Aircraft of size category E.</summary>
        AircraftE,

        /// <summary>Aircraft of size category F.</summary>
        AircraftF,
    }

    /// <summary>An sRGB colour. Spec: 15 §15.9 (Q-130).</summary>
    public readonly struct Rgb
    {
        /// <summary>Red.</summary>
        public byte R { get; }

        /// <summary>Green.</summary>
        public byte G { get; }

        /// <summary>Blue.</summary>
        public byte B { get; }

        /// <summary>Constructs the colour.</summary>
        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>Per-instance region colours; all zero by default. Spec: 15 §15.9, §15.16 (Q-130).</summary>
    public readonly struct Paint
    {
        /// <summary>Region 0.</summary>
        public Rgb Region0 { get; }

        /// <summary>Region 1.</summary>
        public Rgb Region1 { get; }

        /// <summary>Region 2.</summary>
        public Rgb Region2 { get; }

        /// <summary>Region 3.</summary>
        public Rgb Region3 { get; }

        /// <summary>Region 4.</summary>
        public Rgb Region4 { get; }

        /// <summary>Aircraft: the logo mark as a byte; 0 otherwise.</summary>
        public byte Mark { get; }

        /// <summary>Constructs the paint.</summary>
        public Paint(Rgb region0, Rgb region1, Rgb region2, Rgb region3, Rgb region4, byte mark)
        {
            Region0 = region0;
            Region1 = region1;
            Region2 = region2;
            Region3 = region3;
            Region4 = region4;
            Mark = mark;
        }
    }

    /// <summary>The regions of an aircraft's Paint; the value is the region index. Spec: 15 §15.9.</summary>
    public enum AircraftRegion
    {
        /// <summary>Fuselage.</summary>
        Fuselage,

        /// <summary>Tail.</summary>
        Tail,

        /// <summary>Cheatline.</summary>
        Cheatline,

        /// <summary>Engines.</summary>
        Engines,

        /// <summary>Logo.</summary>
        Logo,
    }

    /// <summary>The regions of a passenger's Paint; the value is the region index. Spec: 15 §15.9.</summary>
    public enum PassengerRegion
    {
        /// <summary>Top.</summary>
        Top,

        /// <summary>Bottom.</summary>
        Bottom,

        /// <summary>Skin.</summary>
        Skin,

        /// <summary>Hair.</summary>
        Hair,

        /// <summary>Bag.</summary>
        Bag,
    }

    /// <summary>A livery's logo mark. Spec: 15 §15.9.</summary>
    public enum LogoMark
    {
        /// <summary>No mark.</summary>
        None,

        /// <summary>A disc.</summary>
        Disc,

        /// <summary>A ring.</summary>
        Ring,

        /// <summary>A chevron.</summary>
        Chevron,

        /// <summary>A star.</summary>
        Star,

        /// <summary>Bars.</summary>
        Bars,

        /// <summary>A diamond.</summary>
        Diamond,

        /// <summary>A crescent.</summary>
        Crescent,
    }

    /// <summary>The colours of one airline's aircraft. Spec: 15 §15.9 (Q-130).</summary>
    public readonly struct Livery
    {
        /// <summary>Fuselage colour.</summary>
        public Rgb Fuselage { get; }

        /// <summary>Tail colour.</summary>
        public Rgb Tail { get; }

        /// <summary>Cheatline colour.</summary>
        public Rgb Cheatline { get; }

        /// <summary>Engines colour.</summary>
        public Rgb Engines { get; }

        /// <summary>Logo colour.</summary>
        public Rgb Logo { get; }

        /// <summary>Logo mark.</summary>
        public LogoMark Mark { get; }

        /// <summary>Constructs the livery.</summary>
        public Livery(Rgb fuselage, Rgb tail, Rgb cheatline, Rgb engines, Rgb logo, LogoMark mark)
        {
            Fuselage = fuselage;
            Tail = tail;
            Cheatline = cheatline;
            Engines = engines;
            Logo = logo;
            Mark = mark;
        }
    }

    /// <summary>One airline's livery. Spec: 15 §15.9 (Q-130).</summary>
    public readonly struct AirlineLivery
    {
        /// <summary>The airline.</summary>
        public AirlineId Airline { get; }

        /// <summary>Its livery.</summary>
        public Livery Livery { get; }

        /// <summary>Constructs the entry.</summary>
        public AirlineLivery(AirlineId airline, Livery livery)
        {
            Airline = airline;
            Livery = livery;
        }
    }

    /// <summary>Liveries and passenger palettes, loaded from data/looks. Spec: 15 §15.9, §15.16 (Q-130).</summary>
    public readonly struct RenderLooks
    {
        /// <summary>The livery of an airline with no entry.</summary>
        public Livery DefaultLivery { get; }

        /// <summary>Per-airline liveries, ascending airline id, unique.</summary>
        public IReadOnlyList<AirlineLivery> Airlines { get; }

        /// <summary>Passenger top colours; non-empty.</summary>
        public IReadOnlyList<Rgb> Tops { get; }

        /// <summary>Passenger bottom colours; non-empty.</summary>
        public IReadOnlyList<Rgb> Bottoms { get; }

        /// <summary>Passenger skin colours; non-empty.</summary>
        public IReadOnlyList<Rgb> Skins { get; }

        /// <summary>Passenger hair colours; non-empty.</summary>
        public IReadOnlyList<Rgb> Hairs { get; }

        /// <summary>Passenger bag colours; non-empty.</summary>
        public IReadOnlyList<Rgb> Bags { get; }

        /// <summary>Constructs the looks.</summary>
        public RenderLooks(
            Livery defaultLivery,
            IReadOnlyList<AirlineLivery> airlines,
            IReadOnlyList<Rgb> tops,
            IReadOnlyList<Rgb> bottoms,
            IReadOnlyList<Rgb> skins,
            IReadOnlyList<Rgb> hairs,
            IReadOnlyList<Rgb> bags)
        {
            DefaultLivery = defaultLivery;
            Airlines = airlines;
            Tops = tops;
            Bottoms = bottoms;
            Skins = skins;
            Hairs = hairs;
            Bags = bags;
        }
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

        /// <summary>What is drawn (Q-130).</summary>
        public VisualId Visual { get; }

        /// <summary>Box: min corner. Segment: start. Dot: centre.</summary>
        public WorldPoint A { get; }

        /// <summary>Box: max corner. Segment: end. Dot: unused.</summary>
        public WorldPoint B { get; }

        /// <summary>Box: unused. Segment: width. Dot: diameter.</summary>
        public float Size { get; }

        /// <summary>Dot: the direction the visual's forward points, an exact integer-valued vector; (0,0) is +Y. Otherwise (0,0) (Q-130).</summary>
        public WorldPoint Facing { get; }

        /// <summary>Per-instance region colours; all zero unless the visual has regions (Q-130).</summary>
        public Paint Paint { get; }

        /// <summary>Where it came from.</summary>
        public SourceRef Source { get; }

        /// <summary>Constructs the primitive.</summary>
        public DrawPrimitive(PrimitiveKind kind, DrawLayer layer, ColourRole colour, VisualId visual, WorldPoint a, WorldPoint b, float size, WorldPoint facing, Paint paint, SourceRef source)
        {
            Kind = kind;
            Layer = layer;
            Colour = colour;
            Visual = visual;
            A = a;
            B = b;
            Size = size;
            Facing = facing;
            Paint = paint;
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

        /// <summary>The schedule, read for TryGetFlight only; null gives AircraftC and the default livery (Q-130).</summary>
        public IScheduleSystem? Schedule { get; }

        /// <summary>The content index, read once at construction; null gives AircraftC (Q-130).</summary>
        public IContentIndex? Content { get; }

        /// <summary>Constructs the sources with no schedule and no content (07 L10 kept constructor).</summary>
        public RenderSources(ISimHost host, IAirsideSystem? airside, IFlowSystem? flow)
            : this(host, airside, flow, null, null)
        {
        }

        /// <summary>Constructs the sources.</summary>
        public RenderSources(ISimHost host, IAirsideSystem? airside, IFlowSystem? flow, IScheduleSystem? schedule, IContentIndex? content)
        {
            Host = host;
            Airside = airside;
            Flow = flow;
            Schedule = schedule;
            Content = content;
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
