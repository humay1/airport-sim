using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.App.Host.Tests
{
    /// <summary>The sim and preference members the frame loop's parts may reach (15 §15.6, 17 §17.6, 16 §16.6).</summary>
    internal enum Call
    {
        CurrentTick,
        Step,
        TrySubmit,
        Population,
        TryGetLaneState,
        AgentsAt,
        SetPromoted,
        PreferenceRead,
        PreferenceWrite,
    }

    internal readonly struct Entry
    {
        public Entry(Call call, ulong arg, bool flag)
        {
            Call = call;
            Arg = arg;
            Flag = flag;
        }

        public Call Call { get; }

        /// <summary>Step's tick count, or the node of a flow call.</summary>
        public ulong Arg { get; }

        /// <summary>SetPromoted's value.</summary>
        public bool Flag { get; }

        public override string ToString()
        {
            return Call + "(" + Arg.ToString(CultureInfo.InvariantCulture) + (Call == Call.SetPromoted ? (Flag ? ",true" : ",false") : string.Empty) + ")";
        }
    }

    /// <summary>
    /// One ordered record of every call the fakes see, shared by the host, the
    /// flow and the preference store, so a test can read the frame order of
    /// 16 §16.6 off it. Recording reuses preallocated capacity, so it adds no
    /// allocation while Recording is set; members outside 15 §15.6 / 17 §17.6
    /// are violations and throw.
    /// </summary>
    internal sealed class Trace
    {
        public readonly List<Entry> Entries = new List<Entry>(1 << 16);
        public readonly List<string> Violations = new List<string>();
        public bool Recording = true;

        public void Add(Call call, ulong arg = 0UL, bool flag = false)
        {
            if (Recording)
            {
                Entries.Add(new Entry(call, arg, flag));
            }
        }

        public Exception Forbidden(string member)
        {
            Violations.Add(member);
            return new InvalidOperationException("the frame loop reached " + member + ", which 15 §15.6, 17 §17.6 and 16 §16.6 do not list");
        }

        public int Count(Call call)
        {
            int n = 0;
            foreach (Entry e in Entries)
            {
                if (e.Call == call)
                {
                    n++;
                }
            }

            return n;
        }

        public int First(Call call)
        {
            return Entries.FindIndex(e => e.Call == call);
        }

        public int Last(Call call)
        {
            return Entries.FindLastIndex(e => e.Call == call);
        }

        public List<(ulong Node, bool Promoted)> Promotions()
        {
            var list = new List<(ulong Node, bool Promoted)>();
            foreach (Entry e in Entries)
            {
                if (e.Call == Call.SetPromoted)
                {
                    list.Add((e.Arg, e.Flag));
                }
            }

            return list;
        }

        public List<ulong> Steps()
        {
            var list = new List<ulong>();
            foreach (Entry e in Entries)
            {
                if (e.Call == Call.Step)
                {
                    list.Add(e.Arg);
                }
            }

            return list;
        }

        public string Show()
        {
            return string.Join(" ", Entries.ConvertAll(e => e.ToString()));
        }

        public void Clear()
        {
            Entries.Clear();
        }
    }

    /// <summary>
    /// An ISimHost that records CurrentTick, Step and TrySubmit (15 §15.6,
    /// 17 §17.6, 16 §16.6). Step advances the tick and runs nothing. TrySubmit
    /// admits every command.
    /// </summary>
    internal sealed class TraceHost : ISimHost
    {
        public readonly List<Command> Submitted = new List<Command>();
        public ulong Tick;
        public long StepCalls;

        private readonly Trace _trace;

        public TraceHost(Trace trace, ulong tick = 0UL)
        {
            _trace = trace;
            Tick = tick;
        }

        public ulong CurrentTick
        {
            get
            {
                _trace.Add(Call.CurrentTick, Tick);
                return Tick;
            }
        }

        public void Step(uint ticks)
        {
            _trace.Add(Call.Step, ticks);
            StepCalls++;
            Tick += ticks;
        }

        public ulong WorldStateHash()
        {
            throw _trace.Forbidden("ISimHost.WorldStateHash");
        }

        public bool TrySubmit(in Command cmd, out CommandRejection reason)
        {
            _trace.Add(Call.TrySubmit, cmd.Tick);
            Submitted.Add(cmd);
            reason = CommandRejection.None;
            return true;
        }

        public IReadOnlyList<Command> CommandLogSince(ulong tick)
        {
            throw _trace.Forbidden("ISimHost.CommandLogSince");
        }
    }

    /// <summary>
    /// An IFlowSystem over the nodes of tests/fixtures/render/phase1-layout.json
    /// (1 to 9). Nodes 5 and 6 are lanes with 3 servers, 1 open, as in the
    /// Phase 1 flow fixture. Its queries allocate nothing.
    /// </summary>
    internal sealed class TraceFlow : IFlowSystem
    {
        public const uint LaneNode = 5;

        private static readonly AgentView[] NoAgents = new AgentView[0];
        private readonly Trace _trace;
        private readonly bool[] _promoted = new bool[10];

        public TraceFlow(Trace trace)
        {
            _trace = trace;
        }

        public SystemId Id => throw _trace.Forbidden("IFlowSystem.Id");

        public string Name => throw _trace.Forbidden("IFlowSystem.Name");

        public bool IsPromoted(uint node)
        {
            return _promoted[node];
        }

        private bool Known(NodeId node)
        {
            return node.Value >= 1 && node.Value <= 9;
        }

        public void Tick(in TickContext ctx)
        {
            throw _trace.Forbidden("IFlowSystem.Tick");
        }

        public ulong ComputeStateHash()
        {
            throw _trace.Forbidden("IFlowSystem.ComputeStateHash");
        }

        public int Population(NodeId node)
        {
            _trace.Add(Call.Population, node.Value);
            return 0;
        }

        public Fx PredictedWaitMinutes(NodeId node)
        {
            throw _trace.Forbidden("IFlowSystem.PredictedWaitMinutes");
        }

        public int PopulationForFlight(FlightId flight, FlowDirection direction)
        {
            throw _trace.Forbidden("IFlowSystem.PopulationForFlight");
        }

        public IReadOnlyList<CohortId> CohortsAt(NodeId node)
        {
            throw _trace.Forbidden("IFlowSystem.CohortsAt");
        }

        public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
        {
            throw _trace.Forbidden("IFlowSystem.TryGetCohort");
        }

        public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
        {
            throw _trace.Forbidden("IFlowSystem.TryGetOutstanding");
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            _trace.Add(Call.TryGetLaneState, node.Value);
            if (node.Value == 5 || node.Value == 6)
            {
                lanes = new LaneState(3, 1);
                return true;
            }

            lanes = default;
            return false;
        }

        public NodeKind KindOf(NodeId node)
        {
            throw _trace.Forbidden("IFlowSystem.KindOf");
        }

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            throw _trace.Forbidden("IFlowSystem.Inject");
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            throw _trace.Forbidden("IFlowSystem.Absorb");
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
            if (!Known(node))
            {
                throw new ArgumentException("unknown node " + node.Value, nameof(node));
            }

            _trace.Add(Call.SetPromoted, node.Value, promoted);
            _promoted[node.Value] = promoted;
        }

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            if (!Known(node))
            {
                throw new ArgumentException("unknown node " + node.Value, nameof(node));
            }

            _trace.Add(Call.AgentsAt, node.Value);
            return NoAgents;
        }
    }

    /// <summary>An IPreferenceStore over a dictionary (16 §16.6), recording every read key and write.</summary>
    internal sealed class FakePreferences : IPreferenceStore
    {
        public readonly List<string> ReadKeys = new List<string>();
        public readonly List<(string Key, string Value)> Writes = new List<(string Key, string Value)>();

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Trace? _trace;

        public FakePreferences(Trace? trace = null)
        {
            _trace = trace;
        }

        public FakePreferences With(string key, string value)
        {
            _values[key] = value;
            return this;
        }

        public bool TryRead(string key, out string value)
        {
            _trace?.Add(Call.PreferenceRead);
            ReadKeys.Add(key);
            if (key != null && _values.TryGetValue(key, out string? v))
            {
                value = v;
                return true;
            }

            value = null!;
            return false;
        }

        public void Write(string key, string value)
        {
            _trace?.Add(Call.PreferenceWrite);
            Writes.Add((key, value));
            _values[key] = value;
        }
    }

    /// <summary>UI inputs of 17 §17.3. Fields a kind does not use are left at legal values.</summary>
    internal static class In
    {
        public static readonly UiInput[] None = new UiInput[0];

        public static UiInput TogglePause()
        {
            return new UiInput(UiInputKind.TogglePause, GameSpeed.X1, default, GraphicsPreset.Medium, default);
        }

        public static UiInput ToggleSettings()
        {
            return new UiInput(UiInputKind.ToggleSettings, GameSpeed.X1, default, GraphicsPreset.Medium, default);
        }

        public static UiInput Preset(GraphicsPreset preset)
        {
            return new UiInput(UiInputKind.SetGraphicsPreset, GameSpeed.X1, default, preset, default);
        }

        public static UiInput Settings(in GraphicsSettings g)
        {
            return new UiInput(UiInputKind.SetGraphicsSettings, GameSpeed.X1, default, GraphicsPreset.Medium, g);
        }

        /// <summary>A primary click at a world point, through the Rig camera's exact mapping (17 §17.3).</summary>
        public static UiInput ClickWorld(float worldX, float worldY)
        {
            return new UiInput(UiInputKind.PrimaryClick, GameSpeed.X1, new ScreenPoint(worldX - Rig.ViewMinX, worldY - Rig.ViewMinY), GraphicsPreset.Medium, default);
        }
    }

    internal static class Gfx
    {
        public static GraphicsSettings Of(GraphicsPreset preset)
        {
            return RenderFactory.GraphicsForPreset(preset);
        }

        public static string Show(in GraphicsSettings g)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} agents={1} max={2} fps={3} res={4} aa={5}",
                g.Preset,
                g.DrawAgents,
                g.MaxDrawnAgentsPerNode,
                g.FrameRateCap,
                g.ResolutionScalePercent,
                g.AntiAliasing);
        }

        public static bool Same(in GraphicsSettings a, in GraphicsSettings b)
        {
            return a.Preset == b.Preset && a.DrawAgents == b.DrawAgents && a.MaxDrawnAgentsPerNode == b.MaxDrawnAgentsPerNode
                && a.FrameRateCap == b.FrameRateCap && a.ResolutionScalePercent == b.ResolutionScalePercent && a.AntiAliasing == b.AntiAliasing;
        }

        public static void AssertSame(in GraphicsSettings expected, in GraphicsSettings actual, string what)
        {
            Assert.True(Same(expected, actual), what + ": expected " + Show(expected) + ", got " + Show(actual));
        }
    }

    /// <summary>
    /// A presentation composed by IPresentationComposer over fakes: a ComposedSim
    /// with the TraceHost and the TraceFlow and no airside (15 §15.5 "Absent
    /// modules"), the render layout fixture as render_layout.fixture, and a fake
    /// preference store. The camera sees every flow-node box of the layout
    /// (y 200..260, x 0..880) and is zoomed in below AGENT_ZOOM_THRESHOLD.
    ///
    /// Floats (07 L4 has no exception for tests/app/host yet; see the PR):
    /// only for the float-typed members of 15 and 17, every value dyadic. The
    /// screen is 1024 × 64 pixels over a 1024 × 64 world view, so screen to
    /// world (17 §17.3) is world = screen + (0, 224), exact.
    /// </summary>
    internal sealed class Rig
    {
        public const float ViewMinX = 0f;
        public const float ViewMinY = 224f;
        public const float ScreenWidth = 1024f;
        public const float ScreenHeight = 64f;

        /// <summary>One tick of real time at 1x (15 §15.2).</summary>
        public const long OneTick = RenderConstants.REAL_MICROSECONDS_PER_TICK_1X;

        public static readonly CameraView Camera = new CameraView(new WorldPoint(512f, 256f), 64f, 16f);

        public readonly Trace Trace = new Trace();
        public readonly TraceHost Host;
        public readonly TraceFlow Flow;
        public readonly FakePreferences Preferences;
        public readonly MemoryBundle Bundle;
        public readonly Presentation Presentation;

        public Rig(Func<Trace, FakePreferences>? preferences = null)
        {
            Assert.True(64f <= (float)RenderConstants.AGENT_ZOOM_THRESHOLD, "the rig camera must be zoomed in (15 §15.7)");
            Host = new TraceHost(Trace);
            Flow = new TraceFlow(Trace);
            Preferences = preferences == null ? new FakePreferences(Trace) : preferences(Trace);
            Bundle = new MemoryBundle().Put("render_layout.fixture", Repo.Read(Bundles.RenderLayout));
            var sim = new ComposedSim(Host, null, null, null, Flow, null, null);
            Presentation = HostFactory.CreatePresentationComposer().Compose(sim, Bundle, Preferences);
        }

        public static FrameInput Input(long elapsed, UiInput[] inputs)
        {
            return new FrameInput(Camera, ScreenWidth, ScreenHeight, inputs, elapsed);
        }

        public FrameOutput Frame(long elapsed, params UiInput[] inputs)
        {
            FrameInput input = Input(elapsed, inputs);
            return Presentation.Frame.RunFrame(input);
        }
    }
}
