using System.Collections.Generic;
using System.Diagnostics;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.17 "Budget": Fill over the §15.11 max-tier frame at High, mean ≤
    /// 2.0 ms and p99 ≤ 4.0 ms (Q-131), with §15.11's window and arithmetic, and
    /// no allocation after the first call.
    /// </summary>
    public sealed class Art2DBudgetTests
    {
        private const long BudgetMicros = 2000;
        private const long P99Micros = 4000;

        // 15 §15.11 (Q-096): n = 14 400 consecutive frames after warm-up.
        private const int Frames = (int)RenderConst.TicksPerDay;
        private const int WarmUpFrames = 200;

        [Fact]
        [Trait("Category", "Budget")]
        [Trait("Category", "Slow")] // 07 L11a's prompt: about 19–22 s in a Release run of the T-052 implementation; CI decides afterwards
        public void test_art2d_tessellator_fill_within_budget_and_allocates_nothing()
        {
            var m = new MaxTierScene();
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = m.Builder();
            CameraView cam = MaxTierScene.Camera;
            GraphicsSettings high = Gfx.High();
            c.Update(cam, high);
            RenderFrame frame = b.Build(cam, high);

            // The max-tier frame: 16 promoted boxes of MAX_DRAWN_AGENTS_PER_NODE
            // passengers, every aircraft, the scenery and markings, over the ground.
            List<DrawPrimitive> all = Prims.Copy(frame);
            Assert.Equal(16 * RenderConst.MaxDrawnAgentsPerNode, Prims.InLayer(all, DrawLayer.Agent).Count);
            Assert.Equal(MaxTierScene.Aircraft, Prims.InLayer(all, DrawLayer.Aircraft).Count);

            Rgba[] roles = ArtFrames.Roles();
            int expected = ArtRef.Fill(frame, roles, false).Count;
            Assert.True(expected >= 16 * RenderConst.MaxDrawnAgentsPerNode * 6, "the max-tier frame is smaller than its passengers");
            ISpriteTessellator t = Art2DFactory.CreateTessellator();
            int quads = t.Fill(frame, roles, false);
            Assert.Equal(expected, quads);
            Assert.Equal(quads, t.QuadCount);

            // No allocation after the first call, in either colour space.
            long start = Allocation.Start();
            for (int i = 0; i < 120; i++)
            {
                t.Fill(frame, roles, i % 2 == 1);
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "Fill allocated " + allocated + " bytes over 120 calls after the first (15 §15.17)");
            Assert.Equal(quads, t.QuadCount);

            for (int i = 0; i < WarmUpFrames; i++)
            {
                t.Fill(frame, roles, true);
            }

            var window = new BudgetWindow(Frames, BudgetMicros);
            for (int i = 0; i < Frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                t.Fill(frame, roles, true);
                long t1 = Stopwatch.GetTimestamp();
                window.Add(t1 - t0);
            }

            string? why = window.Verdict(P99Micros, "ISpriteTessellator.Fill at max tier, High");
            Assert.True(why == null, why);
            Assert.Equal(quads, t.QuadCount);
            Assert.Empty(m.Guard.Violations);
        }
    }
}
