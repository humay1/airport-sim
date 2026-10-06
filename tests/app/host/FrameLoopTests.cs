using System;
using System.Collections.Generic;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. The frame loop of 16 §16.6, built by IPresentationComposer over
    /// fakes (the Rig) and driven through IFrameLoop.RunFrame only. Its order is
    /// read off one shared call trace: the UI's lane sink submits through
    /// ISimHost.TrySubmit, promotion calls IFlowSystem.SetPromoted, the pacer's
    /// ticks reach ISimHost.Step, and the scene build reads CurrentTick and
    /// Population (15 §15.6, 17 §17.6).
    /// </summary>
    public sealed class FrameLoopTests
    {
        private const string Key = "airportsim.graphics";

        private static void AssertNoViolations(Rig rig)
        {
            Assert.True(rig.Trace.Violations.Count == 0, "unlisted sim members called: " + string.Join(", ", rig.Trace.Violations));
        }

        [Fact]
        public void test_frame_loop_runs_ui_then_promotion_then_step_then_build()
        {
            var rig = new Rig();
            Trace t = rig.Trace;
            t.Clear();

            // Frame 1: a primary click on lane node 5 (box 400..480 × 200..260)
            // and one tick of real time at 1x.
            FrameOutput first = rig.Frame(Rig.OneTick, In.ClickWorld(440f, 230f));

            int submit = t.First(Call.TrySubmit);
            int step = t.First(Call.Step);
            Assert.True(submit >= 0, "the click on lane node 5 submitted nothing: " + t.Show());
            Assert.True(step >= 0, "one tick of real time at 1x stepped nothing: " + t.Show());
            Assert.Equal(new List<ulong> { 1UL }, t.Steps());

            // 1) UI first: the command was submitted at tick 0, before Step, so it
            // is dated CurrentTick + COMMAND_MIN_LEAD_TICKS = 1 (17 §17.5).
            Assert.Single(rig.Host.Submitted);
            Assert.Equal(0UL + SimConstants.COMMAND_MIN_LEAD_TICKS, rig.Host.Submitted[0].Tick);

            // 2) Promotion after UI, before Step. The first Update sets every box,
            // ascending NodeId (15 §15.7).
            List<(ulong Node, bool Promoted)> promotions = t.Promotions();
            Assert.Equal(9, promotions.Count);
            for (int i = 0; i < 9; i++)
            {
                Assert.Equal((ulong)(i + 1), promotions[i].Node);
            }

            Assert.True(submit < t.First(Call.SetPromoted), "promotion ran before the UI's update: " + t.Show());
            Assert.True(t.Last(Call.SetPromoted) < step, "promotion ran after Step: " + t.Show());

            // 3) then 4): the build reads the sim after Step, and only then.
            Assert.True(t.First(Call.Population) > step, "the scene was built before Step: " + t.Show());
            Assert.True(t.Last(Call.CurrentTick) > step, "the scene did not read CurrentTick after Step: " + t.Show());
            Assert.True(t.Last(Call.TrySubmit) < step, "a command was submitted after Step: " + t.Show());
            Assert.Equal(1UL, first.Render.Tick);

            // Later frames: Step(n) exactly when the pacer returns n > 0 (16 §16.6
            // step 3), with promotion before it and the build after it. The
            // expected n come from an independent pacer fed the same frames.
            long[] elapsed = { Rig.OneTick * 5 / 2, Rig.OneTick / 2, Rig.OneTick * 2 / 5, Rig.OneTick * 10, Rig.OneTick };
            ITickPacer reference = RenderFactory.CreatePacer();
            Assert.Equal(1u, reference.Advance(Rig.OneTick, false, GameSpeed.X1));
            foreach (long e in elapsed)
            {
                uint n = reference.Advance(e, false, GameSpeed.X1);
                ulong before = rig.Host.Tick;
                t.Clear();
                FrameOutput o = rig.Frame(e, In.None);
                if (n == 0)
                {
                    Assert.True(t.Steps().Count == 0, "elapsed " + e + " µs: the pacer returns 0 but the frame stepped: " + t.Show());
                }
                else
                {
                    Assert.Equal(new List<ulong> { n }, t.Steps());
                    int s = t.First(Call.Step);
                    int lastPromotion = t.Last(Call.SetPromoted);
                    Assert.True(lastPromotion < s, "promotion ran after Step: " + t.Show());
                    Assert.True(t.First(Call.Population) > s, "the scene was built before Step: " + t.Show());
                }

                Assert.Equal(before + n, rig.Host.Tick);
                Assert.Equal(rig.Host.Tick, o.Render.Tick);
            }

            AssertNoViolations(rig);
        }

        [Fact]
        public void test_frame_loop_pause_pressed_this_frame_steps_nothing()
        {
            var rig = new Rig();
            Trace t = rig.Trace;

            // Control: an unpaused frame with one tick of real time steps once.
            t.Clear();
            FrameOutput o = rig.Frame(Rig.OneTick, In.None);
            Assert.Equal(new List<ulong> { 1UL }, t.Steps());
            Assert.False(o.Ui.Pacing.Paused);

            // The pause pressed this frame stops this frame's Step (16 §16.6).
            t.Clear();
            o = rig.Frame(Rig.OneTick * 3, In.TogglePause());
            Assert.True(t.Steps().Count == 0, "the frame that pressed pause stepped: " + t.Show());
            Assert.True(o.Ui.Pacing.Paused, "UiFrame.Pacing does not report the pause");
            Assert.Equal(1UL, o.Render.Tick);

            t.Clear();
            rig.Frame(Rig.OneTick * 2, In.None);
            Assert.True(t.Steps().Count == 0, "a paused frame stepped: " + t.Show());

            // Unpausing steps again in the same frame. The paused time is not
            // replayed (15 §15.8), so one tick of real time is one tick.
            t.Clear();
            o = rig.Frame(Rig.OneTick, In.TogglePause());
            Assert.Equal(new List<ulong> { 1UL }, t.Steps());
            Assert.False(o.Ui.Pacing.Paused);
            Assert.Equal(2UL, rig.Host.Tick);
            AssertNoViolations(rig);
        }

        [Fact]
        public void test_frame_loop_settings_opened_this_frame_steps_nothing()
        {
            var rig = new Rig();
            Trace t = rig.Trace;

            t.Clear();
            rig.Frame(Rig.OneTick, In.None);
            Assert.Equal(new List<ulong> { 1UL }, t.Steps());

            // Opening the settings panel this frame pauses this frame (17 §17.4a,
            // Q-034): Ui.Pacing.Paused already includes it.
            t.Clear();
            FrameOutput o = rig.Frame(Rig.OneTick * 3, In.ToggleSettings());
            Assert.True(t.Steps().Count == 0, "the frame that opened the settings panel stepped: " + t.Show());
            Assert.True(o.Ui.SettingsOpen, "UiFrame.SettingsOpen is false after ToggleSettings");
            Assert.True(o.Ui.Pacing.Paused, "UiFrame.Pacing does not report the panel's pause");

            t.Clear();
            rig.Frame(Rig.OneTick, In.None);
            Assert.True(t.Steps().Count == 0, "a frame with the panel open stepped: " + t.Show());

            // Closing it ends the pause in the same frame.
            t.Clear();
            o = rig.Frame(Rig.OneTick, In.ToggleSettings());
            Assert.Equal(new List<ulong> { 1UL }, t.Steps());
            Assert.False(o.Ui.SettingsOpen);
            Assert.False(o.Ui.Pacing.Paused);
            Assert.Equal(2UL, rig.Host.Tick);
            AssertNoViolations(rig);
        }

        [Fact]
        public void test_frame_loop_passes_ui_graphics_to_promotion_and_scene()
        {
            GraphicsSettings high = Gfx.Of(GraphicsPreset.High);
            var rig = new Rig(trace => new FakePreferences(trace).With(Key, UiFactory.EncodeGraphicsPreference(high)));
            Trace t = rig.Trace;

            // Frame 1 at High: every box is visible and the camera is zoomed in,
            // so every box is desired promoted (15 §15.7) and drawn with agents.
            t.Clear();
            FrameOutput o = rig.Frame(0, In.None);
            Gfx.AssertSame(high, o.Render.Graphics, "frame 1 RenderFrame.Graphics");
            Assert.All(t.Promotions(), p => Assert.True(p.Promoted));
            Assert.Equal(9, t.Promotions().Count);
            Assert.Equal(9, t.Count(Call.AgentsAt));

            // Low, chosen this frame, reaches promotion and the build in this
            // frame: DrawAgents false demotes every node and draws no agents.
            GraphicsSettings low = Gfx.Of(GraphicsPreset.Low);
            t.Clear();
            o = rig.Frame(0, In.Preset(GraphicsPreset.Low));
            Gfx.AssertSame(low, o.Render.Graphics, "frame 2 RenderFrame.Graphics");
            Gfx.AssertSame(low, o.Ui.Graphics, "frame 2 UiFrame.Graphics");
            Assert.Equal(9, t.Promotions().Count);
            Assert.All(t.Promotions(), p => Assert.False(p.Promoted));
            Assert.Equal(0, t.Count(Call.AgentsAt));
            for (uint node = 1; node <= 9; node++)
            {
                Assert.False(rig.Flow.IsPromoted(node), "node " + node + " still promoted at Low");
            }

            GraphicsSettings medium = Gfx.Of(GraphicsPreset.Medium);
            t.Clear();
            o = rig.Frame(0, In.Preset(GraphicsPreset.Medium));
            Gfx.AssertSame(medium, o.Render.Graphics, "frame 3 RenderFrame.Graphics");
            Assert.Equal(9, t.Promotions().Count);
            Assert.All(t.Promotions(), p => Assert.True(p.Promoted));
            Assert.Equal(9, t.Count(Call.AgentsAt));

            // A custom value is the validated copy marked Custom (17 §17.4a), and
            // that is what promotion and the build receive.
            var custom = new GraphicsSettings(GraphicsPreset.High, false, 7, 30, 80, true);
            GraphicsSettings expected = RenderFactory.ValidateGraphics(new GraphicsSettings(GraphicsPreset.Custom, false, 7, 30, 80, true));
            t.Clear();
            o = rig.Frame(0, In.Settings(custom));
            Gfx.AssertSame(expected, o.Render.Graphics, "frame 4 RenderFrame.Graphics");
            Gfx.AssertSame(expected, rig.Presentation.Ui.Graphics, "frame 4 Ui.Graphics");
            Assert.Equal(9, t.Promotions().Count);
            Assert.All(t.Promotions(), p => Assert.False(p.Promoted));
            Assert.Equal(0, t.Count(Call.AgentsAt));
            AssertNoViolations(rig);
        }

        [Fact]
        public void test_frame_loop_writes_graphics_preference_only_on_change()
        {
            // No stored preference: the default, Medium, counts as last written
            // (16 §16.6), so nothing is written until the graphics change.
            var rig = new Rig();
            FakePreferences prefs = rig.Preferences;
            Trace t = rig.Trace;
            Assert.Empty(prefs.Writes);

            rig.Frame(Rig.OneTick, In.None);
            Assert.Empty(prefs.Writes);

            t.Clear();
            rig.Frame(Rig.OneTick, In.Preset(GraphicsPreset.Low));
            Assert.Equal(new List<(string, string)> { (Key, UiFactory.EncodeGraphicsPreference(Gfx.Of(GraphicsPreset.Low))) }, prefs.Writes);

            // Step 5 comes after the build (step 4) and is the frame's last call.
            int write = t.Last(Call.PreferenceWrite);
            Assert.True(write == t.Entries.Count - 1, "the preference write is not the frame's last call: " + t.Show());
            Assert.True(write > t.Last(Call.Population), "the preference was written before the build: " + t.Show());

            rig.Frame(Rig.OneTick, In.None);
            rig.Frame(Rig.OneTick, In.Preset(GraphicsPreset.Low));
            Assert.Single(prefs.Writes);

            // Changed and changed back within one frame: no difference from the
            // value last written.
            rig.Frame(Rig.OneTick, In.Preset(GraphicsPreset.High), In.Preset(GraphicsPreset.Low));
            Assert.Single(prefs.Writes);

            // A custom value, set while the panel is open, is written as the
            // controller holds it.
            rig.Frame(0, In.ToggleSettings(), In.Settings(new GraphicsSettings(GraphicsPreset.Low, true, 7, 30, 80, true)));
            Assert.Equal(2, prefs.Writes.Count);
            Assert.Equal((Key, UiFactory.EncodeGraphicsPreference(rig.Presentation.Ui.Graphics)), prefs.Writes[1]);
            Assert.Equal(GraphicsPreset.Custom, rig.Presentation.Ui.Graphics.Preset);

            rig.Frame(0, In.ToggleSettings());
            Assert.Equal(2, prefs.Writes.Count);

            // Back to the default value: it differs from the last written, so it is written.
            rig.Frame(Rig.OneTick, In.Preset(GraphicsPreset.Medium));
            Assert.Equal(3, prefs.Writes.Count);
            Assert.Equal((Key, UiFactory.EncodeGraphicsPreference(Gfx.Of(GraphicsPreset.Medium))), prefs.Writes[2]);

            // A stored preference is the value last written: choosing it again writes nothing.
            var stored = new Rig(trace => new FakePreferences(trace).With(Key, UiFactory.EncodeGraphicsPreference(Gfx.Of(GraphicsPreset.High))));
            stored.Frame(Rig.OneTick, In.None);
            stored.Frame(Rig.OneTick, In.Preset(GraphicsPreset.High));
            Assert.Empty(stored.Preferences.Writes);
            stored.Frame(Rig.OneTick, In.Preset(GraphicsPreset.Low));
            Assert.Equal(new List<(string, string)> { (Key, UiFactory.EncodeGraphicsPreference(Gfx.Of(GraphicsPreset.Low))) }, stored.Preferences.Writes);
            AssertNoViolations(rig);
            AssertNoViolations(stored);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_frame_loop_allocates_nothing_after_first_call()
        {
            // 16 §16.10: RunFrame allocates nothing after the first call. Every
            // metered frame steps one tick and rebuilds the scene; the fakes and
            // the trace allocate nothing while recording is off.
            var rig = new Rig();
            FrameInput input = Rig.Input(Rig.OneTick, In.None);
            rig.Presentation.Frame.RunFrame(input);
            rig.Trace.Recording = false;

            long start = Allocation.Start();
            for (int frame = 0; frame < 240; frame++)
            {
                rig.Presentation.Frame.RunFrame(input);
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "RunFrame allocated " + allocated + " bytes over 240 frames after the first call (16 §16.10)");
            Assert.Equal(241, rig.Host.StepCalls);
            Assert.Equal(241UL, rig.Host.Tick);
            AssertNoViolations(rig);
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
}
