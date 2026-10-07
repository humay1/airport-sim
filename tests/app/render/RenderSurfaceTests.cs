using System;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// Interface conformance for Q-130's additions to 15 §15.4 and §15.9,
    /// under 07 L10: enum members in declared order (the draw order and
    /// Paint's region indices depend on them), constructor shapes including
    /// the kept constructors, and the new RenderFactory members.
    /// </summary>
    public sealed class RenderSurfaceTests
    {
        private static void AssertMembers<T>(params string[] names)
            where T : struct, Enum
        {
            Assert.Equal(names, Enum.GetNames(typeof(T)));
            for (int i = 0; i < names.Length; i++)
            {
                Assert.Equal(i, Convert.ToInt32(Enum.Parse(typeof(T), names[i]), System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static string Shape(ConstructorInfo c)
        {
            return string.Join(",", c.GetParameters().Select(p => p.ParameterType.Name));
        }

        private static string[] Constructors(Type t)
        {
            return t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Select(Shape).OrderBy(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).ToArray();
        }

        [Fact]
        public void test_render_q130_surface_matches_spec()
        {
            AssertMembers<DrawLayer>("Ground", "Runway", "Taxiway", "Stand", "LandsideNode", "QueueFill", "Lane", "Agent", "Aircraft");
            AssertMembers<ColourRole>(
                "Runway", "RunwayQueued", "Taxiway", "StandFree", "StandOccupied", "LandsideNode", "QueueFill", "Agent",
                "AircraftMoving", "AircraftHolding", "AircraftOnStand", "LaneOpen", "LaneClosed", "RunwayMarking", "TaxiwayMarking", "Apron", "Building");
            AssertMembers<SourceKind>(
                "Runway", "TaxiEdge", "Stand", "FlowNode", "QueueFill", "Agent", "Aircraft", "Lane",
                "RunwayMarking", "TaxiNode", "TaxiCentreline", "Apron", "Building", "StandMarking", "StandNumber", "JetBridge");
            AssertMembers<VisualId>(
                "RunwaySurface", "RunwayEdgeLines", "RunwayThreshold", "RunwayCentreDash",
                "TaxiwaySurface", "TaxiwayJunction", "TaxiwayCentreline",
                "Apron", "TerminalBuilding", "Pier", "ControlTower", "JetBridge",
                "StandPad", "StandLeadIn",
                "MarkingDigit0", "MarkingDigit1", "MarkingDigit2", "MarkingDigit3", "MarkingDigit4",
                "MarkingDigit5", "MarkingDigit6", "MarkingDigit7", "MarkingDigit8", "MarkingDigit9",
                "TerminalZone", "QueueFill", "LanePip", "Passenger",
                "AircraftA", "AircraftB", "AircraftC", "AircraftD", "AircraftE", "AircraftF");
            AssertMembers<AircraftRegion>("Fuselage", "Tail", "Cheatline", "Engines", "Logo");
            AssertMembers<PassengerRegion>("Top", "Bottom", "Skin", "Hair", "Bag");
            AssertMembers<LogoMark>("None", "Disc", "Ring", "Chevron", "Star", "Bars", "Diamond", "Crescent");
            AssertMembers<AreaKind>("Apron", "Terminal", "Pier", "ControlTower");

            // One constructor per struct, in declared order, except 07 L10's kept constructors.
            Assert.Equal(new[] { "PrimitiveKind,DrawLayer,ColourRole,VisualId,WorldPoint,WorldPoint,Single,WorldPoint,Paint,SourceRef" }, Constructors(typeof(DrawPrimitive)));
            Assert.Equal(new[] { "Byte,Byte,Byte" }, Constructors(typeof(Rgb)));
            Assert.Equal(new[] { "Rgb,Rgb,Rgb,Rgb,Rgb,Byte" }, Constructors(typeof(Paint)));
            Assert.Equal(new[] { "Rgb,Rgb,Rgb,Rgb,Rgb,LogoMark" }, Constructors(typeof(Livery)));
            Assert.Equal(new[] { "AirlineId,Livery" }, Constructors(typeof(AirlineLivery)));
            Assert.Equal(new[] { "Livery,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1" }, Constructors(typeof(RenderLooks)));
            Assert.Equal(new[] { "UInt32,AreaKind,Int32,Int32,Int32,Int32" }, Constructors(typeof(LayoutArea)));
            Assert.Equal(new[] { "UInt32,Int32,Int32,Int32,Int32,Int32" }, Constructors(typeof(LayoutBridge)));
            Assert.Equal(
                new[]
                {
                    "ISimHost,IAirsideSystem,IFlowSystem",
                    "ISimHost,IAirsideSystem,IFlowSystem,IScheduleSystem,IContentIndex",
                },
                Constructors(typeof(RenderSources)));
            Assert.Equal(
                new[]
                {
                    "IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,Int32,Int32,Int32,Int32",
                    "IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,Int32,Int32,Int32,Int32,IReadOnlyList`1,IReadOnlyList`1",
                },
                Constructors(typeof(RenderLayout)));

            // The kept RenderSources constructor leaves Schedule and Content null.
            var guard = new CallGuard();
            var kept = new RenderSources(new FakeHost(guard), null, null);
            Assert.Null(kept.Schedule);
            Assert.Null(kept.Content);
            var schedule = new FakeSchedule(guard);
            var content = new FakeContent(guard);
            var full = new RenderSources(new FakeHost(guard), null, null, schedule, content);
            Assert.Same(schedule, full.Schedule);
            Assert.Same(content, full.Content);

            // Paint's default is all zero; a Paint keeps what it is given.
            Paint zero = default;
            Assert.Equal("#000000,#000000,#000000,#000000,#000000/0", Prims.Show(zero));
            var p = new Paint(new Rgb(1, 2, 3), new Rgb(4, 5, 6), new Rgb(7, 8, 9), new Rgb(10, 11, 12), new Rgb(13, 14, 15), 6);
            Assert.Equal("#010203,#040506,#070809,#0A0B0C,#0D0E0F/6", Prims.Show(p));

            // DrawPrimitive keeps every field it is given.
            var prim = new DrawPrimitive(
                PrimitiveKind.Dot,
                DrawLayer.Stand,
                ColourRole.TaxiwayMarking,
                VisualId.MarkingDigit7,
                new WorldPoint(1f, 2f),
                new WorldPoint(3f, 4f),
                5f,
                new WorldPoint(-6f, 7f),
                p,
                new SourceRef(SourceKind.StandNumber, 42UL, 1));
            Assert.Equal("Dot/Stand/TaxiwayMarking/MarkingDigit7 A=(1,2) B=(3,4) size=5 facing=(-6,7) paint=#010203,#040506,#070809,#0A0B0C,#0D0E0F/6 src=StandNumber:42:1", Prims.Show(prim));

            // The new RenderFactory members (15 §15.9).
            Type f = typeof(RenderFactory);
            Assert.NotNull(f.GetMethod("CreateSceneBuilder", new[] { typeof(RenderSources).MakeByRefType(), typeof(RenderLayout).MakeByRefType() }));
            Assert.NotNull(f.GetMethod("CreateSceneBuilder", new[] { typeof(RenderSources).MakeByRefType(), typeof(RenderLayout).MakeByRefType(), typeof(RenderLooks).MakeByRefType() }));
            MethodInfo? load = f.GetMethod("LoadLooks", new[] { typeof(IContentSource) });
            Assert.True(load != null && load.IsStatic && load.ReturnType == typeof(RenderLooks), "RenderFactory.LoadLooks(IContentSource) -> RenderLooks");
            MethodInfo? defaults = f.GetMethod("DefaultLooks", Type.EmptyTypes);
            Assert.True(defaults != null && defaults.IsStatic && defaults.ReturnType == typeof(RenderLooks), "RenderFactory.DefaultLooks() -> RenderLooks");
            Assert.Equal(typeof(IScheduleSystem), typeof(RenderSources).GetProperty("Schedule")!.PropertyType);
        }
    }
}
