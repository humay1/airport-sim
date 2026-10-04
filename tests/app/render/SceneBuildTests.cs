using System.Diagnostics;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.11: Build (a rebuilding call) plus Update, mean ≤ 2.0 ms and
    /// p99 ≤ 4.0 ms per frame at max tier with the High preset, and no
    /// allocation in either after the first call.
    /// </summary>
    public sealed class SceneBuildTests
    {
        private const long BudgetMicros = 2000;
        private const long P99Micros = 4000;

        // 03 "Budget tests: window and arithmetic" sizes its window in ticks
        // (TICKS_PER_SIM_DAY). This budget is per frame; the suite samples the
        // same count of frames, each a rebuilding Build.
        private const int Frames = (int)RenderConst.TicksPerDay;
        private const int WarmUpFrames = 200;

        [Fact]
        [Trait("Category", "Budget")]
        public void test_scene_build_within_frame_budget_at_max_tier()
        {
            var m = new MaxTierScene();
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(m.Sources, m.Layout);
            CameraView cam = MaxTierScene.Camera;
            GraphicsSettings high = Gfx.High();

            c.Update(cam, high);
            RenderFrame first = b.Build(cam, high);
            Assert.Equal(16 * RenderConst.MaxDrawnAgentsPerNode, Prims.InLayer(Prims.Copy(first), DrawLayer.Agent).Count);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                m.Host.Tick++;
                c.Update(cam, high);
                b.Build(cam, high);
            }

            var window = new BudgetWindow(Frames, BudgetMicros);
            for (int i = 0; i < Frames; i++)
            {
                // A new tick each frame, so every Build rebuilds (15 §15.6).
                m.Host.Tick++;
                long t0 = Stopwatch.GetTimestamp();
                c.Update(cam, high);
                b.Build(cam, high);
                long t1 = Stopwatch.GetTimestamp();
                window.Add(t1 - t0);
            }

            string? why = window.Verdict(P99Micros, "Update + Build at max tier, High");
            Assert.True(why == null, why);
            Assert.Empty(m.Guard.Violations);
        }

        [Fact]
        public void test_scene_build_and_update_allocate_nothing_after_first_call()
        {
            var m = new MaxTierScene();
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = RenderFactory.CreateSceneBuilder(m.Sources, m.Layout);
            GraphicsSettings high = Gfx.High();
            GraphicsSettings low = Gfx.Custom(false, 32, 60, 75, false);

            // The first call is the largest frame: every later one fits its buffers.
            c.Update(MaxTierScene.Camera, high);
            b.Build(MaxTierScene.Camera, high);

            CameraView[] cameras = { MaxTierScene.Camera, Cam.Away(), Cam.At(35f, 35f, 500f), MaxTierScene.Camera };
            long start = Allocation.Start();
            for (int frame = 0; frame < 120; frame++)
            {
                CameraView cam = cameras[frame % cameras.Length];
                GraphicsSettings g = frame % 3 == 1 ? low : high;
                m.Host.Tick++;
                c.Update(cam, g);
                b.Build(cam, g);
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "Update + Build allocated " + allocated + " bytes over 120 frames after the first call (15 §15.11)");
            Assert.True(m.Flow.SetPromotedCount > 16, "the metered frames never changed promotion");
            Assert.Empty(m.Guard.Violations);
        }
    }
}
