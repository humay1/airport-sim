using System.Collections.Generic;
using System.Globalization;
using AirportSim.App.Render;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class RecordingCheckpointSink : ICheckpointSink
    {
        public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

        public void Record(in Checkpoint cp)
        {
            Recorded.Add(cp);
        }

        public List<string> Describe()
        {
            var result = new List<string>();
            foreach (Checkpoint cp in Recorded)
            {
                var parts = new List<string>();
                foreach (ulong h in cp.SystemHashes)
                {
                    parts.Add(h.ToString("X16", CultureInfo.InvariantCulture));
                }

                result.Add(string.Format(CultureInfo.InvariantCulture, "t={0} world={1:X16} core={2:X16} [{3}]", cp.Tick, cp.WorldHash, cp.CoreHash, string.Join(",", parts)));
            }

            return result;
        }
    }

    /// <summary>
    /// Test-owned content (08 §8.11a lets tests build definitions directly):
    /// the aircraft types of tests/fixtures/schedule/phase0-200.csv, the size
    /// categories the airside fixture's stands name, the two pax profiles,
    /// and the queue profile of the flow fixture's queue nodes. Fixture
    /// sizing, not balance.
    /// </summary>
    internal static class Phase1Content
    {
        private static readonly (string Id, int Ordinal)[] Sizes =
        {
            ("small", 1), ("medium", 2), ("heavy", 3), ("super", 4),
        };

        private static readonly (string Id, string Size)[] Aircraft =
        {
            ("a320", "medium"), ("a321", "medium"), ("a359", "heavy"), ("a388", "super"), ("atr72", "small"),
            ("b738", "medium"), ("b744", "heavy"), ("b789", "heavy"), ("crj900", "small"),
        };

        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        public static IContentIndex Index()
        {
            var defs = new List<IContentDefinition>();
            foreach ((string id, int ordinal) in Sizes)
            {
                defs.Add(new SizeCategoryDefinition(new ContentId(id), ordinal));
            }

            foreach ((string id, string size) in Aircraft)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId(size)));
            }

            defs.Add(Profile("business", Business));
            defs.Add(Profile("leisure", Leisure));
            defs.Add(new QueueProfileDefinition(new ContentId("security_standard"), Fx.FromInt(15), 1000, Fx.FromInt(10), Fx.FromInt(3), DelayCategory.SecurityQueue));
            return ContentIndexFactory.Create(defs);
        }

        private static PaxProfileDefinition Profile(string id, (uint Minutes, uint Share)[] curve)
        {
            var buckets = new List<ShowUpBucket>();
            foreach ((uint m, uint s) in curve)
            {
                buckets.Add(new ShowUpBucket(m, s));
            }

            return new PaxProfileDefinition(new ContentId(id), Fx.FromRatio(13, 10), buckets);
        }
    }

    /// <summary>
    /// The real Phase 1 sim the render layout fixture positions: sim.world,
    /// sim.flow, sim.schedule and sim.airside over the Phase 0/1 fixtures,
    /// with the real sim.schedule and sim.flow that 17 §17.10 names.
    /// </summary>
    internal sealed class Phase1Sim
    {
        public const string WorldFile = "phase0-landside.json";
        public const string FlowFile = "phase0-landside.flow.json";
        public const string ScheduleFile = "phase0-200.csv";
        public const string AirsideFile = "phase1-single-runway.json";
        public const uint FlowSink = 9;
        public const ulong Seed = 0x5EED_0029UL;

        public readonly ISimHost Host;
        public readonly IAirsideSystem Airside;
        public readonly IFlowSystem Flow;
        public readonly RecordingCheckpointSink Checkpoints = new RecordingCheckpointSink();

        /// <summary>16 §16.4's construction order (world; flow; schedule; airside), registered in 08 §8.5's order.</summary>
        public Phase1Sim()
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(Seed, Phase1Content.Index(), Checkpoints, new NullLog()));
            WalkGraph walk = WorldFactory.CreateGraphLoader().Load(Repo.Read("tests", "fixtures", "world", WorldFile), WorldFile);
            IWorldSystem world = WorldFactory.CreateSystem(b.Services, walk);
            FlowGraph graph = FlowFactory.CreateGraphLoader().Load(Repo.Read("tests", "fixtures", "flow", FlowFile), FlowFile, world);
            Flow = FlowFactory.CreateSystem(b.Services, graph, world);
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(Repo.Read("tests", "fixtures", "schedule", ScheduleFile), ScheduleFile);
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(b.Services, table, Flow);
            Airside = AirsideFactory.CreateSystem(b.Services, AirsideLayout(), new AirsideRules(10U, 2U), schedule, Flow, false);
            b.Register(world);
            b.Register(schedule);
            b.Register(Airside);
            b.Register(Flow);
            Host = b.Build();
        }

        /// <summary>
        /// tests/fixtures/airside/phase1-single-runway.json, with every
        /// stand's departure_sink_node moved to the flow fixture's one Sink.
        /// The airside fixture names sinks 901-904, which the flow fixture
        /// does not have, so a departure's Absorb would throw (09 §9.7). The
        /// taxi graph is the fixture's unchanged.
        /// </summary>
        public static AirsideLayout AirsideLayout()
        {
            IAirsideLayoutLoader loader = AirsideFactory.CreateLayoutLoader();
            AirsideLayout parsed = loader.Parse(Repo.Read("tests", "fixtures", "airside", AirsideFile), AirsideFile);
            var stands = new List<StandDef>();
            foreach (StandDef s in parsed.Stands)
            {
                stands.Add(new StandDef(s.Id, s.Node, s.MaxAircraftSizeCategory, new NodeId(FlowSink)));
            }

            return loader.Load(new AirsideLayout(parsed.Runways, parsed.Nodes, parsed.Edges, stands));
        }

        /// <summary>The same sim stepped headless, one Step(1) per tick, to the given tick.</summary>
        public static (List<string> Checkpoints, ulong Hash) Headless(ulong ticks)
        {
            var sim = new Phase1Sim();
            for (ulong t = 0; t < ticks; t++)
            {
                sim.Host.Step(1);
            }

            return (sim.Checkpoints.Describe(), sim.Host.WorldStateHash());
        }
    }

    /// <summary>
    /// 16 §16.6's frame loop, written out by the test because app.host does
    /// not exist yet: the real IUiController with the production lane sink
    /// (over guarded host and flow), the real promotion controller, pacer
    /// and scene builder, in the binding order Ui.Update; Promotion.Update;
    /// Pacer.Advance and Step; Scene.Build; preference write; Ui.Frame.
    /// </summary>
    internal sealed class FrameLoop
    {
        public readonly Phase1Sim Sim = new Phase1Sim();
        public readonly CallGuard Guard = new CallGuard();
        public readonly GuardedHost UiHost;
        public readonly IUiController Ui;
        public readonly RenderLayout Layout = Layouts.Fixture();
        public long Frames;
        public long PausedFrames;
        public long PreferenceWrites;
        public string LastWritten;

        private readonly IPromotionController _promotion;
        private readonly ISceneBuilder _scene;
        private readonly ITickPacer _pacer = RenderFactory.CreatePacer();
        private GraphicsSettings _lastWritten;

        public FrameLoop(in GraphicsSettings initialGraphics)
        {
            UiHost = new GuardedHost(Sim.Host, Guard);
            ILaneCommandSink sink = UiFactory.CreateLaneCommandSink(UiHost, new GuardedFlow(Sim.Flow, Guard));
            Ui = UiFactory.CreateController(Layout, sink, initialGraphics);
            var sources = new RenderSources(Sim.Host, Sim.Airside, Sim.Flow);
            _promotion = RenderFactory.CreatePromotionController(sources, Layout);
            _scene = RenderFactory.CreateSceneBuilder(sources, Layout);
            _lastWritten = Ui.Graphics;
            LastWritten = UiFactory.EncodeGraphicsPreference(_lastWritten);
        }

        /// <summary>One RunFrame; returns the ticks stepped and the frame's UiFrame.</summary>
        public (uint Stepped, UiFrame Frame) Run(IReadOnlyList<UiInput> inputs, in CameraView camera, long elapsedRealMicroseconds)
        {
            Ui.Update(inputs, camera, Screen.W, Screen.H);
            _promotion.Update(camera, Ui.Graphics);
            PacingState pacing = Ui.Pacing;
            uint n = _pacer.Advance(elapsedRealMicroseconds, pacing.Paused, pacing.Speed);
            if (pacing.Paused)
            {
                PausedFrames++;
                Assert.True(n == 0, "frame " + Frames + ": the pacer stepped " + n + " ticks while Ui.Pacing.Paused");
            }

            if (n > 0)
            {
                Sim.Host.Step(n);
            }

            RenderFrame render = _scene.Build(camera, Ui.Graphics);
            Assert.True(render.Tick == Sim.Host.CurrentTick, "frame " + Frames + ": RenderFrame.Tick " + render.Tick + " but the host is at " + Sim.Host.CurrentTick);
            GraphicsSettings g = Ui.Graphics;
            if (Gfx.Show(g) != Gfx.Show(_lastWritten))
            {
                _lastWritten = g;
                LastWritten = UiFactory.EncodeGraphicsPreference(g);
                PreferenceWrites++;
            }

            UiFrame frame = Ui.Frame();
            Frames++;
            return (n, frame);
        }

        /// <summary>
        /// The run is outcome-neutral: no member outside 17 §17.6 was called,
        /// and the checkpoints and final hash equal the same sim stepped
        /// headless to the same tick (17 §17.4, 15 §15.8).
        /// </summary>
        public void AssertNeutral(string what)
        {
            Assert.True(Guard.Violations.Count == 0, what + ": members outside 17 §17.6 were called: " + string.Join(", ", Guard.Violations));
            ulong finalTick = Sim.Host.CurrentTick;
            Assert.True(finalTick >= UiConst.TicksPerDay, what + ": the loop stopped at tick " + finalTick + ", short of a sim-day");
            List<string> rendered = Sim.Checkpoints.Describe();
            ulong hash = Sim.Host.WorldStateHash();
            (List<string> headless, ulong headlessHash) = Phase1Sim.Headless(finalTick);
            Assert.True(headless.Count >= (int)(UiConst.TicksPerDay / UiConst.HashCheckpointTicks), what + ": only " + headless.Count + " checkpoints in a sim-day");
            Assert.Equal(headless.Count, rendered.Count);
            for (int i = 0; i < headless.Count; i++)
            {
                Assert.True(headless[i] == rendered[i], what + ": checkpoint " + i + " differs.\n headless: " + headless[i] + "\n frame loop: " + rendered[i]);
            }

            Assert.Equal(headlessHash, hash);
        }
    }
}
