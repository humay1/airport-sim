using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render;
using UnityEngine;

namespace AirportSim.App.Ui.UnityBackend
{
    /// <summary>
    /// Draws the control strip and the settings panel from a <see cref="UiFrame"/> and collects the
    /// inputs the player makes, in arrival order (spec 17 section 17.8). It reads no sim state.
    /// </summary>
    public sealed class UiBackend : MonoBehaviour
    {
        private const float Cell = 40f;
        private const float Gap = 6f;
        private const float PanelWidth = 360f;
        private const float Row = 32f;

        private static readonly GameSpeed[] Speeds = { GameSpeed.X1, GameSpeed.X2, GameSpeed.X4 };
        private static readonly int[] FrameRateCaps = { 0, 30, 60, 120, 240 };
        private static readonly Color Lit = new Color(0.45f, 0.8f, 1f, 1f);

        private readonly List<UiInput> inputs = new List<UiInput>();
        private UiFrame frame;

        /// <summary>This frame's inputs, in arrival order. The bootstrap puts them in FrameInput.Ui.</summary>
        public IReadOnlyList<UiInput> Inputs => inputs;

        /// <summary>Sets the state the strip and panel draw. Called by the bootstrap with the frame loop's UiFrame.</summary>
        public void Show(in UiFrame uiFrame)
        {
            frame = uiFrame;
        }

        /// <summary>Forgets the collected inputs. Called by the bootstrap after it has passed them on.</summary>
        public void ClearInputs()
        {
            inputs.Clear();
        }

        private void OnGUI()
        {
            Event e = Event.current;
            bool open = frame.SettingsOpen;
            Rect strip = new Rect(Gap, Gap, 5 * Cell + 6 * Gap, Cell + 2 * Gap);
            Rect panel = new Rect(Gap, strip.yMax + Gap, PanelWidth, 7 * Row + 2 * Gap);

            if (e.type == EventType.MouseDown
                && !strip.Contains(e.mousePosition)
                && !(open && panel.Contains(e.mousePosition)))
            {
                // Anywhere else in the game view: a world click, at its bottom-left-origin pixel position.
                ScreenPoint at = new ScreenPoint(e.mousePosition.x, Screen.height - e.mousePosition.y);
                if (e.button == 0)
                {
                    Add(UiInputKind.PrimaryClick, at: at);
                }
                else if (e.button == 1)
                {
                    Add(UiInputKind.SecondaryClick, at: at);
                }
            }

            if (e.type == EventType.KeyDown)
            {
                Shortcut(e.keyCode, open);
            }

            GUI.Box(strip, GUIContent.none);
            DrawStrip(strip, open);
            if (open)
            {
                GUI.Box(panel, GUIContent.none);
                DrawPanel(panel);
            }
        }

        private void Shortcut(KeyCode key, bool open)
        {
            if (key == KeyCode.Escape)
            {
                Add(UiInputKind.ToggleSettings);
            }
            else if (open)
            {
                return;
            }
            else if (key == KeyCode.Space)
            {
                Add(UiInputKind.TogglePause);
            }
            else if (key == KeyCode.Alpha1)
            {
                Add(UiInputKind.SetSpeed, GameSpeed.X1);
            }
            else if (key == KeyCode.Alpha2)
            {
                Add(UiInputKind.SetSpeed, GameSpeed.X2);
            }
            else if (key == KeyCode.Alpha4)
            {
                Add(UiInputKind.SetSpeed, GameSpeed.X4);
            }
        }

        private void DrawStrip(Rect strip, bool open)
        {
            // Every control but the settings icon is inert while the panel is open.
            GUI.enabled = !open;
            Rect cell = new Rect(strip.x + Gap, strip.y + Gap, Cell, Cell);
            if (Press(cell, frame.Pacing.Paused))
            {
                Add(UiInputKind.TogglePause);
            }

            Bar(cell, 0.30f, 0.25f, 0.15f, 0.5f);
            Bar(cell, 0.55f, 0.25f, 0.15f, 0.5f);
            for (int i = 0; i < Speeds.Length; i++)
            {
                cell.x += Cell + Gap;
                if (Press(cell, frame.Pacing.Speed == Speeds[i]))
                {
                    Add(UiInputKind.SetSpeed, Speeds[i]);
                }

                // The speed's multiple, as that many squares: no text.
                int n = (int)Speeds[i];
                float x0 = 0.5f - (n * 0.2f - 0.04f) / 2f;
                for (int k = 0; k < n; k++)
                {
                    Bar(cell, x0 + k * 0.2f, 0.42f, 0.16f, 0.16f);
                }
            }

            GUI.enabled = true;
            cell.x += Cell + Gap;
            if (Press(cell, open))
            {
                Add(UiInputKind.ToggleSettings);
            }

            Bar(cell, 0.25f, 0.25f, 0.5f, 0.1f);
            Bar(cell, 0.25f, 0.45f, 0.5f, 0.1f);
            Bar(cell, 0.25f, 0.65f, 0.5f, 0.1f);
        }

