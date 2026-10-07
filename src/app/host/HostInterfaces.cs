using System.Collections.Generic;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Delay;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.Turnaround;
using AirportSim.Sim.World;

namespace AirportSim.App.Host
{
    /// <summary>A scenario bundle: a set of named files, read by exact name only. Spec: 16 §16.3.</summary>
    public interface IScenarioBundle
    {
        /// <summary>Whether the bundle holds a file of exactly this name.</summary>
        bool Has(string fileName);

        /// <summary>The file's bytes. Load time only; allocation is fine here.</summary>
        byte[] ReadAll(string fileName);
    }

    /// <summary>A composed sim: the host and the registered systems, null for one not registered. Spec: 16 §16.4.</summary>
    public readonly struct ComposedSim
    {
        /// <summary>The built host.</summary>
        public ISimHost Host { get; }

        /// <summary>sim.world, or null when not registered.</summary>
        public IWorldSystem? World { get; }

        /// <summary>sim.schedule, or null when not registered.</summary>
        public IScheduleSystem? Schedule { get; }

        /// <summary>sim.airside, or null when not registered.</summary>
        public IAirsideSystem? Airside { get; }

        /// <summary>sim.flow, or null when not registered.</summary>
        public IFlowSystem? Flow { get; }

        /// <summary>sim.turnaround, or null when not registered.</summary>
        public ITurnaroundSystem? Turnaround { get; }

        /// <summary>sim.delay, or null when not registered.</summary>
        public IDelaySystem? Delay { get; }

        /// <summary>The content index step 1 built (Q-130); null only from the seven-argument constructor.</summary>
        public IContentIndex? Content { get; }

        /// <summary>Constructs the value as given, with no content index (kept constructor, 07 L10).</summary>
        public ComposedSim(
            ISimHost host,
            IWorldSystem? world,
            IScheduleSystem? schedule,
            IAirsideSystem? airside,
            IFlowSystem? flow,
            ITurnaroundSystem? turnaround,
            IDelaySystem? delay)
            : this(host, world, schedule, airside, flow, turnaround, delay, null)
        {
        }

        /// <summary>Constructs the value as given.</summary>
        public ComposedSim(
            ISimHost host,
            IWorldSystem? world,
            IScheduleSystem? schedule,
            IAirsideSystem? airside,
            IFlowSystem? flow,
            ITurnaroundSystem? turnaround,
            IDelaySystem? delay,
            IContentIndex? content)
        {
            Content = content;
            Host = host;
            World = world;
            Schedule = schedule;
            Airside = airside;
            Flow = flow;
            Turnaround = turnaround;
            Delay = delay;
        }
    }

    /// <summary>Builds the sim from a bundle. Spec: 16 §16.4.</summary>
    public interface ISimComposer
    {
        /// <summary>Composes the bundle once; every checkpoint goes to <paramref name="checkpoints"/>. A load failure throws <see cref="System.FormatException"/>.</summary>
        ComposedSim Compose(IScenarioBundle bundle, ICheckpointSink checkpoints);
    }

    /// <summary>The scene-layer objects built over a composed sim. Spec: 16 §16.5.</summary>
    public readonly struct Presentation
    {
        /// <summary>The scene builder.</summary>
        public ISceneBuilder Scene { get; }

        /// <summary>The promotion controller.</summary>
        public IPromotionController Promotion { get; }

        /// <summary>The tick pacer.</summary>
        public ITickPacer Pacer { get; }

        /// <summary>The UI controller.</summary>
        public IUiController Ui { get; }

        /// <summary>The frame loop over the parts above.</summary>
        public IFrameLoop Frame { get; }

        /// <summary>Constructs the value as given.</summary>
        public Presentation(ISceneBuilder scene, IPromotionController promotion, ITickPacer pacer, IUiController ui, IFrameLoop frame)
        {
            Scene = scene;
            Promotion = promotion;
            Pacer = pacer;
            Ui = ui;
            Frame = frame;
        }
    }

