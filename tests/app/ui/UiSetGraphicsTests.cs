using AirportSim.App.Render;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// SetGraphicsPreset and SetGraphicsSettings (17 §17.4a, D10): presets
    /// through 15 §15.14's ForPreset with Custom ignored, settings through
    /// Validate with Preset = Custom, both whether or not the panel is open,
    /// reported by IUiController.Graphics and UiFrame.Graphics.
    /// </summary>
    public sealed class UiSetGraphicsTests
    {
        private static void AssertGraphics(in GraphicsSettings expected, IUiController ui, string what)
        {
            Gfx.AssertSame(expected, ui.Graphics, what + " (Graphics)");
            Gfx.AssertSame(expected, ui.Frame().Graphics, what + " (Frame().Graphics)");
        }

        [Fact]
        public void test_ui_set_graphics_preset_applies_preset_values_and_ignores_custom()
        {
            IUiController ui = UiFactory.CreateController(Layouts.Fixture(), new RecordingSink(), Gfx.Of(GraphicsPreset.High));
            AssertGraphics(Gfx.Of(GraphicsPreset.High), ui, "initial");

            Screen.Update(ui, In.Preset(GraphicsPreset.Low));
            AssertGraphics(Gfx.Of(GraphicsPreset.Low), ui, "SetGraphicsPreset(Low)");

            Screen.Update(ui, In.Preset(GraphicsPreset.Custom));
            AssertGraphics(Gfx.Of(GraphicsPreset.Low), ui, "SetGraphicsPreset(Custom) is ignored");

            Screen.Update(ui, In.Preset(GraphicsPreset.High), In.Preset(GraphicsPreset.Medium));
            AssertGraphics(Gfx.Of(GraphicsPreset.Medium), ui, "[High, Medium]: input order");

            // With the panel open, too; and Custom after a custom value keeps it.
            GraphicsSettings custom = Gfx.Custom(true, 7, 0, 63, false);
            Screen.Update(ui, In.Settings(), In.Preset(GraphicsPreset.Low));
            AssertGraphics(Gfx.Of(GraphicsPreset.Low), ui, "SetGraphicsPreset(Low) with the panel open");
            Screen.Update(ui, In.Graphics(custom), In.Preset(GraphicsPreset.Custom));
            AssertGraphics(custom, ui, "SetGraphicsPreset(Custom) after a custom value");
            Screen.Update(ui, In.Preset(GraphicsPreset.High), In.Settings());
            AssertGraphics(Gfx.Of(GraphicsPreset.High), ui, "SetGraphicsPreset(High), then the panel closed");
            Pace.AssertBoth(false, GameSpeed.X1, ui, "graphics inputs leave pacing alone");
        }

        [Fact]
        public void test_ui_set_graphics_settings_marks_custom_and_validates()
        {
            IUiController ui = UiFactory.CreateController(Layouts.Fixture(), new RecordingSink(), Gfx.Of(GraphicsPreset.Medium));

            // In range: copied, Preset forced to Custom.
            Screen.Update(ui, In.Graphics(new GraphicsSettings(GraphicsPreset.Low, true, 7, 0, 63, false)));
            AssertGraphics(Gfx.Custom(true, 7, 0, 63, false), ui, "an in-range value claiming Low");

            // Out of range: Validate clamps every knob (15 §15.14 bounds).
            var high = new GraphicsSettings(GraphicsPreset.High, false, 9999, 7, 10, true);
            Screen.Update(ui, In.Graphics(high));
            AssertGraphics(Gfx.Custom(false, 256, 15, 50, true), ui, "an out-of-range value claiming High");
            AssertGraphics(RenderFactory.ValidateGraphics(Gfx.Custom(false, 9999, 7, 10, true)), ui, "against 15 §15.14 Validate");

            // With the panel open, in input order.
            Screen.Update(
                ui,
                In.Settings(),
                In.Graphics(new GraphicsSettings(GraphicsPreset.Medium, true, -5, 1000, 500, false)),
                In.Graphics(new GraphicsSettings(GraphicsPreset.Custom, true, 1, 240, 100, true)));
            AssertGraphics(Gfx.Custom(true, 1, 240, 100, true), ui, "the last of two values with the panel open");
            Screen.Update(ui, In.Graphics(new GraphicsSettings(GraphicsPreset.Medium, true, -5, 1000, 500, false)));
            AssertGraphics(Gfx.Custom(true, 1, 240, 100, false), ui, "negative and oversized knobs clamp");
            Pace.AssertBoth(true, GameSpeed.X1, ui, "the panel is still open");
        }
    }
}
