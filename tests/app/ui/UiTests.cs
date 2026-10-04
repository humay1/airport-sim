using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.App.Render;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// 17-interfaces-ui.md: the public surface (§17.3, §17.4, §17.7), pacing
    /// (§17.4), screen to world (§17.3), the lane click (§17.5), the
    /// production sink's pending target (§17.5 step 4), the settings panel
    /// and its invariant (§17.4a), the §17.9 allocation budget, and the
    /// §17.10 integration test with scripted graphics inputs.
    /// </summary>
    public sealed class UiTests
    {
        private static readonly GameSpeed[] Speeds = { GameSpeed.X1, GameSpeed.X2, GameSpeed.X4 };

        private static IUiController Make(out RecordingSink sink)
        {
            sink = new RecordingSink();
            return UiFactory.CreateController(Layouts.Fixture(), sink, Gfx.Of(GraphicsPreset.High));
        }

        private static IUiController Make(in RenderLayout layout, out RecordingSink sink)
        {
            sink = new RecordingSink();
            return UiFactory.CreateController(layout, sink, Gfx.Of(GraphicsPreset.High));
        }

        private static void AssertRequests(string expected, RecordingSink sink, string what)
        {
            Assert.True(sink.Show() == expected, what + ": expected requests " + expected + ", got " + sink.Show());
        }

        /// <summary>Everything a controller exposes, graphics included, and what its sink received.</summary>
        private static string Snapshot(IUiController ui, RecordingSink sink)
        {
            UiFrame f = ui.Frame();
            return "pacing " + ui.Pacing.Paused + "/" + ui.Pacing.Speed + " frame " + f.Pacing.Paused + "/" + f.Pacing.Speed + " open " + f.SettingsOpen
                + " graphics " + Gfx.Show(ui.Graphics) + " frame graphics " + Gfx.Show(f.Graphics) + " requests " + sink.Show();
        }

        // ------------------------------------------------------------ misuse (§17.7, Q-104)

        [Fact]
        public void test_ui_misused_calls_throw_named_exception_and_change_nothing()
        {
            RenderLayout layout = Layouts.Fixture();

            // CreateController: layout.FlowNodes null, then sink null, then initialGraphics.Preset out of the enum.
            var unused = new RecordingSink();
            GraphicsSettings badPreset = new GraphicsSettings((GraphicsPreset)4, true, 64, 60, 100, false);
            Assert.Equal("layout", Assert.Throws<ArgumentException>(() => UiFactory.CreateController(default(RenderLayout), unused, Gfx.Of(GraphicsPreset.High))).ParamName);
            Assert.Equal("layout", Assert.Throws<ArgumentException>(() => UiFactory.CreateController(default(RenderLayout), null!, badPreset)).ParamName);
            Assert.Equal("sink", Assert.Throws<ArgumentNullException>(() => UiFactory.CreateController(layout, null!, Gfx.Of(GraphicsPreset.High))).ParamName);
            Assert.Equal("sink", Assert.Throws<ArgumentNullException>(() => UiFactory.CreateController(layout, null!, badPreset)).ParamName);
            Assert.Equal("initialGraphics", Assert.Throws<ArgumentOutOfRangeException>(() => UiFactory.CreateController(layout, unused, badPreset)).ParamName);
            GraphicsSettings negativePreset = new GraphicsSettings((GraphicsPreset)(-1), true, 64, 60, 100, false);
            Assert.Equal("initialGraphics", Assert.Throws<ArgumentOutOfRangeException>(() => UiFactory.CreateController(layout, unused, negativePreset)).ParamName);
            Assert.Empty(unused.Requests);

            // CreateLaneCommandSink: host null, then flow null; nothing is called.
            var guard = new CallGuard();
            var host = new FakeHost(guard, 40UL);
            var flow = new FakeFlow(guard).Lanes(5, 6, 2);
            Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => UiFactory.CreateLaneCommandSink(null!, flow)).ParamName);
            Assert.Equal("host", Assert.Throws<ArgumentNullException>(() => UiFactory.CreateLaneCommandSink(null!, null!)).ParamName);
            Assert.Equal("flow", Assert.Throws<ArgumentNullException>(() => UiFactory.CreateLaneCommandSink(host, null!)).ParamName);

            // The production sink's Request: a delta other than ±1 throws before TryGetLaneState, lane or not.
            ILaneCommandSink laneSink = UiFactory.CreateLaneCommandSink(host, flow);
            foreach (int delta in new[] { 0, 2, -2, int.MaxValue, int.MinValue })
            {
                foreach (uint node in new uint[] { 5, 4 })
                {
                    long laneCalls = flow.LaneCalls;
                    long tickReads = host.CurrentTickReads;
                    ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => laneSink.Request(new NodeId(node), delta));
                    Assert.Equal("delta", e.ParamName);
                    Assert.True(flow.LaneCalls == laneCalls && host.CurrentTickReads == tickReads && host.Submits.Count == 0, "Request(" + node + ", " + delta + ") touched the sim before throwing");
                }
            }

            laneSink.Request(new NodeId(5), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => laneSink.Request(new NodeId(5), 2));
            laneSink.Request(new NodeId(5), 1);
            Assert.True(host.Submits.Count == 2 && host.Submits[0].Count == 3 && host.Submits[1].Count == 4, "a throwing Request changed the pending target: " + host.Show());
            Assert.Empty(guard.Violations);

            // IUiController.Update, from a state that is not the initial one.
            var sink = new RecordingSink();
            IUiController ui = UiFactory.CreateController(layout, sink, Gfx.Of(GraphicsPreset.Medium));
            Screen.Update(ui, In.Speed(GameSpeed.X2), In.Pause(), In.Preset(GraphicsPreset.Low), In.Click(440f, 230f));

            void Misuse<T>(IReadOnlyList<UiInput>? inputs, float w, float h, string param, string what)
                where T : ArgumentException
            {
                string before = Snapshot(ui, sink);
                T e = Assert.Throws<T>(() => ui.Update(inputs!, Screen.Identity, w, h));
                Assert.True(e.ParamName == param, what + ": ParamName " + e.ParamName + ", expected " + param);
                string after = Snapshot(ui, sink);
                Assert.True(after == before, what + ": a throwing Update changed state.\n before " + before + "\n after  " + after);
            }

            // Every valid kind, a hit among them, so a partly applied list shows.
            UiInput[] prefix = { In.Pause(), In.Speed(GameSpeed.X4), In.Click(540f, 230f), In.Preset(GraphicsPreset.High), In.Graphics(Gfx.Custom(true, 7, 0, 63, false)), In.Settings() };
            UiInput badKind = new UiInput((UiInputKind)7, GameSpeed.X1, default, GraphicsPreset.Low, default);
            UiInput negativeKind = new UiInput((UiInputKind)(-1), GameSpeed.X1, default, GraphicsPreset.Low, default);
            UiInput badSpeed = In.Speed((GameSpeed)3);
            UiInput zeroSpeed = In.Speed((GameSpeed)0);
            UiInput bigSpeed = In.Speed((GameSpeed)8);
            UiInput badPresetInput = In.Preset((GraphicsPreset)4);

            // 1. inputs null, first.
            Misuse<ArgumentNullException>(null, Screen.W, Screen.H, "inputs", "null inputs");
            Misuse<ArgumentNullException>(null, 0f, float.NaN, "inputs", "null inputs and a bad screen");

            // 2. screenWidth, then screenHeight: finite and > 0, checked with no inputs too.
            foreach (float bad in new[] { 0f, -0f, -1024f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Misuse<ArgumentOutOfRangeException>(Array.Empty<UiInput>(), bad, Screen.H, "screenWidth", "screenWidth " + bad + ", no inputs");
                Misuse<ArgumentOutOfRangeException>(prefix, bad, Screen.H, "screenWidth", "screenWidth " + bad);
                Misuse<ArgumentOutOfRangeException>(prefix, Screen.W, bad, "screenHeight", "screenHeight " + bad);
                Misuse<ArgumentOutOfRangeException>(prefix.Append(badKind).ToArray(), bad, bad, "screenWidth", "both sizes " + bad + " and a bad input");
            }

            // 3. Every input checked before any is applied, wherever the bad one sits.
            foreach (UiInput bad in new[] { badKind, negativeKind, badSpeed, zeroSpeed, bigSpeed, badPresetInput })
            {
                string what = "a " + bad.Kind + " input (speed " + (int)bad.Speed + ", preset " + (int)bad.Preset + ")";
                Misuse<ArgumentOutOfRangeException>(prefix.Append(bad).ToArray(), Screen.W, Screen.H, "inputs", what + " after valid ones");
                Misuse<ArgumentOutOfRangeException>(new[] { bad }.Concat(prefix).ToArray(), Screen.W, Screen.H, "inputs", what + " before valid ones");
            }

            // Also while the settings panel is open, where the modal rule would ignore the input.
            Screen.Update(ui, In.Settings());
            Assert.True(ui.Frame().SettingsOpen, "the panel did not open");
            Misuse<ArgumentOutOfRangeException>(new[] { In.Preset(GraphicsPreset.High), badSpeed }, Screen.W, Screen.H, "inputs", "a bad SetSpeed with the panel open");
            Misuse<ArgumentOutOfRangeException>(new[] { In.Settings(), badKind }, Screen.W, Screen.H, "inputs", "a bad Kind with the panel open");
            Screen.Update(ui, In.Settings());

            // Only the field the Kind uses is checked, Custom is ignored, and a click is never an error.
            string start = Snapshot(ui, sink);
            Screen.Update(
                ui,
                new UiInput(UiInputKind.TogglePause, (GameSpeed)3, default, (GraphicsPreset)9, default),
                new UiInput(UiInputKind.TogglePause, (GameSpeed)0, default, (GraphicsPreset)(-1), default),
                new UiInput(UiInputKind.SetSpeed, GameSpeed.X4, default, (GraphicsPreset)9, default),
                new UiInput(UiInputKind.SetGraphicsPreset, (GameSpeed)3, default, GraphicsPreset.Custom, default),
                new UiInput(UiInputKind.PrimaryClick, (GameSpeed)3, new ScreenPoint(float.NaN, 230f), (GraphicsPreset)9, default),
                In.Click(float.PositiveInfinity, 230f),
                In.RightClick(float.NegativeInfinity, float.NaN),
                new UiInput(UiInputKind.SecondaryClick, (GameSpeed)0, new ScreenPoint(540f, 230f), (GraphicsPreset)4, default));
            Pace.AssertBoth(true, GameSpeed.X4, ui, "after the non-throwing inputs");
            Assert.True(sink.Show() == "[(5,+1) (6,-1)]", "non-finite clicks must hit nothing, the last click must hit box 6: " + sink.Show() + " (started from " + start + ")");
            Gfx.AssertSame(Gfx.Of(GraphicsPreset.Low), ui.Graphics, "SetGraphicsPreset(Custom) is ignored");
        }

        // ------------------------------------------------------------ surface

        [Fact]
        public void test_ui_public_surface_matches_spec()
        {
            // 07 L5/L6: a type is public iff the spec names it, directly in the RootNamespace.
            Assembly ui = typeof(UiFactory).Assembly;
            string[] expected =
            {
                "ILaneCommandSink", "IUiController", "PacingState", "ScreenPoint", "UiFactory", "UiFrame", "UiInput", "UiInputKind",
            };
            var exported = ui.GetExportedTypes().Select(t => t.FullName ?? t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(expected.Select(n => "AirportSim.App.Ui." + n).OrderBy(n => n, StringComparer.Ordinal).ToList(), exported);

            // 17 §17.3: UiInputKind in declared order, the D10 kinds appended.
            UiInputKind[] kinds =
            {
                UiInputKind.TogglePause, UiInputKind.SetSpeed, UiInputKind.PrimaryClick, UiInputKind.SecondaryClick,
                UiInputKind.ToggleSettings, UiInputKind.SetGraphicsPreset, UiInputKind.SetGraphicsSettings,
            };
            Assert.Equal(kinds.Length, Enum.GetValues(typeof(UiInputKind)).Length);
            for (int i = 0; i < kinds.Length; i++)
            {
                Assert.Equal(i, (int)kinds[i]);
            }

            // 07 L10: UiFactory is a static class whose public members are exactly 17 §17.4a/§17.7's four.
            Type factory = typeof(UiFactory);
            Assert.True(factory.IsAbstract && factory.IsSealed, "UiFactory is not a static class");
            var methods = factory.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new List<string> { "CreateController", "CreateLaneCommandSink", "EncodeGraphicsPreference", "TryDecodeGraphicsPreference" }, methods);

            // 17 §17.7: the two interfaces carry exactly their spec members.
            Assert.Equal(
                new List<string> { "Frame", "Update", "get_Graphics", "get_Pacing" },
                typeof(IUiController).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());
            Assert.Equal(new List<string> { "Request" }, typeof(ILaneCommandSink).GetMethods().Select(m => m.Name).ToList());
        }

        // ------------------------------------------------------------ pacing (§17.4)

        [Fact]
        public void test_ui_starts_unpaused_at_1x()
        {
            IUiController ui = Make(out RecordingSink sink);
            Pace.AssertBoth(false, GameSpeed.X1, ui, "a new controller");
            Assert.False(ui.Frame().SettingsOpen, "a new controller has the settings panel open");

            Screen.Update(ui);
            Pace.AssertBoth(false, GameSpeed.X1, ui, "after an Update with no inputs");
            Assert.Empty(sink.Requests);
        }

        [Fact]
        public void test_ui_toggle_pause_and_set_speed_apply_in_input_order()
        {
            IUiController ui = Make(out _);
            Screen.Update(ui, In.Speed(GameSpeed.X4), In.Pause(), In.Speed(GameSpeed.X2));
            Pace.AssertBoth(true, GameSpeed.X2, ui, "[X4, pause, X2]");

            Screen.Update(ui, In.Pause(), In.Pause(), In.Pause());
            Pace.AssertBoth(false, GameSpeed.X2, ui, "three toggles flip once");

            Screen.Update(ui, In.Speed(GameSpeed.X1), In.Speed(GameSpeed.X4));
            Pace.AssertBoth(false, GameSpeed.X4, ui, "[X1, X4]: the last speed wins");

            Screen.Update(ui, In.Speed(GameSpeed.X4), In.Speed(GameSpeed.X1));
            Pace.AssertBoth(false, GameSpeed.X1, ui, "[X4, X1]: the last speed wins");

            Screen.Update(ui, In.Pause(), In.Pause());
            Pace.AssertBoth(false, GameSpeed.X1, ui, "two toggles in one frame cancel");

            Screen.Update(ui, In.Pause(), In.Speed(GameSpeed.X2));
            Pace.AssertBoth(true, GameSpeed.X2, ui, "[pause, X2]");
        }

        [Fact]
        public void test_ui_set_speed_does_not_unpause()
        {
            IUiController ui = Make(out _);
            Screen.Update(ui, In.Pause());
            Pace.AssertBoth(true, GameSpeed.X1, ui, "paused");

            foreach (GameSpeed s in new[] { GameSpeed.X2, GameSpeed.X4, GameSpeed.X1, GameSpeed.X1 })
            {
                Screen.Update(ui, In.Speed(s));
                Pace.AssertBoth(true, s, ui, "SetSpeed(" + s + ") while paused");
            }

            Screen.Update(ui, In.Speed(GameSpeed.X4), In.Pause());
            Pace.AssertBoth(false, GameSpeed.X4, ui, "TogglePause unpauses at the speed set while paused");
        }

        // ------------------------------------------------------------ screen to world (§17.3)

        [Fact]
        public void test_ui_screen_to_world_maps_corners_and_centre()
        {
            // Centre (1000, 2000), ViewHeight 256, Aspect 4 on a 1024 x 512 screen:
            // one world unit per pixel in X, half a unit in Y. Aspect differs
            // from w/h, so using w/h instead of Aspect is caught; origin
            // bottom-left with +Y up, so a flipped Y is caught.
            var camera = new CameraView(new WorldPoint(1000f, 2000f), 256f, 4f);
            (float Sx, float Sy, int Wx, int Wy)[] probes =
            {
                (0f, 0f, 488, 1872),
                (1024f, 0f, 1512, 1872),
                (0f, 512f, 488, 2128),
                (1024f, 512f, 1512, 2128),
                (512f, 256f, 1000, 2000),
                (256f, 384f, 744, 2064),
            };

            foreach ((float sx, float sy, int wx, int wy) in probes)
            {
                // P is a corner of each of these four boxes, closed intervals:
                // a hit on all four puts the click exactly on P.
                (int, int, int, int)[] quadrants =
                {
                    (wx, wy, wx + 4, wy + 4),
                    (wx - 4, wy, wx, wy + 4),
                    (wx, wy - 4, wx + 4, wy),
                    (wx - 4, wy - 4, wx, wy),
                };

                foreach ((int minX, int minY, int maxX, int maxY) in quadrants)
                {
                    string what = "screen (" + sx + "," + sy + ") should map to world (" + wx + "," + wy + "); box (" + minX + "," + minY + ")-(" + maxX + "," + maxY + ")";
                    IUiController ui = Make(Layouts.FromBoxes((42U, minX, minY, maxX, maxY)), out RecordingSink sink);
                    ui.Update(In.List(In.Click(sx, sy)), camera, Screen.W, Screen.H);
                    AssertRequests("[(42,+1)]", sink, what);

                    // A quarter pixel off on the side away from the box misses it.
                    float dx = minX == wx ? -0.25f : 0.25f;
                    float dy = minY == wy ? -0.25f : 0.25f;
                    ui.Update(In.List(In.Click(sx + dx, sy), In.Click(sx, sy + dy)), camera, Screen.W, Screen.H);
                    AssertRequests("[(42,+1)]", sink, what + ", then a quarter pixel outside");
                }
            }
        }

        // ------------------------------------------------------------ the lane click (§17.5)

        [Fact]
        public void test_ui_overlapping_boxes_hit_highest_node_id()
        {
            // box 3 (0,0)-(100,100); box 5 (40,40)-(60,60); box 7 (50,50)-(150,150); box 9 (300,300)-(340,320).
            RenderLayout layout = Layouts.Overlap();
            Assert.Equal(new uint[] { 3, 5, 7, 9 }, layout.FlowNodes.Select(b => b.Node.Value).ToArray());

            (float X, float Y, uint Node)[] clicks =
            {
                (55f, 55f, 7),   // in 3, 5 and 7
                (45f, 45f, 5),   // in 3 and 5
                (20f, 20f, 3),   // in 3 only
                (120f, 120f, 7), // in 7 only
                (50f, 50f, 7),   // 7's corner, inside 3 and 5
                (100f, 100f, 7), // 3's corner, inside 7
                (40f, 40f, 5),   // 5's corner, inside 3
                (60f, 60f, 7),   // 5's corner, inside 3 and 7
                (150f, 150f, 7), // 7's far corner
                (0f, 0f, 3),     // 3's corner
                (320f, 310f, 9), // the lone box
            };

            IUiController ui = Make(layout, out RecordingSink sink);
            var expected = new List<string>();
            foreach ((float x, float y, uint node) in clicks)
            {
                Screen.Update(ui, In.Click(x, y));
                expected.Add("(" + node + ",+1)");
                AssertRequests("[" + string.Join(" ", expected) + "]", sink, "click at world (" + x + "," + y + ")");
            }
        }

        [Fact]
        public void test_ui_primary_and_secondary_click_request_plus_and_minus_one()
        {
            // Fixture boxes: node k at ((k-1)·100, 200)-((k-1)·100 + 80, 260).
            IUiController ui = Make(out RecordingSink sink);
            Screen.Update(ui, In.Click(140f, 230f), In.RightClick(440f, 230f), In.Click(440f, 230f), In.RightClick(840f, 230f), In.RightClick(840f, 230f));
            AssertRequests("[(2,+1) (5,-1) (5,+1) (9,-1) (9,-1)]", sink, "one request per click, in input order");

            // The frame's own camera maps the click: on Screen.Halved, screen
            // (712, 256) is world (540, 230), inside box 6.
            ui.Update(In.List(In.Click(712f, 256f), In.RightClick(712f, 256f)), Screen.Halved, Screen.W, Screen.H);
            AssertRequests("[(2,+1) (5,-1) (5,+1) (9,-1) (9,-1) (6,+1) (6,-1)]", sink, "clicks through Screen.Halved");
            Pace.AssertBoth(false, GameSpeed.X1, ui, "clicks leave pacing alone");
        }

        // ------------------------------------------------------------ the production sink (§17.5 step 4)

        [Fact]
        public void test_ui_two_clicks_before_tick_runs_build_on_pending_target()
        {
            var guard = new CallGuard();
            var host = new FakeHost(guard, 100UL);
            var flow = new FakeFlow(guard).Lanes(5, 5, 2).Lanes(6, 4, 4);
            ILaneCommandSink sink = UiFactory.CreateLaneCommandSink(host, flow);

            // Two clicks at tick 100: the second builds on the first's pending 3.
            sink.Request(new NodeId(5), 1);
            sink.Request(new NodeId(5), 1);

            // At tick 101 == cmd.Tick the target 4 is still pending (CurrentTick <= cmd.Tick).
            host.Tick = 101UL;
            sink.Request(new NodeId(5), -1);

            // Pending targets are per node: node 6 starts from its own ServersOpen.
            sink.Request(new NodeId(6), -1);

            // At tick 102 the sim shows 3 open, and the command dated 102 is still pending.
            host.Tick = 102UL;
            flow.Lanes(5, 5, 3);
            sink.Request(new NodeId(5), 1);

            // At tick 104 every command (latest dated 103) has certainly run: base is ServersOpen again.
            host.Tick = 104UL;
            flow.Lanes(5, 5, 1);
            sink.Request(new NodeId(5), 1);

            (ulong Tick, uint Node, int Count)[] expected =
            {
                (101UL, 5U, 3), (101UL, 5U, 4), (102UL, 5U, 3), (102UL, 6U, 3), (103UL, 5U, 4), (105UL, 5U, 2),
            };
            Assert.True(host.Submits.Count == expected.Length, "expected " + expected.Length + " submits, got " + host.Show());
            for (int i = 0; i < expected.Length; i++)
            {
                Submitted s = host.Submits[i];
                Assert.True(
                    s.Tick == expected[i].Tick && s.Node == expected[i].Node && s.Count == expected[i].Count,
                    "submit " + i + ": expected tick " + expected[i].Tick + " node " + expected[i].Node + " count " + expected[i].Count + ", got " + host.Show());
            }

            Assert.Empty(guard.Violations);
        }

        [Fact]
        public void test_ui_rejected_command_is_dropped_not_redated()
        {
            foreach (CommandRejection rejection in new[] { CommandRejection.TooLate, CommandRejection.NotPermitted, CommandRejection.MalformedPayload })
            {
                var guard = new CallGuard();
                var host = new FakeHost(guard, 50UL) { Answer = rejection };
                var flow = new FakeFlow(guard).Lanes(5, 6, 2);
                ILaneCommandSink sink = UiFactory.CreateLaneCommandSink(host, flow);

                // Rejected: submitted once, never retried, never re-dated.
                sink.Request(new NodeId(5), 1);
                Assert.True(host.Submits.Count == 1, rejection + ": one request must make exactly one submit, got " + host.Show());
                Assert.True(host.Submits[0].Tick == 51UL && host.Submits[0].Count == 3, rejection + ": " + host.Show());

                // A rejected command sets no pending target: the next request starts from ServersOpen.
                host.Answer = CommandRejection.None;
                host.Tick = 52UL;
                sink.Request(new NodeId(5), 1);
                Assert.True(host.Submits.Count == 2, rejection + ": " + host.Show());
                Assert.True(host.Submits[1].Tick == 53UL && host.Submits[1].Count == 3, rejection + ": the request after a rejection did not start from ServersOpen: " + host.Show());

                // An admitted one does.
                sink.Request(new NodeId(5), 1);
                Assert.True(host.Submits.Count == 3 && host.Submits[2].Tick == 53UL && host.Submits[2].Count == 4, rejection + ": " + host.Show());
                Assert.Empty(guard.Violations);
            }
        }

        // ------------------------------------------------------------ settings (§17.4a)

        [Fact]
        public void test_ui_settings_open_pauses_and_close_restores_player_pause()
        {
            IUiController ui = Make(out _);
            Screen.Update(ui, In.Speed(GameSpeed.X4));

            // The player had not paused.
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(true, GameSpeed.X4, ui, "settings opened while unpaused");
            Assert.True(ui.Frame().SettingsOpen, "UiFrame.SettingsOpen after ToggleSettings");
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(false, GameSpeed.X4, ui, "settings closed: the player's own state was unpaused");
            Assert.False(ui.Frame().SettingsOpen, "UiFrame.SettingsOpen after the second ToggleSettings");

            // The player had paused.
            Screen.Update(ui, In.Pause());
            Pace.AssertBoth(true, GameSpeed.X4, ui, "player paused");
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(true, GameSpeed.X4, ui, "settings opened while paused");
            Screen.Update(ui, In.Settings());
            Pace.AssertBoth(true, GameSpeed.X4, ui, "settings closed: the player's own state was paused");
            Screen.Update(ui, In.Pause());
            Pace.AssertBoth(false, GameSpeed.X4, ui, "the player unpauses after the panel closed");

            // Opening and closing within one frame leaves the player's state.
            Screen.Update(ui, In.Settings(), In.Settings());
            Pace.AssertBoth(false, GameSpeed.X4, ui, "open and close in one frame");
            Assert.False(ui.Frame().SettingsOpen, "open and close in one frame leaves the panel closed");
        }

        [Fact]
        public void test_ui_initial_graphics_is_validated()
        {
            // 17 §17.7 (D10): the initial value is validated; Validate leaves Preset unchanged (15 §15.14).
            var initial = new GraphicsSettings(GraphicsPreset.Medium, true, 0, 5, 200, false);
            IUiController ui = UiFactory.CreateController(Layouts.Fixture(), new RecordingSink(), initial);
            var expected = new GraphicsSettings(GraphicsPreset.Medium, true, 1, 15, 100, false);
            Gfx.AssertSame(expected, RenderFactory.ValidateGraphics(initial), "15 §15.14 Validate");
            Gfx.AssertSame(expected, ui.Graphics, "IUiController.Graphics");
            Gfx.AssertSame(expected, ui.Frame().Graphics, "UiFrame.Graphics");
            Assert.False(ui.Frame().SettingsOpen, "SettingsOpen starts false");

            foreach (GraphicsSettings g in Gfx.EverySetting())
            {
                IUiController other = UiFactory.CreateController(Layouts.Fixture(), new RecordingSink(), g);
                Gfx.AssertSame(g, other.Graphics, "a valid initial value is kept as is");
                Gfx.AssertSame(g, other.Frame().Graphics, "a valid initial value is kept as is (Frame)");
            }
        }

        [Fact]
        public void test_ui_world_clicks_ignored_while_settings_open()
        {
            IUiController ui = Make(out RecordingSink sink);
            Screen.Update(ui, In.Settings(), In.Click(140f, 230f), In.RightClick(440f, 230f), In.Settings(), In.Click(540f, 230f));
            AssertRequests("[(6,+1)]", sink, "only the click after the panel closed counts");

            Screen.Update(ui, In.Settings());
            Screen.Update(ui, In.Click(140f, 230f));
            Screen.Update(ui, In.RightClick(140f, 230f), In.Click(440f, 230f));
            AssertRequests("[(6,+1)]", sink, "clicks in later frames while open");

            Screen.Update(ui, In.Settings(), In.RightClick(140f, 230f));
            AssertRequests("[(6,+1) (2,-1)]", sink, "a click after the panel closed in the same frame");
        }

        [Fact]
        public void test_ui_inputs_after_toggle_settings_see_new_state()
        {
            IUiController ui = Make(out _);
            Screen.Update(ui, In.Speed(GameSpeed.X2));
            Screen.Update(ui, In.Settings(), In.Pause(), In.Speed(GameSpeed.X4), In.Settings(), In.Speed(GameSpeed.X1));
            Pace.AssertBoth(false, GameSpeed.X1, ui, "[open, pause, X4, close, X1]");

            Screen.Update(ui, In.Pause(), In.Settings(), In.Pause(), In.Settings());
            Pace.AssertBoth(true, GameSpeed.X1, ui, "[pause, open, pause (ignored), close]");

            Screen.Update(ui, In.Settings(), In.Settings(), In.Pause(), In.Speed(GameSpeed.X4));
            Pace.AssertBoth(false, GameSpeed.X4, ui, "[open, close, pause, X4]");
        }

        // ------------------------------------------------------------ the graphics invariant (§17.4a)

        /// <summary>One frame's observable state, graphics excluded.</summary>
        private static string Observe(IUiController ui, RecordingSink sink)
        {
            UiFrame f = ui.Frame();
            return "pacing " + ui.Pacing.Paused + "/" + ui.Pacing.Speed + " frame " + f.Pacing.Paused + "/" + f.Pacing.Speed + " open " + f.SettingsOpen + " requests " + sink.Show();
        }

        private static UiInput RandomControl(SplitMix64 rng, bool clicksOnly = false)
        {
            int r = clicksOnly ? 99 : rng.Range(0, 99);
            if (r < 15)
            {
                return In.Pause();
            }

            if (r < 27)
            {
                return In.Speed(Speeds[rng.Range(0, Speeds.Length - 1)]);
            }

            if (r < 35)
            {
                return In.Settings();
            }

            // A click on a fixture box (edges included) or anywhere on the screen.
            float x;
            float y;
            if (rng.Range(0, 9) < 7)
            {
                int k = rng.Range(1, 9);
                x = ((k - 1) * 100) + (rng.Range(-2, 162) * 0.5f);
                y = 200 + (rng.Range(-2, 122) * 0.5f);
            }
            else
            {
                x = rng.Range(0, 1024);
                y = rng.Range(0, 512);
            }

            return rng.Range(0, 1) == 0 ? In.Click(x, y) : In.RightClick(x, y);
        }

        [Fact]
        public void test_ui_controls_and_hits_identical_at_every_graphics_setting()
        {
            const ulong seed = 0x6A11_0029UL;
            const int frames = 400;
            RenderLayout layout = Layouts.Fixture();

            // The script: frames of 0-4 control inputs, graphics-free.
            var script = new List<UiInput[]>();
            var rng = new SplitMix64(seed);
            for (int f = 0; f < frames; f++)
            {
                var inputs = new UiInput[rng.Range(0, 4)];
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = RandomControl(rng);
                }

                script.Add(inputs);
            }

            // What 17 §17.4/§17.4a/§17.5 say the script produces, at any graphics.
            var model = new UiModel(layout, Gfx.Of(GraphicsPreset.High));
            var reference = new RecordingSink();
            var expected = new List<string>();
            int pausedFrames = 0;
            int openFrames = 0;
            foreach (UiInput[] inputs in script)
            {
                model.Update(inputs, Screen.Identity, Screen.W, Screen.H);
                reference.Requests.Clear();
                reference.Requests.AddRange(model.Requests);
                expected.Add("pacing " + model.Paused + "/" + model.Speed + " frame " + model.Paused + "/" + model.Speed + " open " + model.SettingsOpen + " requests " + reference.Show());
                pausedFrames += model.Paused ? 1 : 0;
                openFrames += model.SettingsOpen ? 1 : 0;
            }

            Assert.True(model.Requests.Count > 20 && pausedFrames > 20 && openFrames > 20, "seed " + seed + ": the script exercises too little");

            // The same script at each preset and custom extreme, as the initial
            // value, then again with graphics inputs interleaved at random.
            var gfx = new SplitMix64(seed + 1UL);
            GraphicsSettings[] settings = Gfx.EverySetting();
            foreach (GraphicsSettings initial in settings)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var sink = new RecordingSink();
                    IUiController ui = UiFactory.CreateController(layout, sink, initial);
                    for (int f = 0; f < script.Count; f++)
                    {
                        var inputs = new List<UiInput>();
                        foreach (UiInput input in script[f])
                        {
                            if (pass == 1 && gfx.Range(0, 2) == 0)
                            {
                                inputs.Add(gfx.Range(0, 1) == 0
                                    ? In.Preset((GraphicsPreset)gfx.Range(0, 3))
                                    : In.Graphics(settings[gfx.Range(0, settings.Length - 1)]));
                            }

                            inputs.Add(input);
                        }

                        ui.Update(inputs, Screen.Identity, Screen.W, Screen.H);
                        string actual = Observe(ui, sink);
                        Assert.True(
                            actual == expected[f],
                            "seed " + seed + ", initial " + Gfx.Show(initial) + ", pass " + pass + ", frame " + f + ":\n expected " + expected[f] + "\n got      " + actual);
                    }
                }
            }
        }

        [Fact]
        public void test_ui_controller_matches_spec_model_over_random_inputs()
        {
            GraphicsSettings[] pool = Gfx.EverySetting()
                .Concat(new[]
                {
                    new GraphicsSettings(GraphicsPreset.Low, true, 0, 5, 300, true),
                    new GraphicsSettings(GraphicsPreset.High, false, 9999, -3, 10, false),
                    new GraphicsSettings(GraphicsPreset.Medium, true, 300, 241, 49, true),
                })
                .ToArray();
            CameraView[] cameras = { Screen.Identity, Screen.Halved };
            RenderLayout[] layouts = { Layouts.Fixture(), Layouts.Overlap() };

            for (ulong seed = 0x0DE1_0029UL; seed < 0x0DE1_0029UL + 8UL; seed++)
            {
                var rng = new SplitMix64(seed);
                RenderLayout layout = layouts[(int)(seed % 2UL)];
                GraphicsSettings initial = pool[rng.Range(0, pool.Length - 1)];
                var sink = new RecordingSink();
                IUiController ui = UiFactory.CreateController(layout, sink, initial);
                var model = new UiModel(layout, initial);
                for (int frame = 0; frame < 300; frame++)
                {
                    CameraView cam = cameras[rng.Range(0, cameras.Length - 1)];
                    var inputs = new UiInput[rng.Range(0, 5)];
                    for (int i = 0; i < inputs.Length; i++)
                    {
                        int r = rng.Range(0, 99);
                        if (r < 6)
                        {
                            inputs[i] = In.Preset((GraphicsPreset)rng.Range(0, 3));
                        }
                        else if (r < 12)
                        {
                            inputs[i] = In.Graphics(pool[rng.Range(0, pool.Length - 1)]);
                        }
                        else
                        {
                            inputs[i] = RandomControl(rng, clicksOnly: r >= 60);
                            if (inputs[i].Kind == UiInputKind.PrimaryClick || inputs[i].Kind == UiInputKind.SecondaryClick)
                            {
                                inputs[i] = Retarget(inputs[i], layout, cam, rng);
                            }
                        }
                    }

                    ui.Update(inputs, cam, Screen.W, Screen.H);
                    model.Update(inputs, cam, Screen.W, Screen.H);

                    string where = "seed " + seed + ", frame " + frame;
                    Pace.AssertBoth(model.Paused, model.Speed, ui, where);
                    UiFrame f = ui.Frame();
                    Assert.True(f.SettingsOpen == model.SettingsOpen, where + ": SettingsOpen " + f.SettingsOpen + ", expected " + model.SettingsOpen);
                    Gfx.AssertSame(model.Graphics, ui.Graphics, where + ": Graphics");
                    Gfx.AssertSame(model.Graphics, f.Graphics, where + ": UiFrame.Graphics");
                    var expected = new RecordingSink();
                    expected.Requests.AddRange(model.Requests);
                    Assert.True(expected.Show() == sink.Show(), where + ": requests\n expected " + expected.Show() + "\n got      " + sink.Show());
                }

                Assert.True(model.Requests.Count > 10, "seed " + seed + ": the run made too few requests to mean anything");
            }
        }

        /// <summary>A click aimed at the layout through cam: a box corner, edge or interior, or a near miss.</summary>
        private static UiInput Retarget(in UiInput click, in RenderLayout layout, in CameraView cam, SplitMix64 rng)
        {
            FlowNodeBox b = layout.FlowNodes[rng.Range(0, layout.FlowNodes.Count - 1)];
            float wx = b.MinX + (rng.Range(-2, ((b.MaxX - b.MinX) * 2) + 2) * 0.5f);
            float wy = b.MinY + (rng.Range(-2, ((b.MaxY - b.MinY) * 2) + 2) * 0.5f);

            // The inverse of 17 §17.3; exact on this suite's dyadic cameras.
            float sx = (((wx - cam.Centre.X) / (cam.ViewHeight * cam.Aspect)) + 0.5f) * Screen.W;
            float sy = (((wy - cam.Centre.Y) / cam.ViewHeight) + 0.5f) * Screen.H;
            return new UiInput(click.Kind, default, new ScreenPoint(sx, sy), default, default);
        }

        // ------------------------------------------------------------ budget (§17.9)

        [Fact]
        [Trait("Category", "Budget")]
        public void test_ui_update_and_frame_allocate_nothing_after_first_call()
        {
            var sink = new CountingSink();
            IUiController ui = UiFactory.CreateController(Layouts.Fixture(), sink, Gfx.Of(GraphicsPreset.Medium));
            GraphicsSettings custom = Gfx.Custom(true, 7, 0, 63, false);

            // Every input kind, hits and misses, the panel opened and closed.
            var a = new List<UiInput>
            {
                In.Pause(), In.Speed(GameSpeed.X2), In.Click(140f, 230f), In.RightClick(440f, 230f), In.Click(90f, 230f),
                In.Settings(), In.Click(540f, 230f), In.Preset(GraphicsPreset.Low), In.Graphics(custom), In.Preset(GraphicsPreset.Custom),
                In.Settings(), In.Pause(), In.Speed(GameSpeed.X4), In.Click(712f, 256f),
            };
            UiInput[] b = { In.Settings(), In.Preset(GraphicsPreset.High), In.Settings(), In.RightClick(840f, 260f), In.Speed(GameSpeed.X1) };
            UiInput[] none = Array.Empty<UiInput>();

            ui.Update(a, Screen.Identity, Screen.W, Screen.H);
            ui.Frame();

            long before = sink.Count;
            long start = Allocation.Start();
            for (int frame = 0; frame < 300; frame++)
            {
                CameraView cam = frame % 2 == 0 ? Screen.Identity : Screen.Halved;
                IReadOnlyList<UiInput> inputs = (frame % 3) switch
                {
                    0 => a,
                    1 => b,
                    _ => none,
                };
                ui.Update(inputs, cam, Screen.W, Screen.H);
                ui.Frame();
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "Update + Frame allocated " + allocated + " bytes over 300 frames after the first call (17 §17.9)");
            Assert.True(sink.Count - before > 300, "the metered frames made only " + (sink.Count - before) + " requests");
        }

        // ------------------------------------------------------------ integration (§17.10)

        [Fact]
        public void test_ui_graphics_changes_do_not_change_outcome()
        {
            const ulong seed = 0x6C4A_0029UL;
            const int maxFrames = 200000;
            GraphicsSettings[] choices = Gfx.EverySetting();
            var loop = new FrameLoop(Gfx.Of(GraphicsPreset.Medium));
            var model = new UiModel(loop.Layout, Gfx.Of(GraphicsPreset.Medium));
            var rng = new SplitMix64(seed);
            var seen = new HashSet<string>();
            int settingsPausedFrames = 0;

            while (loop.Sim.Host.CurrentTick < UiConst.TicksPerDay)
            {
                Assert.True(loop.Frames < maxFrames, "seed " + seed + ": no sim-day after " + maxFrames + " frames");
                var inputs = new List<UiInput>();
                if (model.SettingsOpen ? rng.Range(0, 9) == 0 : rng.Range(0, 399) == 0)
                {
                    inputs.Add(In.Settings());
                }

                if (rng.Range(0, 29) == 0)
                {
                    inputs.Add(rng.Range(0, 1) == 0
                        ? In.Preset((GraphicsPreset)rng.Range(0, 3))
                        : In.Graphics(choices[rng.Range(0, choices.Length - 1)]));
                }

                if (model.PlayerPaused ? rng.Range(0, 11) == 0 : rng.Range(0, 299) == 0)
                {
                    inputs.Add(In.Pause());
                }

                if (rng.Range(0, 99) == 0)
                {
                    inputs.Add(In.Speed(Speeds[rng.Range(0, Speeds.Length - 1)]));
                }

                model.Update(inputs, Screen.Identity, Screen.W, Screen.H);
                (uint _, UiFrame frame) = loop.Run(inputs, Screen.Identity, rng.Range(4000, 250000));

                string where = "seed " + seed + ", frame " + (loop.Frames - 1);
                Pace.AssertIs(model.Paused, model.Speed, frame.Pacing, where);
                Assert.True(frame.SettingsOpen == model.SettingsOpen, where + ": SettingsOpen");
                Gfx.AssertSame(model.Graphics, frame.Graphics, where + ": UiFrame.Graphics");
                seen.Add(Gfx.Show(frame.Graphics));
                if (model.SettingsOpen && !model.PlayerPaused)
                {
                    settingsPausedFrames++;
                }
            }

            foreach (GraphicsSettings g in choices)
            {
                Assert.True(seen.Contains(Gfx.Show(g)), "the day never showed " + Gfx.Show(g));
            }

            Assert.True(settingsPausedFrames > 0, "the settings panel never paused an otherwise running game");
            Assert.True(loop.PreferenceWrites > 0, "the graphics never changed");
            Assert.True(loop.UiHost.Submits == 0, "a run with no clicks submitted " + loop.UiHost.Submits + " commands");
            loop.AssertNeutral("graphics changes");
        }

        [Fact]
        public void test_ui_lane_click_through_real_sim_changes_servers_open()
        {
            // The production sink over the real sim: a click while paused is
            // still submitted, and applies at the next tick the game runs (17 §17.5).
            var loop = new FrameLoop(Gfx.Of(GraphicsPreset.High));
            const uint queue = 5;
            Assert.True(loop.Sim.Flow.TryGetLaneState(new NodeId(queue), out LaneState before), "flow fixture node " + queue + " is not a Queue");
            Assert.True(before.ServerCount > 0, "flow fixture node " + queue + " has no server to open or close");
            bool open = before.ServersOpen < before.ServerCount;
            int target = before.ServersOpen + (open ? 1 : -1);
            FlowNodeBox box = Layouts.Box(loop.Layout, queue);
            float cx = (box.MinX + box.MaxX) / 2f;
            float cy = (box.MinY + box.MaxY) / 2f;
            UiInput click = open ? In.Click(cx, cy) : In.RightClick(cx, cy);

            loop.Run(In.List(In.Speed(GameSpeed.X1)), Screen.Identity, 300000);
            ulong at = loop.Sim.Host.CurrentTick;
            loop.Run(In.List(In.Pause(), click), Screen.Identity, 300000);
            Assert.True(loop.Sim.Host.CurrentTick == at, "the game stepped while paused");

            IReadOnlyList<Command> log = loop.Sim.Host.CommandLogSince(0UL);
            Assert.True(log.Count == 1, "expected one admitted command, got " + log.Count);
            Command cmd = log[0];
            Assert.Equal(CommandKind.SetServersOpen, cmd.Kind);
            Assert.Equal(SimConstants.PLAYER_LOCAL.Value, cmd.Issuer.Value);
            Assert.Equal(at + UiConst.CommandMinLeadTicks, cmd.Tick);
            Assert.Equal(new byte[] { (byte)queue, 0, 0, 0, (byte)target, 0, 0, 0 }, cmd.Payload);

            for (int i = 0; i < 20; i++)
            {
                loop.Run(In.List(), Screen.Identity, 300000);
            }

            Assert.True(loop.Sim.Host.CurrentTick == at, "the game stepped while paused");
            loop.Run(In.List(In.Pause()), Screen.Identity, 300000);
            while (loop.Sim.Host.CurrentTick <= cmd.Tick + 1UL)
            {
                loop.Run(In.List(), Screen.Identity, 300000);
            }

            Assert.True(loop.Sim.Flow.TryGetLaneState(new NodeId(queue), out LaneState after));
            Assert.True(after.ServersOpen == target, "ServersOpen " + after.ServersOpen + " after the click, expected " + target);
            Assert.Single(loop.Sim.Host.CommandLogSince(0UL));
            Assert.Empty(loop.Guard.Violations);
        }
    }
}
