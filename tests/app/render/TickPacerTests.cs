using System;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.8 (amended by D4): an integer accumulator of speed-scaled
    /// microseconds, REAL_MICROSECONDS_PER_TICK_1X = 100 000, at most
    /// MAX_CATCHUP_TICKS_PER_FRAME = 3 ticks a frame.
    /// </summary>
    public sealed class TickPacerTests
    {
        private static uint Sum(ITickPacer p, long elapsed, int frames, GameSpeed speed)
        {
            uint total = 0;
            for (int i = 0; i < frames; i++)
            {
                uint n = p.Advance(elapsed, false, speed);
                Assert.True(n <= RenderConst.MaxCatchupTicksPerFrame, "one frame stepped " + n + " ticks");
                total += n;
            }

            return total;
        }

        [Fact]
        public void test_tick_pacer_steps_ten_ticks_per_real_second()
        {
            // 60 fps: 40 frames of 16 666 us and 20 of 16 668 us make exactly one second.
            ITickPacer p = RenderFactory.CreatePacer();
            uint total = Sum(p, 16666, 40, GameSpeed.X1) + Sum(p, 16668, 20, GameSpeed.X1);
            Assert.Equal(10U, total);

            // Ten more seconds at uneven frame lengths, each under the catch-up cap.
            var rng = new SplitMix64(0x7E57_0020UL);
            long left = 10L * 1000000L;
            total = 0;
            int frame = 0;
            while (left > 0)
            {
                long dt = Math.Min(left, rng.Range(1000, 250000));
                left -= dt;
                uint n = p.Advance(dt, false, GameSpeed.X1);
                Assert.True(n <= RenderConst.MaxCatchupTicksPerFrame, "seed 0x7E570020, frame " + frame + ": " + n + " ticks");
                total += n;
                frame++;
            }

            Assert.True(total == 100U, "seed 0x7E570020: ten real seconds at 1x stepped " + total + " ticks, expected 100");

            // Exactly one tick's worth steps on the frame that completes it, not before.
            ITickPacer q = RenderFactory.CreatePacer();
            Assert.Equal(0U, q.Advance(99999, false, GameSpeed.X1));
            Assert.Equal(1U, q.Advance(1, false, GameSpeed.X1));
            Assert.Equal(0U, q.Advance(0, false, GameSpeed.X1));
        }

        [Fact]
        public void test_tick_pacer_steps_forty_ticks_per_real_second_at_4x()
        {
            ITickPacer p = RenderFactory.CreatePacer();
            Assert.Equal(40U, Sum(p, 16666, 40, GameSpeed.X4) + Sum(p, 16668, 20, GameSpeed.X4));

            ITickPacer q = RenderFactory.CreatePacer();
            Assert.Equal(20U, Sum(q, 10000, 100, GameSpeed.X2));

            // 4x: 25 000 real microseconds is one tick.
            ITickPacer r = RenderFactory.CreatePacer();
            Assert.Equal(0U, r.Advance(24999, false, GameSpeed.X4));
            Assert.Equal(1U, r.Advance(1, false, GameSpeed.X4));
            Assert.Equal(3U, r.Advance(75000, false, GameSpeed.X4));
        }

        [Fact]
        public void test_tick_pacer_speed_change_keeps_accumulated_time()
        {
            ITickPacer p = RenderFactory.CreatePacer();

            // 60 000 scaled us at 1x, then 20 000 real us at 2x adds 40 000: one tick, nothing left.
            Assert.Equal(0U, p.Advance(60000, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(20000, false, GameSpeed.X2));
            Assert.Equal(0U, p.Advance(99999, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(1, false, GameSpeed.X1));

            // 30 000 at 1x, then 17 500 at 4x adds 70 000: one tick; the 0 left is not lost or duplicated.
            Assert.Equal(0U, p.Advance(30000, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(17500, false, GameSpeed.X4));
            Assert.Equal(0U, p.Advance(24999, false, GameSpeed.X4));
            Assert.Equal(1U, p.Advance(1, false, GameSpeed.X4));

            // 55 000 at 4x is 220 000 scaled: two ticks, then the 20 000 left carries into 1x.
            ITickPacer q = RenderFactory.CreatePacer();
            Assert.Equal(2U, q.Advance(55000, false, GameSpeed.X4));
            Assert.Equal(0U, q.Advance(79999, false, GameSpeed.X1));
            Assert.Equal(1U, q.Advance(1, false, GameSpeed.X1));
        }

        [Fact]
        public void test_tick_pacer_caps_catch_up_and_drops_backlog()
        {
            ITickPacer p = RenderFactory.CreatePacer();

            // A one-second hitch at 1x would be 10 ticks: 3 are stepped and the backlog is dropped.
            Assert.Equal(3U, p.Advance(1000000, false, GameSpeed.X1));
            Assert.Equal(0U, p.Advance(99999, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(1, false, GameSpeed.X1));

            // Exactly 3 is not over the cap, so the remainder is kept.
            ITickPacer q = RenderFactory.CreatePacer();
            Assert.Equal(3U, q.Advance(399999, false, GameSpeed.X1));
            Assert.Equal(1U, q.Advance(1, false, GameSpeed.X1));

            // 4 ticks is over the cap: 3, and the 50 000 remainder is dropped too.
            ITickPacer r = RenderFactory.CreatePacer();
            Assert.Equal(3U, r.Advance(450000, false, GameSpeed.X1));
            Assert.Equal(0U, r.Advance(99999, false, GameSpeed.X1));
            Assert.Equal(1U, r.Advance(1, false, GameSpeed.X1));

            // The cap is the same at 4x: 100 000 real us there is 4 ticks.
            ITickPacer t = RenderFactory.CreatePacer();
            Assert.Equal(3U, t.Advance(100000, false, GameSpeed.X4));
            Assert.Equal(0U, t.Advance(24999, false, GameSpeed.X4));
            Assert.Equal(1U, t.Advance(1, false, GameSpeed.X4));

            // A frame long enough to overflow a naive int accumulator is still capped.
            ITickPacer u = RenderFactory.CreatePacer();
            Assert.Equal(3U, u.Advance(long.MaxValue / 8, false, GameSpeed.X4));
            Assert.Equal(0U, u.Advance(1, false, GameSpeed.X1));
        }

        [Fact]
        public void test_tick_pacer_paused_steps_nothing()
        {
            ITickPacer p = RenderFactory.CreatePacer();
            Assert.Equal(0U, p.Advance(90000, false, GameSpeed.X1));

            // Paused: 0 ticks at any speed, and the elapsed time is discarded, not replayed later.
            Assert.Equal(0U, p.Advance(1000000, true, GameSpeed.X1));
            Assert.Equal(0U, p.Advance(5000000, true, GameSpeed.X4));
            Assert.Equal(0U, p.Advance(0, true, GameSpeed.X2));

            // The 90 000 accumulated before the pause is untouched by it.
            Assert.Equal(0U, p.Advance(9999, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(1, false, GameSpeed.X1));
            Assert.Equal(0U, p.Advance(99999, false, GameSpeed.X1));
        }

        [Fact]
        public void test_tick_pacer_rejects_negative_elapsed_and_unknown_speed()
        {
            // Q-098: ArgumentOutOfRangeException naming the parameter, elapsed
            // checked first, both checked even when paused, accumulator unchanged.
            ITickPacer p = RenderFactory.CreatePacer();
            Assert.Equal(0U, p.Advance(60000, false, GameSpeed.X1));

            foreach (bool paused in new[] { false, true })
            {
                foreach (long elapsed in new[] { -1L, long.MinValue })
                {
                    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => p.Advance(elapsed, paused, GameSpeed.X1));
                    Assert.Equal("elapsedRealMicroseconds", ex.ParamName);
                }

                foreach (int bad in new[] { 0, 3, 5, 8, -1, int.MaxValue })
                {
                    var ex = Assert.Throws<ArgumentOutOfRangeException>(() => p.Advance(1000, paused, (GameSpeed)bad));
                    Assert.Equal("speed", ex.ParamName);
                }

                // Both wrong: elapsed is named.
                var both = Assert.Throws<ArgumentOutOfRangeException>(() => p.Advance(-5, paused, (GameSpeed)3));
                Assert.Equal("elapsedRealMicroseconds", both.ParamName);
            }

            // The 60 000 accumulated before the throws is still there, and nothing was added.
            Assert.Equal(0U, p.Advance(39999, false, GameSpeed.X1));
            Assert.Equal(1U, p.Advance(1, false, GameSpeed.X1));
            Assert.Equal(0U, p.Advance(99999, false, GameSpeed.X1));
        }
    }
}
