using System;
using System.Collections.Generic;
using AirportSim.App.Render;
using AirportSim.Sim.Core;

namespace AirportSim.App.Ui
{
    // Spec: 17 §17.3 to §17.5.
    internal sealed class UiController : IUiController
    {
        private readonly FlowNodeBox[] _boxes;
        private readonly ILaneCommandSink _sink;

        private bool _playerPaused;
        private GameSpeed _speed = GameSpeed.X1;
        private bool _settingsOpen;
        private GraphicsSettings _graphics;

        internal UiController(in RenderLayout layout, ILaneCommandSink sink, in GraphicsSettings initialGraphics)
        {
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            if (layout.FlowNodes is null)
            {
                throw new ArgumentException("the layout has no flow node list", nameof(layout));
            }

            _boxes = new FlowNodeBox[layout.FlowNodes.Count];
            for (int i = 0; i < _boxes.Length; i++)
            {
                _boxes[i] = layout.FlowNodes[i];
            }

            _sink = sink;
            _graphics = RenderFactory.ValidateGraphics(initialGraphics);
        }

        public PacingState Pacing => new PacingState(_playerPaused || _settingsOpen, _speed);

        public GraphicsSettings Graphics => _graphics;

        public UiFrame Frame()
        {
            return new UiFrame(Pacing, _settingsOpen, _graphics);
        }

        public void Update(IReadOnlyList<UiInput> inputs, in CameraView camera, float screenWidth, float screenHeight)
        {
            if (inputs is null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (!(screenWidth > 0f) || !(screenHeight > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(screenWidth), "the screen must be larger than zero in both directions");
            }

            for (int i = 0; i < inputs.Count; i++)
            {
                UiInput input = inputs[i];
                switch (input.Kind)
                {
                    case UiInputKind.ToggleSettings:
                        _settingsOpen = !_settingsOpen;
                        break;
                    case UiInputKind.SetGraphicsPreset:
                        if (input.Preset != GraphicsPreset.Custom)
                        {
                            _graphics = RenderFactory.GraphicsForPreset(input.Preset);
                        }

                        break;
                    case UiInputKind.SetGraphicsSettings:
                        GraphicsSettings g = input.Graphics;
                        _graphics = RenderFactory.ValidateGraphics(new GraphicsSettings(
                            GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode, g.FrameRateCap, g.ResolutionScalePercent, g.AntiAliasing));
                        break;
                    case UiInputKind.TogglePause:
                        if (!_settingsOpen)
                        {
                            _playerPaused = !_playerPaused;
                        }

                        break;
                    case UiInputKind.SetSpeed:
                        if (!_settingsOpen)
                        {
                            if (input.Speed != GameSpeed.X1 && input.Speed != GameSpeed.X2 && input.Speed != GameSpeed.X4)
                            {
                                throw new ArgumentOutOfRangeException(nameof(inputs), "SetSpeed carries a value outside GameSpeed");
                            }

                            _speed = input.Speed;
                        }

                        break;
                    case UiInputKind.PrimaryClick:
                        if (!_settingsOpen)
                        {
                            Click(input.At, 1, camera, screenWidth, screenHeight);
                        }

                        break;
                    case UiInputKind.SecondaryClick:
                        if (!_settingsOpen)
                        {
                            Click(input.At, -1, camera, screenWidth, screenHeight);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        private void Click(ScreenPoint at, int delta, in CameraView camera, float w, float h)
        {
            float x = camera.Centre.X + ((at.X / w) - 0.5f) * camera.ViewHeight * camera.Aspect;
            float y = camera.Centre.Y + ((at.Y / h) - 0.5f) * camera.ViewHeight;

            bool found = false;
            NodeId best = default;
            for (int i = 0; i < _boxes.Length; i++)
            {
                FlowNodeBox b = _boxes[i];
                if (x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && (!found || b.Node.Value > best.Value))
                {
                    found = true;
                    best = b.Node;
                }
            }

            if (found)
            {
                _sink.Request(best, delta);
            }
        }
    }
}
