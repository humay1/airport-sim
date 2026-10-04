using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirportSim.App.Render;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    // Shared helpers for the T-029 suite, written from 17-interfaces-ui.md,
    // 15 §15.4/§15.8/§15.9/§15.14, 16 §16.6, 08 §8.5/§8.7, 09 §9.7b/§9.8 and
    // 07-conventions.md only.
    //
    // Floats appear only to build and check the values of the float-typed
    // members of 15 and 17 (ScreenPoint, WorldPoint, CameraView, screen
    // size), following 15 §15.3's rule for tests/app/render (Q-100). Every
    // screen and camera value below is chosen so that 17 §17.3's mapping is
    // exact in binary floating point, so expected world points are exact
    // whatever order the scene layer evaluates the formula in.

    internal static class UiConst
    {
        // 08 §8.1.
        public const ulong TicksPerDay = 14400UL;
        public const ulong HashCheckpointTicks = 600UL;
        public const ulong CommandMinLeadTicks = SimConstants.COMMAND_MIN_LEAD_TICKS;
    }

    /// <summary>08 §8.8's SplitMix64, the 07 L4 input generator for property tests.</summary>
    internal sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Integer in [lo, hi]; test inputs only.</summary>
        public int Range(int lo, int hi)
        {
            return lo + (int)(Next() % (ulong)(hi - lo + 1));
        }
    }

    /// <summary>
    /// The repository root is the nearest ancestor of AppContext.BaseDirectory
    /// holding AirportSim.sln (07 "Fixture location", Q-031). A missing root
    /// fails the test.
    /// </summary>
    internal static class Repo
    {
        public static byte[] Read(params string[] relative)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            var parts = new List<string> { dir! };
            parts.AddRange(relative);
            return File.ReadAllBytes(Path.Combine(parts.ToArray()));
        }
    }

    /// <summary>
    /// GC.GetAllocatedBytesForCurrentThread counts whole allocation contexts,
    /// so a full blocking collection right before the first read leaves the
    /// thread with no partly used context to retire inside the window.
    /// </summary>
    internal static class Allocation
    {
        public static long Start()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetAllocatedBytesForCurrentThread();
        }

        public static long Since(long start)
        {
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
    }

    internal static class Gfx
    {
        public static GraphicsSettings Of(GraphicsPreset preset)
        {
            return RenderFactory.GraphicsForPreset(preset);
        }

        public static GraphicsSettings Custom(bool drawAgents, int maxAgents, int frameRateCap, int resolution, bool antiAliasing)
        {
            return new GraphicsSettings(GraphicsPreset.Custom, drawAgents, maxAgents, frameRateCap, resolution, antiAliasing);
        }

        /// <summary>The three presets and custom extremes, every bound of 15 §15.14 among them.</summary>
        public static GraphicsSettings[] EverySetting()
        {
            return new[]
            {
                Of(GraphicsPreset.Low),
                Of(GraphicsPreset.Medium),
                Of(GraphicsPreset.High),
                Custom(false, 1, 15, 50, false),
                Custom(true, RenderConstants.MAX_DRAWN_AGENTS_PER_NODE, 240, 100, true),
                Custom(true, 1, 0, 50, true),
                Custom(false, RenderConstants.MAX_DRAWN_AGENTS_PER_NODE, 0, 100, false),
            };
        }

        public static string Show(in GraphicsSettings g)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} agents={1} max={2} fps={3} res={4} aa={5}",
                g.Preset,
                g.DrawAgents,
                g.MaxDrawnAgentsPerNode,
                g.FrameRateCap,
                g.ResolutionScalePercent,
                g.AntiAliasing);
        }

        public static void AssertSame(in GraphicsSettings expected, in GraphicsSettings actual, string what)
        {
            Assert.True(Show(expected) == Show(actual), what + ": expected " + Show(expected) + ", got " + Show(actual));
        }
    }

    internal static class In
    {
        public static UiInput Pause()
        {
            return Make(UiInputKind.TogglePause);
        }

        public static UiInput Speed(GameSpeed s)
        {
            return new UiInput(UiInputKind.SetSpeed, s, default, default, default);
        }

        public static UiInput Click(float x, float y)
        {
            return new UiInput(UiInputKind.PrimaryClick, default, new ScreenPoint(x, y), default, default);
        }

        public static UiInput RightClick(float x, float y)
        {
            return new UiInput(UiInputKind.SecondaryClick, default, new ScreenPoint(x, y), default, default);
        }

        public static UiInput Settings()
        {
            return Make(UiInputKind.ToggleSettings);
        }

        public static UiInput Preset(GraphicsPreset p)
        {
            return new UiInput(UiInputKind.SetGraphicsPreset, default, default, p, default);
        }

        public static UiInput Graphics(in GraphicsSettings g)
        {
            return new UiInput(UiInputKind.SetGraphicsSettings, default, default, default, g);
        }

        public static UiInput[] List(params UiInput[] inputs)
        {
            return inputs;
        }

        private static UiInput Make(UiInputKind kind)
        {
            return new UiInput(kind, default, default, default, default);
        }
    }

    /// <summary>
    /// The screen and cameras of this suite. On Screen.Identity, a screen
    /// pixel is one world unit on both axes and the origin coincides, so a
    /// click at screen (x, y) lands at world (x, y). Screen.Halved maps
    /// world (x, y) to screen (2(x − 440) + 512, 4(y − 230) + 256).
    /// </summary>
    internal static class Screen
    {
        public const float W = 1024f;
        public const float H = 512f;

        /// <summary>Centre (512, 256), ViewHeight 512, Aspect 2: world = screen.</summary>
        public static readonly CameraView Identity = new CameraView(new WorldPoint(512f, 256f), 512f, 2f);

        /// <summary>Centre (440, 230), ViewHeight 128, Aspect 4: half a unit per pixel in X, a quarter in Y.</summary>
        public static readonly CameraView Halved = new CameraView(new WorldPoint(440f, 230f), 128f, 4f);

        public static void Update(IUiController ui, params UiInput[] inputs)
        {
            ui.Update(inputs, Identity, W, H);
        }
    }

    internal static class Layouts
    {
        public const string FixtureName = "phase1-layout.json";
        public const string OverlapName = "overlap-layout.json";

        /// <summary>tests/fixtures/render/phase1-layout.json (15 §15.12), validated by the 15 §15.4 loader.</summary>
        public static RenderLayout Fixture()
        {
            byte[] file = Repo.Read("tests", "fixtures", "render", FixtureName);
            return RenderFactory.CreateLayoutLoader().Load(file, FixtureName, null);
        }

        /// <summary>
        /// The synthetic layout with overlapping boxes (17 §17.10), validated
        /// by the 15 §15.4 loader. The array is written out of id order on
        /// purpose; Load returns it ascending.
        ///
        ///   box 7: (50,50)-(150,150)   box 3: (0,0)-(100,100)
        ///   box 5: (40,40)-(60,60)     box 9: (300,300)-(340,320), alone
        /// </summary>
        public static RenderLayout Overlap()
        {
            string json =
                "{\n" +
                "  \"schema_version\": 1,\n" +
                "  \"taxi_nodes\": [],\n" +
                "  \"runways\": [],\n" +
                "  \"flow_nodes\": [\n" +
                "    { \"node\": 7, \"min_x\": 50, \"min_y\": 50, \"max_x\": 150, \"max_y\": 150, \"fill_capacity\": 100 },\n" +
                "    { \"node\": 3, \"min_x\": 0, \"min_y\": 0, \"max_x\": 100, \"max_y\": 100, \"fill_capacity\": 100 },\n" +
                "    { \"node\": 9, \"min_x\": 300, \"min_y\": 300, \"max_x\": 340, \"max_y\": 320, \"fill_capacity\": 100 },\n" +
                "    { \"node\": 5, \"min_x\": 40, \"min_y\": 40, \"max_x\": 60, \"max_y\": 60, \"fill_capacity\": 100 }\n" +
                "  ],\n" +
                "  \"stand_size\": 40, \"aircraft_size\": 30, \"agent_size\": 2,\n" +
                "  \"taxiway_width\": 15\n" +
                "}\n";
            return RenderFactory.CreateLayoutLoader().Load(Encoding.UTF8.GetBytes(json), OverlapName, null);
        }

        /// <summary>A layout of only the given boxes (node, minX, minY, maxX, maxY), validated by the 15 §15.4 loader.</summary>
        public static RenderLayout FromBoxes(params (uint Node, int MinX, int MinY, int MaxX, int MaxY)[] boxes)
        {
            var parts = new List<string>();
            foreach ((uint node, int minX, int minY, int maxX, int maxY) in boxes)
            {
                parts.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{{ \"node\": {0}, \"min_x\": {1}, \"min_y\": {2}, \"max_x\": {3}, \"max_y\": {4}, \"fill_capacity\": 100 }}",
                    node,
                    minX,
                    minY,
                    maxX,
                    maxY));
            }

            string json =
                "{ \"schema_version\": 1, \"taxi_nodes\": [], \"runways\": [], \"flow_nodes\": [ " + string.Join(", ", parts) + " ], " +
                "\"stand_size\": 40, \"aircraft_size\": 30, \"agent_size\": 2, \"taxiway_width\": 15 }\n";
            return RenderFactory.CreateLayoutLoader().Load(Encoding.UTF8.GetBytes(json), "synthetic.json", null);
        }

        public static FlowNodeBox Box(in RenderLayout layout, uint node)
        {
            foreach (FlowNodeBox b in layout.FlowNodes)
            {
                if (b.Node.Value == node)
                {
                    return b;
                }
            }

            throw new InvalidOperationException("no box for node " + node);
        }
    }

    internal static class Pace
    {
        public static void AssertIs(bool paused, GameSpeed speed, in PacingState actual, string what)
        {
            Assert.True(
                actual.Paused == paused && actual.Speed == speed,
                what + ": expected { Paused: " + paused + ", Speed: " + speed + " }, got { Paused: " + actual.Paused + ", Speed: " + actual.Speed + " }");
        }

        /// <summary>IUiController.Pacing and UiFrame.Pacing agree, and both are as expected (17 §17.4).</summary>
        public static void AssertBoth(bool paused, GameSpeed speed, IUiController ui, string what)
        {
            AssertIs(paused, speed, ui.Pacing, what + " (Pacing)");
            AssertIs(paused, speed, ui.Frame().Pacing, what + " (Frame().Pacing)");
        }
    }
}
