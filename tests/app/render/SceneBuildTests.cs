using System.Collections.Generic;
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
            // Q-132 (15 §15.11, §15.23): the motion scene, built at a sub-tick
            // that changes every frame, so every frame rebuilds.
            var m = new MaxTierScene(motion: true);
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = m.Builder();
            CameraView cam = MaxTierScene.Camera;
            GraphicsSettings high = Gfx.High();

            c.Update(cam, high);
            RenderFrame first = b.Build(cam, high, 0);
            List<DrawPrimitive> all = Prims.Copy(first);
            Assert.Equal(16 * RenderConst.MaxDrawnAgentsPerNode, Prims.InLayer(all, DrawLayer.Agent).Count);

            // Q-130 (15 §15.11): the measured frame carries the scenery, the
            // markings, and a TryGetFlight per drawn aircraft.
            Assert.Equal(MaxTierScene.Piers + 3, Art.OfKind(all, SourceKind.Apron).Count + Art.OfKind(all, SourceKind.Building).Count);
            Assert.Equal(MaxTierScene.Stands, Art.OfKind(all, SourceKind.JetBridge).Count);
            Assert.Equal(MaxTierScene.Stands, Art.OfKind(all, SourceKind.StandMarking).Count);
            Assert.True(Art.OfKind(all, SourceKind.RunwayMarking).Count > 0, "max-tier frame has no runway marking");
            Assert.Equal((long)m.DrawnAircraft, m.Schedule.FlightCalls);

            // Q-132: every motion aircraft is drawn, and the walkways' cohorts are read.
            Assert.Equal(m.DrawnAircraft, Prims.InLayer(all, DrawLayer.Aircraft).Count);
            Assert.True(m.Flow.CohortCalls > 0, "the max-tier frame read no walkway cohort");

            // The sub-tick walks through the tick in uneven steps, always in range.
            long sub = 0;
            long NextSub()
            {
                sub = (sub + 16667L) % RenderConst.RealMicrosecondsPerTick1X;
                return sub;
            }

            for (int i = 0; i < WarmUpFrames; i++)
            {
                m.Host.Tick++;
                c.Update(cam, high);
                b.Build(cam, high, NextSub());
            }

            var window = new BudgetWindow(Frames, BudgetMicros);
            long cohorts = m.Flow.CohortCalls;
            for (int i = 0; i < Frames; i++)
            {
                // A new tick (15 §15.11) and a new sub-tick (§15.23) each
                // frame, so every Build rebuilds (§15.6).
                m.Host.Tick++;
                long s = NextSub();
                long t0 = Stopwatch.GetTimestamp();
                c.Update(cam, high);
                b.Build(cam, high, s);
                long t1 = Stopwatch.GetTimestamp();
                window.Add(t1 - t0);
            }

            // Every measured frame rebuilt: each read the 16 walkways' cohorts again.
            Assert.True(m.Flow.CohortCalls - cohorts >= 16L * Frames, "only " + (m.Flow.CohortCalls - cohorts) + " TryGetCohort calls over " + Frames + " frames: not every frame rebuilt");

            string? why = window.Verdict(P99Micros, "Update + Build at max tier, High");
            Assert.True(why == null, why);
            Assert.Empty(m.Guard.Violations);
        }

        [Fact]
        public void test_scene_build_and_update_allocate_nothing_after_first_call()
        {
            // Q-132 (15 §15.11, §15.23): the motion scene, at a sub-tick that
            // changes every frame. PierCamera adds the bridge walkers: pier 1's
            // ten stands hold arrivals, so each of its bridges also reads its
            // flight (one TryGetFlight per bridge, 15 §15.6).
            var m = new MaxTierScene(motion: true);
            IPromotionController c = RenderFactory.CreatePromotionController(m.Sources, m.Layout);
            ISceneBuilder b = m.Builder();
            GraphicsSettings high = Gfx.High();
            GraphicsSettings low = Gfx.Custom(false, 32, 60, 75, false);

            // The first calls are the largest frames: every later one fits its buffers.
            c.Update(MaxTierScene.Camera, high);
            b.Build(MaxTierScene.Camera, high, 0);
            c.Update(MaxTierScene.PierCamera, high);
            b.Build(MaxTierScene.PierCamera, high, 0);

            CameraView[] cameras = { MaxTierScene.Camera, Cam.Away(), Cam.At(35f, 35f, 500f), MaxTierScene.Camera, MaxTierScene.PierCamera };
            long flights = m.Schedule.FlightCalls;
            long expectedFlights = 0;
            long boarding = m.Flow.PopulationForFlightCalls;
            long start = Allocation.Start();
            for (int frame = 0; frame < 120; frame++)
            {
                CameraView cam = cameras[frame % cameras.Length];
                GraphicsSettings g = frame % 3 == 1 ? low : high;
                m.Host.Tick++;
                c.Update(cam, g);
                b.Build(cam, g, (frame * 7919L) % RenderConst.RealMicrosecondsPerTick1X);
                expectedFlights += m.DrawnAircraft + (frame % cameras.Length == 4 && g.DrawAgents ? MaxTierScene.StandsPerPier : 0);
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "Update + Build allocated " + allocated + " bytes over 120 frames after the first call (15 §15.11)");
            Assert.True(m.Flow.SetPromotedCount > 16, "the metered frames never changed promotion");
            Assert.Equal(flights + expectedFlights, m.Schedule.FlightCalls);
            Assert.True(m.Flow.PopulationForFlightCalls > boarding, "the metered frames drew no bridge walkers");
            Assert.Empty(m.Guard.Violations);
        }
    }
}
