using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    // Shared helpers for the Q-130 scene tests, written from 15 §15.4, §15.5,
    // §15.9 and §15.16, 08 §8.11 and 11 §11.4 only.

    /// <summary>FNV-1a-32, offset 0x811C9DC5, prime 0x01000193 (15 §15.16, 11 §11.4).</summary>
    internal static class Fnv
    {
        public static uint Hash32(byte[] bytes)
        {
            uint h = 0x811C9DC5U;
            unchecked
            {
                foreach (byte b in bytes)
                {
                    h ^= b;
                    h *= 0x01000193U;
                }
            }

            return h;
        }

        /// <summary>An airline code's AirlineId: FNV-1a-32 over its UTF-8 bytes (11 §11.4).</summary>
        public static AirlineId Airline(string code)
        {
            return new AirlineId(Hash32(Encoding.UTF8.GetBytes(code)));
        }

        /// <summary>
        /// 15 §15.16's passenger hash h_r: Cohort.Value as 8 bytes little-endian,
        /// Index as 4 bytes little-endian two's complement, then the byte r.
        /// Built byte by byte so the test does not lean on the machine's order.
        /// </summary>
        public static uint Passenger(ulong cohort, int index, int region)
        {
            var bytes = new byte[13];
            for (int i = 0; i < 8; i++)
            {
                bytes[i] = (byte)(cohort >> (8 * i));
            }

            uint u = unchecked((uint)index);
            for (int i = 0; i < 4; i++)
            {
                bytes[8 + i] = (byte)(u >> (8 * i));
            }

            bytes[12] = (byte)region;
            return Hash32(bytes);
        }
    }

    internal static class ArtLooks
    {
        public static Rgb Colour(string hex)
        {
            Assert.True(hex.Length == 7 && hex[0] == '#', "test colour " + hex);
            return new Rgb(
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        public static Livery Livery(string fuselage, string tail, string cheatline, string engines, string logo, LogoMark mark)
        {
            return new Livery(Colour(fuselage), Colour(tail), Colour(cheatline), Colour(engines), Colour(logo), mark);
        }

        /// <summary>The Paint 15 §15.16 derives from a livery: AircraftRegion order, then the mark.</summary>
        public static Paint PaintOf(in Livery l)
        {
            return new Paint(l.Fuselage, l.Tail, l.Cheatline, l.Engines, l.Logo, (byte)l.Mark);
        }

        public static List<Rgb> Palette(int count, int seed)
        {
            var list = new List<Rgb>();
            for (int i = 0; i < count; i++)
            {
                list.Add(new Rgb((byte)(seed + i), (byte)(100 + (7 * i)), (byte)(200 - (3 * i))));
            }

            return list;
        }

        /// <summary>
        /// Looks with airlines 1..n (AirlineId values), each with its own
        /// colours and mark, a default livery unlike any of them, and passenger
        /// lists of 7, 5, 3, 11 and 13 entries, so every region's hash matters.
        /// </summary>
        public static RenderLooks WithAirlines(int n)
        {
            var airlines = new List<AirlineLivery>();
            for (int i = 1; i <= n; i++)
            {
                byte b = (byte)(i * 20);
                var livery = new Livery(new Rgb(b, 1, 1), new Rgb(1, b, 1), new Rgb(1, 1, b), new Rgb(b, b, 1), new Rgb(1, b, b), (LogoMark)(i % 8));
                airlines.Add(new AirlineLivery(new AirlineId((uint)i), livery));
            }

            return new RenderLooks(
                Livery("#101112", "#202122", "#303132", "#404142", "#505152", LogoMark.Crescent),
                airlines,
                Palette(7, 10),
                Palette(5, 40),
                Palette(3, 70),
                Palette(11, 100),
                Palette(13, 150));
        }
    }

    internal static class ArtContent
    {
        public static readonly string[] SizeIds = { "size_a", "size_b", "size_c", "size_d", "size_e", "size_f", "size_g" };

        /// <summary>
        /// Size categories size_a..size_g with ordinals 0..6, and aircraft
        /// types t0..t6 naming them in turn (08 §8.11a lets tests build
        /// definitions directly). t7 is deliberately absent.
        /// </summary>
        public static FakeContent AllSizes(CallGuard guard)
        {
            var c = new FakeContent(guard);
            for (int i = 0; i < SizeIds.Length; i++)
            {
                c.Size(SizeIds[i], i);
                c.Aircraft("t" + i.ToString(CultureInfo.InvariantCulture), SizeIds[i]);
            }

            return c;
        }
    }

    internal static class Art
    {
        public static List<DrawPrimitive> OfKind(IReadOnlyList<DrawPrimitive> all, SourceKind kind)
        {
            var list = new List<DrawPrimitive>();
            foreach (DrawPrimitive p in all)
            {
                if (p.Source.Kind == kind)
                {
                    list.Add(p);
                }
            }

            return list;
        }

        public static DrawPrimitive Single(IReadOnlyList<DrawPrimitive> all, SourceKind kind, ulong id, int sub)
        {
            var found = new List<DrawPrimitive>();
            foreach (DrawPrimitive p in all)
            {
                if (p.Source.Kind == kind && p.Source.Id == id && p.Source.Sub == sub)
                {
                    found.Add(p);
                }
            }

            Assert.True(found.Count == 1, "expected one " + kind + " primitive for id " + id + " sub " + sub + ", found " + found.Count + ": " + Prims.Show(Prims.Of(all, kind, id)));
            return found[0];
        }

        public static void AssertFacing(float x, float y, in DrawPrimitive p, string what)
        {
            Assert.True(
                p.Facing.X == x && p.Facing.Y == y,
                string.Format(CultureInfo.InvariantCulture, "{0}: Facing expected ({1:R},{2:R}), got ({3:R},{4:R}) on {5}", what, x, y, p.Facing.X, p.Facing.Y, Prims.Show(p)));
        }

        public static void AssertPaint(in Paint expected, in DrawPrimitive p, string what)
        {
            Assert.True(Prims.Show(expected) == Prims.Show(p.Paint), what + ": Paint expected " + Prims.Show(expected) + ", got " + Prims.Show(p.Paint) + " on " + Prims.Show(p));
        }

        public static void AssertZeroPaint(in DrawPrimitive p, string what)
        {
            AssertPaint(default(Paint), p, what);
        }

        /// <summary>A Dot with this visual, diameter and centre, whatever else it carries.</summary>
        public static void AssertDot(in DrawPrimitive p, VisualId visual, float x, float y, float size, string what)
        {
            Assert.True(p.Kind == PrimitiveKind.Dot, what + ": not a Dot: " + Prims.Show(p));
            Assert.True(p.Visual == visual, what + ": visual " + p.Visual + ", expected " + visual + ": " + Prims.Show(p));
            Prims.AssertPoint(x, y, p.A, what + " centre");
            Assert.True(p.Size == size, what + ": diameter " + p.Size.ToString("R", CultureInfo.InvariantCulture) + ", expected " + size.ToString("R", CultureInfo.InvariantCulture));
        }

        public static void AssertSegment(in DrawPrimitive p, VisualId visual, float x0, float y0, float x1, float y1, float width, string what)
        {
            Assert.True(p.Kind == PrimitiveKind.Segment, what + ": not a Segment: " + Prims.Show(p));
            Assert.True(p.Visual == visual, what + ": visual " + p.Visual + ", expected " + visual + ": " + Prims.Show(p));
            Prims.AssertPoint(x0, y0, p.A, what + " start");
            Prims.AssertPoint(x1, y1, p.B, what + " end");
            Assert.True(p.Size == width, what + ": width " + p.Size.ToString("R", CultureInfo.InvariantCulture) + ", expected " + width.ToString("R", CultureInfo.InvariantCulture));
            AssertFacing(0f, 0f, p, what + " (a Segment's Facing is (0,0))");
        }

        public static VisualId Digit(int d)
        {
            return (VisualId)((int)VisualId.MarkingDigit0 + d);
        }
    }

    /// <summary>
    /// A scene assembled per test: taxi nodes with or without positions,
    /// edges, runways (definition, geometry, or both), stands, scenery and
    /// flow boxes. Call Done() once, then set tracks, stands and queues on
    /// Airside, and build with the sources the test needs.
    /// </summary>
    internal sealed class ArtScene
    {
        public readonly CallGuard Guard = new CallGuard();
        public readonly FakeHost Host;
        public readonly FakeFlow Flow;
        public FakeAirside? Airside;
        public RenderLayout Layout;

        public int StandSize = 40;
        public int AircraftSize = 30;
        public int AgentSize = 2;
        public int TaxiwayWidth = 15;

        private readonly List<TaxiNodeDef> _nodes = new List<TaxiNodeDef>();
        private readonly List<TaxiNodePosition> _positions = new List<TaxiNodePosition>();
        private readonly List<TaxiEdgeDef> _edges = new List<TaxiEdgeDef>();
        private readonly List<RunwayDef> _runways = new List<RunwayDef>();
        private readonly List<RunwayGeometry> _geometry = new List<RunwayGeometry>();
        private readonly List<StandDef> _stands = new List<StandDef>();
        private readonly List<FlowNodeBox> _boxes = new List<FlowNodeBox>();
        private readonly List<LayoutArea> _areas = new List<LayoutArea>();
        private readonly List<LayoutBridge> _bridges = new List<LayoutBridge>();

        public ArtScene()
        {
            Host = new FakeHost(Guard, 3UL);
            Flow = new FakeFlow(Guard);
        }

        public ArtScene Node(ushort id, int x, int y, TaxiNodeKind kind = TaxiNodeKind.Junction)
        {
            _nodes.Add(new TaxiNodeDef(new TaxiNodeId(id), kind));
            _positions.Add(new TaxiNodePosition(new TaxiNodeId(id), x, y));
            return this;
        }

        /// <summary>A taxi node in the airside layout with no layout position.</summary>
        public ArtScene Unpositioned(ushort id, TaxiNodeKind kind = TaxiNodeKind.RunwayThreshold)
        {
            _nodes.Add(new TaxiNodeDef(new TaxiNodeId(id), kind));
            return this;
        }

        public ArtScene Edge(ushort id, ushort from, ushort to)
        {
            _edges.Add(new TaxiEdgeDef(new TaxiEdgeId(id), new TaxiNodeId(from), new TaxiNodeId(to), 10U, true));
            return this;
        }

        /// <summary>A RunwayDef and its geometry.</summary>
        public ArtScene Runway(ushort id, ushort threshold, int deg, int x0, int y0, int x1, int y1, int width)
        {
            RunwayDefOnly(id, threshold, deg);
            return Geometry(id, x0, y0, x1, y1, width);
        }

        public ArtScene RunwayDefOnly(ushort id, ushort threshold, int deg)
        {
            _runways.Add(new RunwayDef(new RunwayId(id), new TaxiNodeId(threshold), deg, 30, 10U));
            return this;
        }

        /// <summary>Layout geometry with no RunwayDef behind it.</summary>
        public ArtScene Geometry(ushort id, int x0, int y0, int x1, int y1, int width)
        {
            _geometry.Add(new RunwayGeometry(new RunwayId(id), x0, y0, x1, y1, width));
            return this;
        }

        public ArtScene Stand(ushort id, ushort node)
        {
            _stands.Add(new StandDef(new StandId(id), new TaxiNodeId(node), new ContentId("size_f"), new NodeId(9)));
            return this;
        }

        public ArtScene Box(uint node, int minX, int minY, int maxX, int maxY, int fill, int population = 0, LaneState? lanes = null)
        {
            _boxes.Add(new FlowNodeBox(new NodeId(node), minX, minY, maxX, maxY, fill));
            Flow.Node(node, population, lanes);
            return this;
        }

        public ArtScene Area(uint id, AreaKind kind, int minX, int minY, int maxX, int maxY)
        {
            _areas.Add(new LayoutArea(id, kind, minX, minY, maxX, maxY));
            return this;
        }

        public ArtScene Bridge(uint id, int x0, int y0, int x1, int y1, int width)
        {
            _bridges.Add(new LayoutBridge(id, x0, y0, x1, y1, width));
            return this;
        }

        public ArtScene Done()
        {
            Airside = new FakeAirside(Guard, new AirsideLayout(_runways, _nodes, _edges, _stands));
            Layout = new RenderLayout(_positions, _geometry, _boxes, StandSize, AircraftSize, AgentSize, TaxiwayWidth, _areas, _bridges);
            return this;
        }

        public RenderSources Sources(bool airside = true, bool flow = true, FakeSchedule? schedule = null, IContentIndex? content = null)
        {
            return new RenderSources(Host, airside ? Airside : null, flow ? Flow : null, schedule, content);
        }

        public ISceneBuilder Builder(bool airside = true, bool flow = true, FakeSchedule? schedule = null, IContentIndex? content = null)
        {
            return RenderFactory.CreateSceneBuilder(Sources(airside, flow, schedule, content), Layout);
        }

        public ISceneBuilder Builder(in RenderLooks looks, FakeSchedule? schedule = null, IContentIndex? content = null)
        {
            return RenderFactory.CreateSceneBuilder(Sources(true, true, schedule, content), Layout, looks);
        }

        /// <summary>A camera over everything this file builds, above the zoom threshold.</summary>
        public static CameraView Overview => Cam.At(0f, 0f, 20000f, 1f);

        /// <summary>Builds and copies one frame, after a tick change so it rebuilds.</summary>
        public List<DrawPrimitive> Frame(ISceneBuilder builder)
        {
            Host.Tick++;
            return Prims.Copy(builder.Build(Overview, Gfx.High()));
        }
    }
}
