using System.Globalization;
using AirportSim.App.Render;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.App.Ui
{
    /// <summary>The one factory of the UI scene layer. Spec: 17 §17.7, §17.4a.</summary>
    public static class UiFactory
    {
        /// <summary>Creates the controller; <paramref name="initialGraphics"/> is validated (§17.7).</summary>
        public static IUiController CreateController(in RenderLayout layout, ILaneCommandSink sink, in GraphicsSettings initialGraphics)
        {
            return new UiController(layout, sink, initialGraphics);
        }

        /// <summary>Creates the production lane sink (§17.5 step 4).</summary>
        public static ILaneCommandSink CreateLaneCommandSink(ISimHost host, IFlowSystem flow)
        {
            return new LaneCommandSink(host, flow);
        }

        /// <summary>Encodes the graphics preference text (§17.4a).</summary>
        public static string EncodeGraphicsPreference(in GraphicsSettings settings)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "graphics 1 {0} {1} {2} {3} {4} {5}",
                settings.Preset.ToString(),
                settings.DrawAgents ? 1 : 0,
                settings.MaxDrawnAgentsPerNode,
                settings.FrameRateCap,
                settings.ResolutionScalePercent,
                settings.AntiAliasing ? 1 : 0);
        }

        /// <summary>Decodes and validates the graphics preference text; false when missing or malformed (§17.4a).</summary>
        public static bool TryDecodeGraphicsPreference(string text, out GraphicsSettings settings)
        {
            settings = default;
            if (text is null)
            {
                return false;
            }

            string[] t = text.Split(' ');
            if (t.Length != 8 || t[0] != "graphics" || t[1] != "1")
            {
                return false;
            }

            GraphicsPreset preset;
            switch (t[2])
            {
                case "Low":
                    preset = GraphicsPreset.Low;
                    break;
                case "Medium":
                    preset = GraphicsPreset.Medium;
                    break;
                case "High":
                    preset = GraphicsPreset.High;
                    break;
                case "Custom":
                    preset = GraphicsPreset.Custom;
                    break;
                default:
                    return false;
            }

            if (!TryFlag(t[3], out bool drawAgents)
                || !TryNumber(t[4], out int maxAgents)
                || !TryNumber(t[5], out int frameRateCap)
                || !TryNumber(t[6], out int resolution)
                || !TryFlag(t[7], out bool antiAliasing))
            {
                return false;
            }

            settings = RenderFactory.ValidateGraphics(new GraphicsSettings(preset, drawAgents, maxAgents, frameRateCap, resolution, antiAliasing));
            return true;
        }

        private static bool TryFlag(string s, out bool value)
        {
            value = s == "1";
            return value || s == "0";
        }

        private static bool TryNumber(string s, out int value)
        {
            value = 0;
            if (s.Length == 0 || s[0] == '+')
            {
                return false;
            }

            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }
    }
}
