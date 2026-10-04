using System.Collections.Generic;
using AirportSim.App.Render;
using AirportSim.Sim.Core;

namespace AirportSim.App.Ui
{
    /// <summary>A screen position in pixels, origin bottom-left, +Y up. Spec: 17 §17.3.</summary>
    public readonly struct ScreenPoint
    {
        /// <summary>Pixels from the left edge.</summary>
        public float X { get; }

        /// <summary>Pixels from the bottom edge.</summary>
        public float Y { get; }

        /// <summary>Constructs the point.</summary>
        public ScreenPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>The semantic inputs a backend reports. Spec: 17 §17.3.</summary>
    public enum UiInputKind
    {
        /// <summary>Flips the player's pause.</summary>
        TogglePause,

        /// <summary>Sets the game speed.</summary>
        SetSpeed,

        /// <summary>A primary click in the game view.</summary>
        PrimaryClick,

        /// <summary>A secondary click in the game view.</summary>
        SecondaryClick,

        /// <summary>Opens or closes the settings panel (§17.4a).</summary>
        ToggleSettings,

        /// <summary>Applies a graphics preset (§17.4a).</summary>
        SetGraphicsPreset,

        /// <summary>Applies custom graphics settings (§17.4a).</summary>
        SetGraphicsSettings,
    }

    /// <summary>One input; only the members named for its kind are meaningful. Spec: 17 §17.3.</summary>
    public readonly struct UiInput
    {
        /// <summary>The input kind.</summary>
        public UiInputKind Kind { get; }

        /// <summary>SetSpeed only.</summary>
        public GameSpeed Speed { get; }

        /// <summary>PrimaryClick and SecondaryClick only.</summary>
        public ScreenPoint At { get; }

        /// <summary>SetGraphicsPreset only.</summary>
        public GraphicsPreset Preset { get; }

        /// <summary>SetGraphicsSettings only.</summary>
        public GraphicsSettings Graphics { get; }

        /// <summary>Constructs the input from all its members.</summary>
        public UiInput(UiInputKind kind, GameSpeed speed, ScreenPoint at, GraphicsPreset preset, GraphicsSettings graphics)
        {
            Kind = kind;
            Speed = speed;
            At = at;
            Preset = preset;
            Graphics = graphics;
        }
    }

    /// <summary>What the frame loop hands the pacer. Spec: 17 §17.4.</summary>
    public readonly struct PacingState
    {
        /// <summary>The player's pause OR the settings panel being open.</summary>
        public bool Paused { get; }

        /// <summary>The game speed.</summary>
        public GameSpeed Speed { get; }

        /// <summary>Constructs the state.</summary>
        public PacingState(bool paused, GameSpeed speed)
        {
            Paused = paused;
            Speed = speed;
        }
    }

    /// <summary>What the backend draws from. Spec: 17 §17.7.</summary>
    public readonly struct UiFrame
    {
        /// <summary>What the control strip shows.</summary>
        public PacingState Pacing { get; }

        /// <summary>Whether the settings panel is open.</summary>
        public bool SettingsOpen { get; }

        /// <summary>What the settings panel shows.</summary>
        public GraphicsSettings Graphics { get; }

        /// <summary>Constructs the frame.</summary>
        public UiFrame(PacingState pacing, bool settingsOpen, GraphicsSettings graphics)
        {
            Pacing = pacing;
            SettingsOpen = settingsOpen;
            Graphics = graphics;
        }
    }

    /// <summary>Receives lane requests from the controller. Spec: 17 §17.7.</summary>
    public interface ILaneCommandSink
    {
        /// <summary>Requests one more (+1) or one fewer (-1) open server at a node.</summary>
        void Request(NodeId node, int delta);
    }

    /// <summary>The headless UI controller. Spec: 17 §17.7.</summary>
    public interface IUiController
    {
        /// <summary>Applies the frame's inputs in list order.</summary>
        void Update(IReadOnlyList<UiInput> inputs, in CameraView camera, float screenWidth, float screenHeight);

        /// <summary>The pacing state, settings pause included.</summary>
        PacingState Pacing { get; }

        /// <summary>The current graphics settings.</summary>
        GraphicsSettings Graphics { get; }

        /// <summary>The state the backend draws.</summary>
        UiFrame Frame();
    }
}