    /// <summary>Builds the presentation scene-layer objects. Spec: 16 §16.5.</summary>
    public interface IPresentationComposer
    {
        /// <summary>Loads render_layout.fixture (a failure throws <see cref="System.FormatException"/>) and builds the parts and the frame loop.</summary>
        Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences);

        /// <summary>As the three-argument overload, with the scene built over <paramref name="looks"/> (Q-130).</summary>
        Presentation Compose(in ComposedSim sim, IScenarioBundle bundle, IPreferenceStore preferences, in RenderLooks looks);
    }

    /// <summary>The player's preferences, over the engine's. Spec: 16 §16.6 (D10).</summary>
    public interface IPreferenceStore
    {
        /// <summary>Reads the string stored under <paramref name="key"/>; false when there is none.</summary>
        bool TryRead(string key, out string value);

        /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>.</summary>
        void Write(string key, string value);
    }

    /// <summary>One frame's input from the bootstrap. Spec: 16 §16.6.</summary>
    public readonly struct FrameInput
    {
        /// <summary>The render backend's camera.</summary>
        public CameraView Camera { get; }

        /// <summary>Screen width in pixels, greater than 0.</summary>
        public float ScreenWidth { get; }

        /// <summary>Screen height in pixels, greater than 0.</summary>
        public float ScreenHeight { get; }

        /// <summary>The UI backend's inputs, in arrival order.</summary>
        public IReadOnlyList<UiInput> Ui { get; }

        /// <summary>The engine frame delta in microseconds.</summary>
        public long ElapsedRealMicroseconds { get; }

        /// <summary>Constructs the value as given.</summary>
        public FrameInput(CameraView camera, float screenWidth, float screenHeight, IReadOnlyList<UiInput> ui, long elapsedRealMicroseconds)
        {
            Camera = camera;
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            Ui = ui;
            ElapsedRealMicroseconds = elapsedRealMicroseconds;
        }
    }

    /// <summary>One frame's output for the backends. Spec: 16 §16.6.</summary>
    public readonly struct FrameOutput
    {
        /// <summary>The frame to draw; valid until the next RunFrame.</summary>
        public RenderFrame Render { get; }

        /// <summary>The UI frame to draw.</summary>
        public UiFrame Ui { get; }

        /// <summary>Constructs the value as given.</summary>
        public FrameOutput(RenderFrame render, UiFrame ui)
        {
            Render = render;
            Ui = ui;
        }
    }

    /// <summary>The frame loop, the only caller of <c>ISimHost.Step</c> in a playable build. Spec: 16 §16.6.</summary>
    public interface IFrameLoop
    {
        /// <summary>Runs one frame in the order of 16 §16.6; allocates nothing after the first call.</summary>
        FrameOutput RunFrame(in FrameInput input);
    }

    /// <summary>A parsed checkpoint run. Spec: 16 §16.8.</summary>
    public readonly struct CheckpointRunRequest
    {
        /// <summary>Sim days to run, 1 to 298 261.</summary>
        public uint Days { get; }

        /// <summary>Where the dump is written.</summary>
        public string OutputPath { get; }

        /// <summary>Constructs the value as given.</summary>
        public CheckpointRunRequest(uint days, string outputPath)
        {
            Days = days;
            OutputPath = outputPath;
        }
    }

    /// <summary>Recognises the checkpoint run among the process arguments. Spec: 16 §16.8.</summary>
    public interface IHostCommandLine
    {
        /// <summary>True exactly for <c>-airportsim-checkpoints &lt;days&gt; &lt;outputPath&gt;</c>, other arguments ignored; else false with a default request. Throws nothing.</summary>
        bool TryParse(IReadOnlyList<string> args, out CheckpointRunRequest request);
    }

    /// <summary>The headless checkpoint run. Spec: 16 §16.8.</summary>
    public interface IHeadlessRun
    {
        /// <summary>Composes the bundle, steps it <c>Days</c> sim days with no presentation and writes the dump. Returns 0 on success, 1 on a load or write failure.</summary>
        int Run(IScenarioBundle bundle, in CheckpointRunRequest request);
    }
}
