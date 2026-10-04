using System;
using System.Globalization;
using AirportSim.App.Render;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// The graphics preference text codec (17 §17.4a, D10, Q-103):
    /// "graphics 1 &lt;Preset name&gt; &lt;DrawAgents 0|1&gt; &lt;MaxDrawnAgentsPerNode&gt;
    /// &lt;FrameRateCap&gt; &lt;ResolutionScalePercent&gt; &lt;AntiAliasing 0|1&gt;". The
    /// decoder accepts exactly what the encoder can produce: single U+0020
    /// spaces, exact case-sensitive words, booleans 0/1, integers of ASCII
    /// digits with no sign and no leading zero that fit int32. Well-formed
    /// text decodes to Validate of the values as written, Preset as written;
    /// anything else, null included, decodes false with out = default. The
    /// encoder writes values as given and throws for a value it cannot encode.
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

            // A field that is exactly 0 is the one digit string allowed to start with 0.
            Assert.True(UiFactory.TryDecodeGraphicsPreference("graphics 1 Custom 0 1 0 50 0", out GraphicsSettings zeros), "a field of exactly 0 did not decode");
            Gfx.AssertSame(Gfx.Custom(false, 1, 0, 50, false), zeros, "\"graphics 1 Custom 0 1 0 50 0\"");

            string?[] malformed =
            {
                // missing, empty, wrong field count
                null,
                string.Empty,
                "graphics",
                "graphics 1",
                "graphics 1 Medium 1 64 60 100",
                "graphics 1 Medium 1 64 60 100 0 0",

                // fields 1 to 3: exact and case-sensitive; a preset is a name, never a number
                "graphics 2 Medium 1 64 60 100 0",
                "graphics 0 Medium 1 64 60 100 0",
                "graphics 01 Medium 1 64 60 100 0",
                "Graphics 1 Medium 1 64 60 100 0",
                "GRAPHICS 1 Medium 1 64 60 100 0",
                "gfx 1 Medium 1 64 60 100 0",
                "graphics 1 Ultra 1 64 60 100 0",
                "graphics 1 medium 1 64 60 100 0",
                "graphics 1 MEDIUM 1 64 60 100 0",
                "graphics 1 1 1 64 60 100 0",

                // booleans are exactly 0 or 1
                "graphics 1 Medium 2 64 60 100 0",
                "graphics 1 Medium true 64 60 100 0",
                "graphics 1 Medium 01 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 2",
                "graphics 1 Medium 1 64 60 100 -1",

                // integers: ASCII digits, no sign, no leading zero, no point, exponent or separator, int32
                "graphics 1 Medium 1 6x 60 100 0",
                "graphics 1 Medium 1 -5 60 100 0",
                "graphics 1 Medium 1 +64 60 100 0",
                "graphics 1 Medium 1 64 -0 100 0",
                "graphics 1 Medium 1 064 60 100 0",
                "graphics 1 Medium 1 64 00 100 0",
                "graphics 1 Medium 1 64 60 0100 0",
                "graphics 1 Medium 1 64.0 60 100 0",
                "graphics 1 Medium 1 64 60. 100 0",
                "graphics 1 Medium 1 64 60 1e2 0",
                "graphics 1 Medium 1 1,000 60 100 0",
                "graphics 1 Medium 1 64 60 1_00 0",
                "graphics 1 Medium 1 0x40 60 100 0",
                "graphics 1 Medium 1 \u0666\u0664 60 100 0",
                "graphics 1 Medium 1 \uFF16\uFF14 60 100 0",
                "graphics 1 Medium 1 2147483648 60 100 0",
                "graphics 1 Medium 1 64 4294967296 100 0",
                "graphics 1 Medium 1 99999999999 60 100 0",

                // single U+0020 spaces only, nothing before or after
                "graphics  1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64  60 100 0",
                " graphics 1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 0 ",
                "graphics\t1 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64 60 100 0\n",
                "graphics 1 Medium 1 64 60 100 0\r",
                "graphics 1 Medium 1 64 60 100 0\r\n",
                "graphics\u00A01 Medium 1 64 60 100 0",
                "graphics 1 Medium 1 64 60 100\u20030",
            };
            GraphicsSettings none = default;
            foreach (string? text in malformed)
            {
                string shown = text == null ? "null" : "\"" + text.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
                bool ok = UiFactory.TryDecodeGraphicsPreference(text!, out GraphicsSettings result);
                Assert.False(ok, shown + " decoded, but 17 §17.4a (Q-103) makes it malformed");
                Gfx.AssertSame(none, result, shown + ": the out value after a false decode");
            }
        }

        [Fact]
        public void test_ui_graphics_preference_decode_validates()
        {
            // Well-formed text decodes to Validate of the values as written,
            // Preset as written, even out of range or not matching the named preset.
            (string Text, GraphicsSettings Expected)[] cases =
            {
                ("graphics 1 Custom 1 9999 7 10 1", new GraphicsSettings(GraphicsPreset.Custom, true, 256, 15, 50, true)),
                ("graphics 1 Low 1 0 0 100 0", new GraphicsSettings(GraphicsPreset.Low, true, 1, 0, 100, false)),
                ("graphics 1 High 0 64 1000 200 1", new GraphicsSettings(GraphicsPreset.High, false, 64, 240, 100, true)),
                ("graphics 1 Low 1 256 0 100 1", new GraphicsSettings(GraphicsPreset.Low, true, 256, 0, 100, true)),
                ("graphics 1 Medium 0 1 15 50 1", new GraphicsSettings(GraphicsPreset.Medium, false, 1, 15, 50, true)),
                ("graphics 1 Custom 1 2147483647 2147483647 2147483647 0", new GraphicsSettings(GraphicsPreset.Custom, true, 256, 240, 100, false)),
            };

            foreach ((string text, GraphicsSettings expected) in cases)
            {
                Assert.True(UiFactory.TryDecodeGraphicsPreference(text, out GraphicsSettings g), "\"" + text + "\" did not decode");
                Gfx.AssertSame(expected, g, "\"" + text + "\"");
                Gfx.AssertSame(RenderFactory.ValidateGraphics(expected), g, "\"" + text + "\" against 15 §15.14 Validate");
            }
        }

        [Fact]
        public void test_ui_graphics_preference_encoder_writes_values_as_given_and_rejects_unencodable()
        {
            // No Validate on the way out: out-of-range but non-negative values are written as they are.
            Assert.Equal("graphics 1 Custom 1 9999 7 10 1", UiFactory.EncodeGraphicsPreference(Gfx.Custom(true, 9999, 7, 10, true)));
            Assert.Equal("graphics 1 Low 1 256 0 100 1", UiFactory.EncodeGraphicsPreference(new GraphicsSettings(GraphicsPreset.Low, true, 256, 0, 100, true)));
            Assert.Equal(
                "graphics 1 High 0 0 2147483647 0 0",
                UiFactory.EncodeGraphicsPreference(new GraphicsSettings(GraphicsPreset.High, false, 0, int.MaxValue, 0, false)));

            // No encoding: a Preset outside GraphicsPreset, or a negative integer knob.
            GraphicsSettings[] unencodable =
            {
                new GraphicsSettings((GraphicsPreset)4, true, 64, 60, 100, false),
                new GraphicsSettings((GraphicsPreset)(-1), true, 64, 60, 100, false),
                Gfx.Custom(true, -1, 60, 100, false),
                Gfx.Custom(true, 64, -1, 100, false),
                Gfx.Custom(true, 64, 60, -1, false),
                Gfx.Custom(true, int.MinValue, 60, 100, false),
            };
            foreach (GraphicsSettings g in unencodable)
            {
                ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => UiFactory.EncodeGraphicsPreference(g));
                Assert.Equal("settings", e.ParamName);
            }
        }
    }
}
