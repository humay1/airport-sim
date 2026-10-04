using System.Globalization;
using AirportSim.App.Render;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// The graphics preference text codec (17 §17.4a, D10):
    /// "graphics 1 &lt;Preset name&gt; &lt;DrawAgents 0|1&gt; &lt;MaxDrawnAgentsPerNode&gt;
    /// &lt;FrameRateCap&gt; &lt;ResolutionScalePercent&gt; &lt;AntiAliasing 0|1&gt;", single
    /// spaces, decimal numbers, invariant formatting; the decoder runs
    /// Validate; a missing, unknown or malformed value decodes to false.
    /// </summary>
    public sealed class UiGraphicsPreferenceTests
    {
        private static string Expected(in GraphicsSettings g)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "graphics 1 {0} {1} {2} {3} {4} {5}",
                g.Preset.ToString(),
                g.DrawAgents ? 1 : 0,
                g.MaxDrawnAgentsPerNode,
                g.FrameRateCap,
                g.ResolutionScalePercent,
                g.AntiAliasing ? 1 : 0);
        }

        [Fact]
        public void test_ui_graphics_preference_round_trips_and_rejects_malformed()
        {
            Assert.Equal("graphics 1 Custom 1 7 0 63 0", UiFactory.EncodeGraphicsPreference(Gfx.Custom(true, 7, 0, 63, false)));
            Assert.Equal("graphics 1 Custom 0 256 240 100 1", UiFactory.EncodeGraphicsPreference(Gfx.Custom(false, 256, 240, 100, true)));

            foreach (GraphicsSettings g in Gfx.EverySetting())
            {
                string text = UiFactory.EncodeGraphicsPreference(g);
                Assert.Equal(Expected(g), text);
                Assert.True(UiFactory.TryDecodeGraphicsPreference(text, out GraphicsSettings back), "\"" + text + "\" did not decode");
                Gfx.AssertSame(g, back, "round trip of \"" + text + "\"");
            }

            string medium = Expected(Gfx.Of(GraphicsPreset.Medium));
            Assert.True(UiFactory.TryDecodeGraphicsPreference(medium, out _), "\"" + medium + "\" did not decode");

            string?[] malformed =
            {
                null,
                string.Empty,
                "graphics",
                "graphics 1",
                "graphics 1 Medium 1 64 60 100",
                "graphics 1 Medium 1 64 60 100 0 0",
                "graphics 2 Medium 1 64 60 100 0",
                "graphics 0 Medium 1 64 60 100 0",
                "Graphics 1 Medium 1 64 60 100 0",
                "gfx 1 Medium 1 64 60 100 0",
                "graphics 1 Ultra 1 64 60 100 0",
                "graphics 1 medium 1 64 60 100 0",
                "graphics 1 1 1 64 60 100 0",
                "graphics 1 Medium 2 64 60 100 0",
                "graphics 1 Medium true 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 2",
                "graphics 1 Medium 1 6x 60 100 0",
                "graphics 1 Medium 1 64.0 60 100 0",
                "graphics 1 Medium 1 64 60 1e2 0",
                "graphics 1 Medium 1 99999999999 60 100 0",
                "graphics  1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64  60 100 0",
                " graphics 1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 0 ",
                "graphics\t1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 0\n",
            };
            foreach (string? text in malformed)
            {
                Assert.False(UiFactory.TryDecodeGraphicsPreference(text!, out _), "\"" + (text ?? "null") + "\" decoded, but 17 §17.4a makes it malformed");
            }
        }

        [Fact]
        public void test_ui_graphics_preference_decode_validates()
        {
            // Well-formed text with out-of-range numbers decodes to Validate of its values (15 §15.14 bounds).
            (string Text, GraphicsSettings Expected)[] cases =
            {
                ("graphics 1 Custom 1 9999 7 10 1", new GraphicsSettings(GraphicsPreset.Custom, true, 256, 15, 50, true)),
                ("graphics 1 Low 1 0 0 100 0", new GraphicsSettings(GraphicsPreset.Low, true, 1, 0, 100, false)),
                ("graphics 1 High 0 64 1000 200 1", new GraphicsSettings(GraphicsPreset.High, false, 64, 240, 100, true)),
            };

            foreach ((string text, GraphicsSettings expected) in cases)
            {
                Assert.True(UiFactory.TryDecodeGraphicsPreference(text, out GraphicsSettings g), "\"" + text + "\" did not decode");
                Gfx.AssertSame(expected, g, "\"" + text + "\"");
                Gfx.AssertSame(RenderFactory.ValidateGraphics(expected), g, "\"" + text + "\" against 15 §15.14 Validate");
            }
        }
    }
}
