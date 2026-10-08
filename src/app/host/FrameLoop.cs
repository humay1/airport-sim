using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Core;

namespace AirportSim.App.Host
{
    /// <summary>
    /// The frame loop of 16 §16.6, the only caller of <c>ISimHost.Step</c> in a
    /// playable build. It reads no clock: elapsed time is passed in.
    /// </summary>
    internal sealed class FrameLoop : IFrameLoop
    {
        private readonly ISimHost _host;
        private readonly ISceneBuilder _scene;
        private readonly IPromotionController _promotion;
        private readonly ITickPacer _pacer;
        private readonly IUiController _ui;
        private readonly IPreferenceStore _preferences;

        // The value the preference store holds, as far as this loop knows. The
        // value the controller started with counts as written (16 §16.6).
        private GraphicsSettings _lastWritten;

        internal FrameLoop(ISimHost host, ISceneBuilder scene, IPromotionController promotion, ITickPacer pacer, IUiController ui, IPreferenceStore preferences)
        {
            _host = host;
            _scene = scene;
            _promotion = promotion;
            _pacer = pacer;
            _ui = ui;
            _preferences = preferences;
            _lastWritten = ui.Graphics;
        }

        public FrameOutput RunFrame(in FrameInput input)
        {
            CameraView camera = input.Camera;

            // 1) the UI may submit commands and change pacing and graphics.
            _ui.Update(input.Ui, in camera, input.ScreenWidth, input.ScreenHeight);

            // 2) promotion before Step.
            GraphicsSettings graphics = _ui.Graphics;
            _promotion.Update(in camera, in graphics);

            // 3) the tick count of this frame's real time.
            PacingState pacing = _ui.Pacing;
            uint n = _pacer.Advance(input.ElapsedRealMicroseconds, pacing.Paused, pacing.Speed);
            if (n > 0)
            {
                _host.Step(n);
            }

            // 4) the build after Step.
            RenderFrame render = _scene.Build(in camera, in graphics, _pacer.SubTickMicroseconds);

            // 5) write the graphics preference on change only.
            if (!Same(in graphics, in _lastWritten))
            {
                _preferences.Write(PresentationComposer.GraphicsKey, UiFactory.EncodeGraphicsPreference(in graphics));
                _lastWritten = graphics;
            }

            // 6)
            return new FrameOutput(render, _ui.Frame());
        }

        private static bool Same(in GraphicsSettings a, in GraphicsSettings b)
        {
            return a.Preset == b.Preset
                && a.DrawAgents == b.DrawAgents
                && a.MaxDrawnAgentsPerNode == b.MaxDrawnAgentsPerNode
                && a.FrameRateCap == b.FrameRateCap
                && a.ResolutionScalePercent == b.ResolutionScalePercent
                && a.AntiAliasing == b.AntiAliasing;
        }
    }
}
