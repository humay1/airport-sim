using System;

namespace AirportSim.App.Render
{
    /// <summary>The module's one factory: stateless static methods only. Spec: 15 §15.9, §15.14; 08 §8.11a.</summary>
    public static class RenderFactory
    {
        /// <summary>Creates the layout loader (§15.4).</summary>
        public static IRenderLayoutLoader CreateLayoutLoader()
        {
            return new RenderLayoutLoader();
        }

        /// <summary>Creates the scene builder over the sources and a validated layout (§15.9). Reads IAirsideSystem.Layout once.</summary>
        public static ISceneBuilder CreateSceneBuilder(in RenderSources sources, in RenderLayout layout)
        {
            return new SceneBuilder(sources, layout, LooksLoader.Defaults());
        }

        /// <summary>Creates the scene builder with the given looks; the two-argument overload uses <see cref="DefaultLooks"/> (§15.9, Q-130).</summary>
        public static ISceneBuilder CreateSceneBuilder(in RenderSources sources, in RenderLayout layout, in RenderLooks looks)
        {
            return new SceneBuilder(sources, layout, looks);
        }

        /// <summary>Reads <c>looks/looks.json</c> from the source (§15.16, Q-130). Every fault is a <see cref="FormatException"/> starting with the file's path.</summary>
        public static RenderLooks LoadLooks(AirportSim.Sim.Core.IContentSource source)
        {
            return LooksLoader.Load(source);
        }

        /// <summary>The looks used when none are given: a fresh value per call (§15.16, Q-130).</summary>
        public static RenderLooks DefaultLooks()
        {
            return LooksLoader.Defaults();
        }

        /// <summary>Creates the promotion controller (§15.7).</summary>
        public static IPromotionController CreatePromotionController(in RenderSources sources, in RenderLayout layout)
        {
            return new PromotionController(sources, layout);
        }

        /// <summary>Creates a tick pacer with an empty accumulator (§15.8).</summary>
        public static ITickPacer CreatePacer()
        {
            return new TickPacer();
        }

        /// <summary>The settings of a named preset (§15.14). Custom throws <see cref="ArgumentException"/>.</summary>
        public static GraphicsSettings GraphicsForPreset(GraphicsPreset preset)
        {
            switch (preset)
            {
                case GraphicsPreset.Low:
                    return new GraphicsSettings(GraphicsPreset.Low, false, 32, 60, 75, false);
                case GraphicsPreset.Medium:
                    return new GraphicsSettings(GraphicsPreset.Medium, true, 64, 60, 100, false);
                case GraphicsPreset.High:
                    return new GraphicsSettings(GraphicsPreset.High, true, RenderConstants.MAX_DRAWN_AGENTS_PER_NODE, 0, 100, true);
                default:
                    throw new ArgumentException("no settings are defined for preset " + preset, nameof(preset));
            }
        }

        /// <summary>Clamps every knob into its range and leaves Preset unchanged (§15.14).</summary>
        public static GraphicsSettings ValidateGraphics(in GraphicsSettings s)
        {
            int agents = Clamp(s.MaxDrawnAgentsPerNode, 1, RenderConstants.MAX_DRAWN_AGENTS_PER_NODE);
            int cap = s.FrameRateCap == 0 ? 0 : Clamp(s.FrameRateCap, 15, 240);
            int resolution = Clamp(s.ResolutionScalePercent, 50, 100);
            return new GraphicsSettings(s.Preset, s.DrawAgents, agents, cap, resolution, s.AntiAliasing);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
