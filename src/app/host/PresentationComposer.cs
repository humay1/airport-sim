using System;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Airside;

namespace AirportSim.App.Host
{
    /// <summary>Builds the scene-layer objects and the frame loop over a composed sim. Spec: 16 §16.5, §16.6.</summary>
    internal sealed class PresentationComposer : IPresentationComposer
    {
        internal const string LayoutFile = "render_layout.fixture";
        internal const string GraphicsKey = "airportsim.graphics";

        public Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences)
        {
            return Compose(in sim, bundle, preferences, RenderFactory.DefaultLooks());
        }

        public Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences, in RenderLooks looks)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            if (preferences == null)
            {
                throw new ArgumentNullException(nameof(preferences));
            }

            var sources = new RenderSources(sim.Host, sim.Airside, sim.Flow, sim.Schedule, sim.Content);

            if (!bundle.Has(LayoutFile))
            {
                throw new FormatException(LayoutFile + ": the bundle has no such file");
            }

            AirsideLayout? airside = sim.Airside != null ? sim.Airside.Layout() : (AirsideLayout?)null;
            RenderLayout layout = RenderFactory.CreateLayoutLoader().Load(bundle.ReadAll(LayoutFile), LayoutFile, in airside);

            ISceneBuilder scene = RenderFactory.CreateSceneBuilder(in sources, in layout, in looks);
            IPromotionController promotion = RenderFactory.CreatePromotionController(in sources, in layout);
            ITickPacer pacer = RenderFactory.CreatePacer();

            // 16 §16.6 (D10): the stored preference if it decodes, else the default of 15 §15.14.
            GraphicsSettings initial = RenderFactory.GraphicsForPreset(GraphicsPreset.Medium);
            if (preferences.TryRead(GraphicsKey, out string stored) && UiFactory.TryDecodeGraphicsPreference(stored, out GraphicsSettings decoded))
            {
                initial = decoded;
            }

            ILaneCommandSink sink = UiFactory.CreateLaneCommandSink(sim.Host, sim.Flow!);
            IUiController ui = UiFactory.CreateController(in layout, sink, in initial);

            var frame = new FrameLoop(sim.Host, scene, promotion, pacer, ui, preferences);
            return new Presentation(scene, promotion, pacer, ui, frame);
        }
    }
}