        private void DrawPanel(Rect panel)
        {
            GraphicsSettings g = frame.Graphics;
            Rect row = new Rect(panel.x + Gap, panel.y + Gap, panel.width - 2 * Gap, Row - Gap);
            GUI.Label(row, UiTextKeys.SettingsTitle);

            // One control per preset.
            row.y += Row;
            Rect presetCell = new Rect(row.x, row.y, (row.width - 2 * Gap) / 3f, row.height);
            PresetButton(presetCell, GraphicsPreset.Low, UiTextKeys.PresetLow);
            presetCell.x += presetCell.width + Gap;
            PresetButton(presetCell, GraphicsPreset.Medium, UiTextKeys.PresetMedium);
            presetCell.x += presetCell.width + Gap;
            PresetButton(presetCell, GraphicsPreset.High, UiTextKeys.PresetHigh);

            // One control per knob, each reporting the current settings with that one knob changed.
            row.y += Row;
            if (Toggle(row, UiTextKeys.DrawAgents, g.DrawAgents))
            {
                Set(new GraphicsSettings(GraphicsPreset.Custom, !g.DrawAgents, g.MaxDrawnAgentsPerNode, g.FrameRateCap, g.ResolutionScalePercent, g.AntiAliasing));
            }

            row.y += Row;
            int step = Stepper(row, UiTextKeys.MaxDrawnAgentsPerNode, g.MaxDrawnAgentsPerNode);
            if (step != 0)
            {
                Set(new GraphicsSettings(GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode + step * 16, g.FrameRateCap, g.ResolutionScalePercent, g.AntiAliasing));
            }

            row.y += Row;
            if (Press(new Rect(row.x, row.y, row.width, row.height), false))
            {
                Set(new GraphicsSettings(GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode, NextFrameRateCap(g.FrameRateCap), g.ResolutionScalePercent, g.AntiAliasing));
            }

            GUI.Label(row, UiTextKeys.FrameRateCap + "  " + g.FrameRateCap.ToString(CultureInfo.InvariantCulture));

            row.y += Row;
            step = Stepper(row, UiTextKeys.ResolutionScalePercent, g.ResolutionScalePercent);
            if (step != 0)
            {
                Set(new GraphicsSettings(GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode, g.FrameRateCap, g.ResolutionScalePercent + step * 10, g.AntiAliasing));
            }

            row.y += Row;
            if (Toggle(row, UiTextKeys.AntiAliasing, g.AntiAliasing))
            {
                Set(new GraphicsSettings(GraphicsPreset.Custom, g.DrawAgents, g.MaxDrawnAgentsPerNode, g.FrameRateCap, g.ResolutionScalePercent, !g.AntiAliasing));
            }
        }

        private void PresetButton(Rect r, GraphicsPreset preset, string key)
        {
            if (Press(r, frame.Graphics.Preset == preset))
            {
                Add(UiInputKind.SetGraphicsPreset, preset: preset);
            }

            GUI.Label(r, key);
        }

        private static bool Toggle(Rect r, string key, bool on)
        {
            bool hit = Press(r, on);
            GUI.Label(r, key);
            return hit;
        }

        // Draws "key  value  [-] [+]" and returns -1, +1 or 0 for the button pressed.
        private static int Stepper(Rect r, string key, int value)
        {
            GUI.Label(r, key + "  " + value.ToString(CultureInfo.InvariantCulture));
            Rect minus = new Rect(r.xMax - 2 * r.height - Gap, r.y, r.height, r.height);
            Rect plus = new Rect(r.xMax - r.height, r.y, r.height, r.height);
            int result = 0;
            if (Press(minus, false))
            {
                result = -1;
            }

            Bar(minus, 0.25f, 0.45f, 0.5f, 0.1f);
            if (Press(plus, false))
            {
                result = 1;
            }

            Bar(plus, 0.25f, 0.45f, 0.5f, 0.1f);
            Bar(plus, 0.45f, 0.25f, 0.1f, 0.5f);
            return result;
        }

        private static int NextFrameRateCap(int current)
        {
            for (int i = 0; i < FrameRateCaps.Length; i++)
            {
                if (FrameRateCaps[i] == current)
                {
                    return FrameRateCaps[(i + 1) % FrameRateCaps.Length];
                }
            }

            return FrameRateCaps[0];
        }

        private static bool Press(Rect r, bool lit)
        {
            Color saved = GUI.backgroundColor;
            if (lit)
            {
                GUI.backgroundColor = Lit;
            }

            bool hit = GUI.Button(r, GUIContent.none);
            GUI.backgroundColor = saved;
            return hit;
        }

        // A filled rectangle given as fractions of a cell: the icons are drawn from these.
        private static void Bar(Rect cell, float x, float y, float w, float h)
        {
            Color saved = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cell.x + x * cell.width, cell.y + y * cell.height, w * cell.width, h * cell.height), Texture2D.whiteTexture);
            GUI.color = saved;
        }

        private void Set(GraphicsSettings graphics)
        {
            Add(UiInputKind.SetGraphicsSettings, graphics: graphics);
        }

        // Members a kind does not use are given a valid value, so every input is well formed.
        private void Add(
            UiInputKind kind,
            GameSpeed speed = GameSpeed.X1,
            ScreenPoint at = default,
            GraphicsPreset preset = GraphicsPreset.Medium,
            GraphicsSettings graphics = default)
        {
            inputs.Add(new UiInput(kind, speed, at, preset, graphics));
        }
    }
}
