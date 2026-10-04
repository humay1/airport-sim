using System.Collections.Generic;
using AirportSim.App.Render;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// The controller of 17 §17.3 to §17.5 written out as a reference, for
    /// the property and integration tests. Pacing (§17.4): the player's own
    /// pause and speed, reported pause = player's pause OR SettingsOpen.
    /// Settings (§17.4a): ToggleSettings and the two graphics inputs always
    /// apply; while open, pause, speed and clicks are ignored. Clicks
    /// (§17.3, §17.5): screen to world, closed-interval hit test, highest
    /// NodeId wins, one request per hit. Graphics never feed anything else.
    /// </summary>
    internal sealed class UiModel
    {
        public readonly List<(uint Node, int Delta)> Requests = new List<(uint Node, int Delta)>();
        public bool PlayerPaused;
        public GameSpeed Speed = GameSpeed.X1;
        public bool SettingsOpen;
        public GraphicsSettings Graphics;

        private readonly RenderLayout _layout;

        public UiModel(in RenderLayout layout, in GraphicsSettings initialGraphics)
        {
            _layout = layout;
            Graphics = RenderFactory.ValidateGraphics(initialGraphics);
        }

        public bool Paused => PlayerPaused || SettingsOpen;

        /// <summary>
        /// 17 §17.3, term by term as written. The suite's screens and cameras
        /// make every term a short dyadic fraction, so this is exact and equals
        /// any other evaluation order.
        /// </summary>
        public static (float X, float Y) ToWorld(in ScreenPoint at, in CameraView camera, float w, float h)
        {
            float x = camera.Centre.X + (((at.X / w) - 0.5f) * camera.ViewHeight * camera.Aspect);
            float y = camera.Centre.Y + (((at.Y / h) - 0.5f) * camera.ViewHeight);
            return (x, y);
        }

        /// <summary>17 §17.5 step 1: the containing box with the highest NodeId, or 0 for none.</summary>
        public static uint Hit(in RenderLayout layout, float x, float y)
        {
            uint best = 0;
            foreach (FlowNodeBox b in layout.FlowNodes)
            {
                if (x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && b.Node.Value > best)
                {
                    best = b.Node.Value;
                }
            }

            return best;
        }

        public void Update(IReadOnlyList<UiInput> inputs, in CameraView camera, float w, float h)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                UiInput input = inputs[i];
                switch (input.Kind)
                {
                    case UiInputKind.ToggleSettings:
                        SettingsOpen = !SettingsOpen;
                        break;
                    case UiInputKind.SetGraphicsPreset:
                        if (input.Preset != GraphicsPreset.Custom)
                        {
                            Graphics = RenderFactory.GraphicsForPreset(input.Preset);
                        }

                        break;
                    case UiInputKind.SetGraphicsSettings:
                        GraphicsSettings g = input.Graphics;
                        Graphics = RenderFactory.ValidateGraphics(new GraphicsSettings(GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode, g.FrameRateCap, g.ResolutionScalePercent, g.AntiAliasing));
                        break;
                    case UiInputKind.TogglePause:
                        if (!SettingsOpen)
                        {
                            PlayerPaused = !PlayerPaused;
                        }

                        break;
                    case UiInputKind.SetSpeed:
                        if (!SettingsOpen)
                        {
                            Speed = input.Speed;
                        }

                        break;
                    case UiInputKind.PrimaryClick:
                    case UiInputKind.SecondaryClick:
                        if (!SettingsOpen)
                        {
                            (float x, float y) = ToWorld(input.At, camera, w, h);
                            uint node = Hit(_layout, x, y);
                            if (node != 0)
                            {
                                Requests.Add((node, input.Kind == UiInputKind.PrimaryClick ? 1 : -1));
                            }
                        }

                        break;
                }
            }
        }
    }
}
