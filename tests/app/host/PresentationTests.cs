using System;
using System.Collections.Generic;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using Xunit;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. Presentation assembly (16 §16.5) and the graphics preference it
    /// reads (16 §16.6, D10, Q-034), through IPresentationComposer.Compose over
    /// the Rig's fakes.
    /// </summary>
    public sealed class PresentationTests
    {
        private const string Key = "airportsim.graphics";

        [Fact]
        public void test_presentation_uses_stored_graphics_preference_or_default()
        {
            GraphicsSettings medium = Gfx.Of(GraphicsPreset.Medium);
            GraphicsSettings low = Gfx.Of(GraphicsPreset.Low);
            Assert.True(UiFactory.TryDecodeGraphicsPreference("graphics 1 Custom 0 7 30 80 1", out GraphicsSettings custom));
            Assert.True(UiFactory.TryDecodeGraphicsPreference("graphics 1 Low 1 0 0 100 0", out GraphicsSettings clamped));

            var cases = new List<(string Label, Func<Trace, FakePreferences> Store, GraphicsSettings Expected)>
            {
                ("nothing stored: the default, Medium (Q-034)", tr => new FakePreferences(tr), medium),
                ("Low stored", tr => new FakePreferences(tr).With(Key, UiFactory.EncodeGraphicsPreference(low)), low),
                ("a custom value stored", tr => new FakePreferences(tr).With(Key, "graphics 1 Custom 0 7 30 80 1"), custom),
                ("an out-of-range value stored, decoded through Validate", tr => new FakePreferences(tr).With(Key, "graphics 1 Low 1 0 0 100 0"), clamped),
                ("a malformed value: lower-case preset", tr => new FakePreferences(tr).With(Key, "graphics 1 low 0 32 60 75 0"), medium),
                ("a malformed value: empty", tr => new FakePreferences(tr).With(Key, string.Empty), medium),
                ("a malformed value: trailing newline", tr => new FakePreferences(tr).With(Key, UiFactory.EncodeGraphicsPreference(low) + "\n"), medium),
                ("Low stored under another key only", tr => new FakePreferences(tr).With(Key + ".old", UiFactory.EncodeGraphicsPreference(low)).With("AirportSim.Graphics", UiFactory.EncodeGraphicsPreference(low)), medium),
            };

            foreach ((string label, Func<Trace, FakePreferences> store, GraphicsSettings expected) in cases)
            {
                var rig = new Rig(store);
                Assert.True(rig.Preferences.ReadKeys.Contains(Key), label + ": the composer never read " + Key + " (read: " + string.Join(", ", rig.Preferences.ReadKeys) + ")");
                Gfx.AssertSame(expected, rig.Presentation.Ui.Graphics, label + ": Ui.Graphics after Compose");

                FrameOutput o = rig.Frame(0, In.None);
                Gfx.AssertSame(expected, o.Ui.Graphics, label + ": first UiFrame.Graphics");
                Gfx.AssertSame(expected, o.Render.Graphics, label + ": first RenderFrame.Graphics");

                // The value it started with counts as last written (16 §16.6).
                Assert.True(rig.Preferences.Writes.Count == 0, label + ": a preference was written without a change");
                Assert.True(rig.Trace.Violations.Count == 0, label + ": " + string.Join(", ", rig.Trace.Violations));
            }
        }

        [Fact]
        public void test_presentation_returns_the_parts_the_frame_loop_drives()
        {
            // 16 §16.5: the parts are RenderFactory's and UiFactory's, and the
            // frame loop is built over them: what the UI holds is what a frame
            // reports, and the pacer is fresh (initial state: not paused, X1).
            var rig = new Rig();
            Presentation p = rig.Presentation;
            Assert.NotNull(p.Scene);
            Assert.NotNull(p.Promotion);
            Assert.NotNull(p.Pacer);
            Assert.NotNull(p.Ui);
            Assert.NotNull(p.Frame);
            Assert.False(p.Ui.Pacing.Paused);
            Assert.Equal(GameSpeed.X1, p.Ui.Pacing.Speed);

            FrameOutput o = rig.Frame(Rig.OneTick, In.TogglePause());
            Assert.True(p.Ui.Pacing.Paused, "Presentation.Ui is not the controller the frame loop updates");
            Assert.Equal(p.Ui.Pacing.Paused, o.Ui.Pacing.Paused);
            Assert.Equal(0UL, rig.Host.Tick);

            // A missing render layout is a hard load failure (16 §16.5).
            var noLayout = new MemoryBundle();
            var sim = new ComposedSim(new TraceHost(new Trace()), null, null, null, new TraceFlow(new Trace()), null, null);
            Assert.Throws<FormatException>(() => HostFactory.CreatePresentationComposer().Compose(sim, noLayout, new FakePreferences()));

            // So is one the layout loader rejects (15 §15.4: stand_size must be > 0).
            string text = System.Text.Encoding.UTF8.GetString(Repo.Read(Bundles.RenderLayout));
            Assert.Contains("\"stand_size\": 40", text);
            var badLayout = new MemoryBundle().Put("render_layout.fixture", Bundles.Utf8(text.Replace("\"stand_size\": 40", "\"stand_size\": 0")));
            FormatException e = Assert.Throws<FormatException>(() => HostFactory.CreatePresentationComposer().Compose(sim, badLayout, new FakePreferences()));
            Assert.StartsWith("render_layout.fixture: ", e.Message, StringComparison.Ordinal);
        }
    }
}
