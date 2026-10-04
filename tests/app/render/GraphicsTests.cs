using System;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>15 §15.14: the preset table, monotonicity and Validate's clamps (D10, Q-034).</summary>
    public sealed class GraphicsTests
    {
        private static void AssertSettings(in GraphicsSettings expected, in GraphicsSettings actual, string what)
        {
            Assert.True(Gfx.Show(expected) == Gfx.Show(actual), what + ": got " + Gfx.Show(actual) + ", expected " + Gfx.Show(expected));
        }

        /// <summary>FrameRateCap read as cost: 0 (uncapped) is the most expensive.</summary>
        private static long FrameCost(int cap)
        {
            return cap == 0 ? long.MaxValue : cap;
        }

        [Fact]
        public void test_graphics_presets_are_monotone_and_high_matches_phase1_behaviour()
        {
            GraphicsSettings low = RenderFactory.GraphicsForPreset(GraphicsPreset.Low);
            GraphicsSettings medium = RenderFactory.GraphicsForPreset(GraphicsPreset.Medium);
            GraphicsSettings high = RenderFactory.GraphicsForPreset(GraphicsPreset.High);

            AssertSettings(Gfx.High(), high, "High is the pre-D10 Phase 1 behaviour");
            Assert.Equal(GraphicsPreset.Low, low.Preset);
            Assert.Equal(GraphicsPreset.Medium, medium.Preset);

            GraphicsSettings[] order = { low, medium, high };
            for (int i = 1; i < order.Length; i++)
            {
                GraphicsSettings a = order[i - 1];
                GraphicsSettings b = order[i];
                string what = a.Preset + " <= " + b.Preset;
                Assert.True(!a.DrawAgents || b.DrawAgents, what + ": DrawAgents");
                Assert.True(a.MaxDrawnAgentsPerNode <= b.MaxDrawnAgentsPerNode, what + ": MaxDrawnAgentsPerNode");
                Assert.True(FrameCost(a.FrameRateCap) <= FrameCost(b.FrameRateCap), what + ": FrameRateCap");
                Assert.True(a.ResolutionScalePercent <= b.ResolutionScalePercent, what + ": ResolutionScalePercent");
                Assert.True(!a.AntiAliasing || b.AntiAliasing, what + ": AntiAliasing");
            }

            // Every preset is already valid.
            foreach (GraphicsSettings p in order)
            {
                AssertSettings(p, RenderFactory.ValidateGraphics(p), "Validate(" + p.Preset + ")");
            }

            Assert.Throws<ArgumentException>(() => RenderFactory.GraphicsForPreset(GraphicsPreset.Custom));
        }

        [Fact]
        public void test_graphics_low_and_medium_match_the_preset_table()
        {
            AssertSettings(
                new GraphicsSettings(GraphicsPreset.Low, false, 32, 60, 75, false),
                RenderFactory.GraphicsForPreset(GraphicsPreset.Low),
                "Low");
            AssertSettings(
                new GraphicsSettings(GraphicsPreset.Medium, true, 64, 60, 100, false),
                RenderFactory.GraphicsForPreset(GraphicsPreset.Medium),
                "Medium");
        }

        [Fact]
        public void test_graphics_settings_validate_clamps_every_knob()
        {
            (int In, int Out)[] maxAgents =
            {
                (int.MinValue, 1), (-5, 1), (0, 1), (1, 1), (2, 2), (255, 255), (256, 256), (257, 256), (int.MaxValue, 256),
            };
            (int In, int Out)[] frameCap =
            {
                (0, 0), (1, 15), (14, 15), (15, 15), (16, 16), (60, 60), (240, 240), (241, 240), (int.MaxValue, 240), (-1, 15), (int.MinValue, 15),
            };
            (int In, int Out)[] resolution =
            {
                (int.MinValue, 50), (0, 50), (49, 50), (50, 50), (75, 75), (100, 100), (101, 100), (int.MaxValue, 100),
            };
            GraphicsPreset[] presets = { GraphicsPreset.Low, GraphicsPreset.Medium, GraphicsPreset.High, GraphicsPreset.Custom };

            int i = 0;
            foreach ((int agentsIn, int agentsOut) in maxAgents)
            {
                foreach ((int capIn, int capOut) in frameCap)
                {
                    foreach ((int resIn, int resOut) in resolution)
                    {
                        // Each knob is clamped on its own; Preset and both bools pass through unchanged.
                        GraphicsPreset preset = presets[i % presets.Length];
                        bool drawAgents = i % 2 == 0;
                        bool aa = i % 3 == 0;
                        var input = new GraphicsSettings(preset, drawAgents, agentsIn, capIn, resIn, aa);
                        var expected = new GraphicsSettings(preset, drawAgents, agentsOut, capOut, resOut, aa);
                        AssertSettings(expected, RenderFactory.ValidateGraphics(input), "Validate(" + Gfx.Show(input) + ")");
                        i++;
                    }
                }
            }
        }
    }
}
