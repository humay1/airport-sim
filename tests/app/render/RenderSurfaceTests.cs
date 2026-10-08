using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Airside;
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
                "RunwayMarking", "TaxiNode", "TaxiCentreline", "Apron", "Building", "StandMarking", "StandNumber", "JetBridge",
                "BridgePassenger"); // appended, Q-132
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

            // One constructor per struct, in declared order, except 07 L10's kept
            // constructors, Q-132's included (15 §15.9, §15.21).
            Assert.Equal(
                new[]
                {
                    "PrimitiveKind,DrawLayer,ColourRole,VisualId,WorldPoint,WorldPoint,Single,WorldPoint,Paint,SourceRef",
                    "PrimitiveKind,DrawLayer,ColourRole,VisualId,WorldPoint,WorldPoint,Single,WorldPoint,Paint,SourceRef,Single",
                },
                Constructors(typeof(DrawPrimitive)));
            Assert.Equal(new[] { "Byte,Byte,Byte" }, Constructors(typeof(Rgb)));
            Assert.Equal(new[] { "Rgb,Rgb,Rgb,Rgb,Rgb,Byte" }, Constructors(typeof(Paint)));
            Assert.Equal(new[] { "Rgb,Rgb,Rgb,Rgb,Rgb,LogoMark" }, Constructors(typeof(Livery)));
            Assert.Equal(new[] { "AirlineId,Livery" }, Constructors(typeof(AirlineLivery)));
            Assert.Equal(new[] { "Livery,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1" }, Constructors(typeof(RenderLooks)));
            Assert.Equal(new[] { "UInt32,AreaKind,Int32,Int32,Int32,Int32" }, Constructors(typeof(LayoutArea)));
            Assert.Equal(new[] { "UInt32,Int32,Int32,Int32,Int32,Int32", "UInt32,Int32,Int32,Int32,Int32,Int32,Nullable`1" }, Constructors(typeof(LayoutBridge)));
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
                    "IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1,Int32,Int32,Int32,Int32,IReadOnlyList`1,IReadOnlyList`1,IReadOnlyList`1",
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
            Assert.Equal("Dot/Stand/TaxiwayMarking/MarkingDigit7 A=(1,2) B=(3,4) size=5 facing=(-6,7) paint=#010203,#040506,#070809,#0A0B0C,#0D0E0F/6 src=StandNumber:42:1 elev=0", Prims.Show(prim));

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

        [Fact]
        public void test_render_q132_surface_matches_spec()
        {
            // 15 §15.19's constants: in RenderConstants, all int (Q-132).
            (string Name, int Value)[] constants =
            {
                ("APPROACH_TICKS", 15),
                ("APPROACH_ENTRY_M", 8000),
                ("FINAL_FIX_M", 2000),
                ("HOLD_LEG_M", 1000),
                ("HOLD_LEG_TICKS", 3),
                ("CLIMB_OUT_M", 4000),
                ("GLIDE_RATIO", 20),
                ("CLIMB_RATIO", 10),
                ("BRIDGE_WALK_TICKS", 3),
                ("MAX_BRIDGE_WALKERS", 8),
            };
            foreach ((string name, int value) in constants)
            {
                FieldInfo? f = typeof(RenderConstants).GetField(name, BindingFlags.Public | BindingFlags.Static);
                Assert.True(f != null, "RenderConstants." + name + " missing");
                Assert.True(f!.IsLiteral, name + " is not a const");
                Assert.Equal(typeof(int), f.FieldType);
                Assert.Equal(value, f.GetRawConstantValue());
            }

            // 15 §15.9: DrawPrimitive.Elevation is a float; the kept ten-field
            // constructor sets it to 0, and the eleven-field one keeps it.
            Assert.Equal(typeof(float), typeof(DrawPrimitive).GetProperty("Elevation")!.PropertyType);
            var paint = new Paint(new Rgb(1, 2, 3), new Rgb(4, 5, 6), new Rgb(7, 8, 9), new Rgb(10, 11, 12), new Rgb(13, 14, 15), 0);
            var source = new SourceRef(SourceKind.BridgePassenger, 3UL, 7);
            var kept = new DrawPrimitive(PrimitiveKind.Dot, DrawLayer.Agent, ColourRole.Agent, VisualId.Passenger, new WorldPoint(1f, 2f), default, 2f, new WorldPoint(0f, 40f), paint, source);
            Assert.Equal(0f, kept.Elevation);
            var lifted = new DrawPrimitive(PrimitiveKind.Dot, DrawLayer.Aircraft, ColourRole.AircraftMoving, VisualId.AircraftC, new WorldPoint(1f, 2f), default, 30f, new WorldPoint(-2000f, 0f), paint, new SourceRef(SourceKind.Aircraft, 9UL, 0), 412.5f);
            Assert.Equal(412.5f, lifted.Elevation);
            Assert.Equal("Dot/Agent/Agent/Passenger A=(1,2) B=(0,0) size=2 facing=(0,40) paint=#010203,#040506,#070809,#0A0B0C,#0D0E0F/0 src=BridgePassenger:3:7 elev=0", Prims.Show(kept));
            Assert.EndsWith("src=Aircraft:9:0 elev=412.5", Prims.Show(lifted), StringComparison.Ordinal);

            // 15 §15.4, §15.21: the layout's Q-132 members and LayoutWalkway's one constructor.
            Assert.Equal(typeof(StandId?), typeof(LayoutBridge).GetProperty("Stand")!.PropertyType);
            Assert.Equal(typeof(IReadOnlyList<LayoutWalkway>), typeof(RenderLayout).GetProperty("Walkways")!.PropertyType);
            Assert.Equal(new[] { "NodeId,Int32,Int32,Int32,Int32,Int32" }, Constructors(typeof(LayoutWalkway)));

            // 15 §15.8: the pacer's get-only sub-tick, an int64.
            PropertyInfo? sub = typeof(ITickPacer).GetProperty("SubTickMicroseconds");
            Assert.True(sub != null && sub.PropertyType == typeof(long) && sub.CanRead && !sub.CanWrite, "ITickPacer.SubTickMicroseconds { get } -> long");

            // 15 §15.9: ISceneBuilder's two Build overloads.
            Type cam = typeof(CameraView).MakeByRefType();
            Type gfx = typeof(GraphicsSettings).MakeByRefType();
            MethodInfo? two = typeof(ISceneBuilder).GetMethod("Build", new[] { cam, gfx });
            MethodInfo? three = typeof(ISceneBuilder).GetMethod("Build", new[] { cam, gfx, typeof(long) });
            Assert.True(two != null && two.ReturnType == typeof(RenderFrame), "Build(in CameraView, in GraphicsSettings) -> RenderFrame");
            Assert.True(three != null && three.ReturnType == typeof(RenderFrame), "Build(in CameraView, in GraphicsSettings, long) -> RenderFrame");
            Assert.Equal("subTickMicroseconds", three!.GetParameters()[2].Name);
        }
    }
}
