using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.12's integration tests: one real sim-day driven through 16
    /// §16.6's frame order (promotion, pacer, Step, build; this suite has no
    /// app.ui, so step 1 is the test's own script), against the same day
    /// stepped headless. Promotion is outcome-neutral (09 §9.1, 15 §15.7) and
    /// graphics are presentation only (15 §15.14), so the checkpoints match.
    /// </summary>
    public sealed class RenderLoopTests
    {
        private const int MaxFrames = 200000;

        private static readonly GameSpeed[] Speeds = { GameSpeed.X4, GameSpeed.X4, GameSpeed.X2, GameSpeed.X1, GameSpeed.X4 };

        /// <summary>
        /// The scripted camera: each box in turn, held for 40 frames at one of
        /// four poses, cycling below, at and just above AGENT_ZOOM_THRESHOLD,
        /// then out of view.
        /// </summary>
        private static CameraView Script(int frame, IReadOnlyList<FlowNodeBox> boxes)
        {
            int segment = frame / 40;
            FlowNodeBox box = boxes[segment % boxes.Count];
            switch ((segment / boxes.Count) % 4)
            {
                case 0: return Cam.On(box, 60f, 16f / 9f);
                case 1: return Cam.On(box, RenderConst.AgentZoomThreshold, 16f / 9f);
                case 2: return Cam.On(box, 121f, 16f / 9f);
                default: return Cam.Away();
            }
        }

        private static (List<string> Checkpoints, ulong Hash) Headless(ulong ticks)
        {
            var sim = new Phase1Sim();
            for (ulong t = 0; t < ticks; t++)
            {
                sim.Host.Step(1);
            }

            return (sim.Checkpoints.Describe(), sim.Host.WorldStateHash());
        }

        private sealed class LoopResult
        {
            public List<string> Checkpoints = new List<string>();
            public ulong Hash;
            public ulong FinalTick;
            public GuardedFlow Flow = null!;
            public CallGuard Guard = null!;
            public HashSet<string> GraphicsUsed = new HashSet<string>();
        }

        /// <summary>Runs one sim-day through the frame order; graphics(frame) picks each frame's settings.</summary>
        private static LoopResult RenderedDay(ulong inputSeed, Func<int, GraphicsSettings> graphics)
        {
            var sim = new Phase1Sim();
            var guard = new CallGuard();
            var flow = new GuardedFlow(sim.Flow, guard);
            var sources = new RenderSources(new GuardedHost(sim.Host, guard), new GuardedAirside(sim.Airside, guard), flow);
            RenderLayout layout = Phase1RenderLayout.Build();
            List<FlowNodeBox> boxes = layout.FlowNodes.OrderBy(b => b.Node.Value).ToList();

            IPromotionController promotion = RenderFactory.CreatePromotionController(sources, layout);
            ISceneBuilder scene = RenderFactory.CreateSceneBuilder(sources, layout);
            ITickPacer pacer = RenderFactory.CreatePacer();

            var result = new LoopResult { Flow = flow, Guard = guard };
            var rng = new SplitMix64(inputSeed);
            GameSpeed speed = GameSpeed.X4;
            int pausedFor = 0;
            int frame = 0;
            while (sim.Host.CurrentTick < RenderConst.TicksPerDay)
            {
                Assert.True(frame < MaxFrames, "seed " + inputSeed + ": no sim-day after " + MaxFrames + " frames");
                CameraView camera = Script(frame, boxes);
                GraphicsSettings g = graphics(frame);
                result.GraphicsUsed.Add(Gfx.Show(g));
                long elapsed = rng.Range(4000, 250000);
                if (rng.Range(0, 99) == 0)
                {
                    speed = Speeds[rng.Range(0, Speeds.Length - 1)];
                }

                if (pausedFor == 0 && rng.Range(0, 199) == 0)
                {
                    pausedFor = rng.Range(1, 30);
                }

                bool paused = pausedFor > 0;
                if (paused)
                {
                    pausedFor--;
                }

                // 16 §16.6 steps 2 to 4.
                promotion.Update(camera, g);
                uint n = pacer.Advance(elapsed, paused, speed);
                if (n > 0)
                {
                    sim.Host.Step(n);
                }

                RenderFrame f = scene.Build(camera, g);
                Assert.True(f.Tick == sim.Host.CurrentTick, "seed " + inputSeed + ", frame " + frame + ": RenderFrame.Tick " + f.Tick + " but the host is at " + sim.Host.CurrentTick);
                frame++;
            }

            result.FinalTick = sim.Host.CurrentTick;
            result.Checkpoints = sim.Checkpoints.Describe();
            result.Hash = sim.Host.WorldStateHash();
            return result;
        }

        private static void AssertNeutral(LoopResult rendered, string what)
        {
            Assert.True(rendered.Guard.Violations.Count == 0, what + ": members outside 15 §15.6 were called: " + string.Join(", ", rendered.Guard.Violations));
            (List<string> headless, ulong hash) = Headless(rendered.FinalTick);
            Assert.True(headless.Count >= (int)(RenderConst.TicksPerDay / RenderConst.HashCheckpointTicks), what + ": only " + headless.Count + " checkpoints in a sim-day");
            Assert.Equal(headless.Count, rendered.Checkpoints.Count);
            for (int i = 0; i < headless.Count; i++)
            {
                Assert.True(headless[i] == rendered.Checkpoints[i], what + ": checkpoint " + i + " differs.\n headless: " + headless[i] + "\n rendered: " + rendered.Checkpoints[i]);
            }

            Assert.Equal(hash, rendered.Hash);

            // The camera really swept every box across the threshold, and agent views were derived.
            foreach (FlowNodeBox box in Phase1RenderLayout.Build().FlowNodes)
            {
                uint node = box.Node.Value;
                int promoted = rendered.Flow.SetPromotedCalls.FindIndex(c => c.Node == node && c.Promoted);
                Assert.True(promoted >= 0, what + ": node " + node + " was never promoted");
                Assert.True(rendered.Flow.SetPromotedCalls.FindIndex(promoted, c => c.Node == node && !c.Promoted) >= 0, what + ": node " + node + " was never demoted after its promotion");
            }

            Assert.True(rendered.Flow.AgentViewsSeen > 0, what + ": no agent view was ever derived");
        }

        [Fact]
        public void test_render_loop_is_outcome_neutral_with_scripted_camera()
        {
            // The layout's boxes are exactly the flow fixture's nodes, which
            // include every schedule entry node and at least one Queue (15 §15.4, §15.12).
            List<(uint Node, string Kind)> fixtureNodes = Phase1Sim.FlowFixtureNodes();
            Assert.True(fixtureNodes.Count > 0, "no node read from " + Phase1Sim.FlowFile);
            var boxNodes = new SortedSet<uint>(Phase1RenderLayout.Build().FlowNodes.Select(b => b.Node.Value));
            Assert.Equal(boxNodes.Count, Phase1RenderLayout.Build().FlowNodes.Count);
            Assert.Equal(new SortedSet<uint>(fixtureNodes.Select(n => n.Node)), boxNodes);
            Assert.Contains(fixtureNodes, n => n.Kind == "queue" && boxNodes.Contains(n.Node));
            Assert.True(Phase1Sim.ScheduleEntryNodes().IsSubsetOf(boxNodes), "a schedule entry node has no FlowNodeBox");

            LoopResult rendered = RenderedDay(0x0F7A_0020UL, _ => Gfx.High());
            AssertNeutral(rendered, "scripted camera");
        }

        [Fact]
        public void test_render_loop_is_outcome_neutral_across_graphics_changes()
        {
            GraphicsSettings[] choices =
            {
                RenderFactory.GraphicsForPreset(GraphicsPreset.Low),
                RenderFactory.GraphicsForPreset(GraphicsPreset.Medium),
                RenderFactory.GraphicsForPreset(GraphicsPreset.High),
                Gfx.Custom(false, 1, 15, 50, false),
                Gfx.Custom(true, 1, 240, 100, true),
                Gfx.Custom(true, 7, 0, 63, false),
                Gfx.Custom(false, 256, 0, 100, true),
            };

            // Switch at irregular frames, from a stream of its own.
            var pick = new SplitMix64(0x6FC5_0020UL);
            int nextSwitch = 0;
            GraphicsSettings current = choices[2];
            LoopResult rendered = RenderedDay(0x0F7A_0021UL, frame =>
            {
                if (frame >= nextSwitch)
                {
                    current = choices[pick.Range(0, choices.Length - 1)];
                    nextSwitch = frame + pick.Range(1, 45);
                }

                return current;
            });

            foreach (GraphicsSettings g in choices)
            {
                Assert.True(rendered.GraphicsUsed.Contains(Gfx.Show(g)), "the day never used " + Gfx.Show(g));
            }

            AssertNeutral(rendered, "graphics changes");
        }
    }
}
