using System;
using System.Collections.Generic;
using System.Numerics;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// ISimClock, 08 §8.2 "Tick numbering and clock arithmetic" (Q-014 A5).
    /// The only public way to reach a clock is TickContext.Clock. The clock is
    /// a pure function of the tick counter, so MinutesBetween and TickOfDayTime
    /// are called on a clock captured during a tick. Fx values are compared by
    /// Raw (value = Raw / 2^32, 08 §8.3).
    /// </summary>
    public sealed class ClockTests
    {
        private const long OneRaw = 1L << 32;

        private static ISimClock CaptureClock()
        {
            ISimClock? clock = null;
            var probe = new ProbeSystem(1) { OnTick = (ProbeSystem self, in TickContext ctx) => clock = ctx.Clock };
            Harness.Build(new RecordingCheckpointSink(), probe).Step(1);
            Assert.NotNull(clock);
            return clock!;
        }

        /// <summary>floor((b - a) * 2^32 / 10), the exact oracle for MinutesBetween's Raw.</summary>
        private static long MinutesRawOracle(ulong a, ulong b)
        {
            BigInteger num = ((BigInteger)b - a) * OneRaw;
            BigInteger q = BigInteger.DivRem(num, 10, out BigInteger r);
            if (r.Sign < 0)
            {
                q -= 1;
            }

            return (long)q;
        }

        [Fact]
        public void test_clock_second_of_day_and_day_index_follow_tick_formula()
        {
            var failures = new List<string>();
            var probe = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ulong t = ctx.Tick;
                    uint expectedDay = (uint)(t / 14400UL);
                    uint expectedSecond = (uint)(t % 14400UL * 6UL);
                    if (ctx.Clock.DayIndex != expectedDay || ctx.Clock.SecondOfDay != expectedSecond || ctx.Clock.CurrentTick != t)
                    {
                        failures.Add($"tick {t}: day {ctx.Clock.DayIndex} second {ctx.Clock.SecondOfDay} current {ctx.Clock.CurrentTick}");
                    }
                },
            };
            ISimHost host = Harness.Build(new RecordingCheckpointSink(), probe);

            host.Step(14400 + 2);

            Assert.True(failures.Count == 0, string.Join("\n", failures.GetRange(0, Math.Min(10, failures.Count))));
            Assert.Equal(14402, probe.TickCalls);
        }

        [Fact]
        public void test_clock_day_boundary_values()
        {
            var seen = new Dictionary<ulong, (uint Day, uint Second)>();
            var probe = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    if (ctx.Tick == 0 || ctx.Tick == 1 || ctx.Tick == 14399 || ctx.Tick == 14400 || ctx.Tick == 14401)
                    {
                        seen[ctx.Tick] = (ctx.Clock.DayIndex, ctx.Clock.SecondOfDay);
                    }
                },
            };
            Harness.Build(new RecordingCheckpointSink(), probe).Step(14402);

            Assert.Equal((0U, 0U), seen[0]);
            Assert.Equal((0U, 6U), seen[1]);
            Assert.Equal((0U, 86394U), seen[14399]);
            Assert.Equal((1U, 0U), seen[14400]);
            Assert.Equal((1U, 6U), seen[14401]);
        }

        [Fact]
        public void test_clock_minutes_between_whole_minutes()
        {
            ISimClock c = CaptureClock();
            Assert.Equal(OneRaw, c.MinutesBetween(0, 10).Raw);
            Assert.Equal(60 * OneRaw, c.MinutesBetween(0, 600).Raw);
            Assert.Equal(1440 * OneRaw, c.MinutesBetween(14400, 28800).Raw);
        }

        [Fact]
        public void test_clock_minutes_between_reversed_order_is_negative_not_an_error()
        {
            ISimClock c = CaptureClock();
            Assert.Equal(-OneRaw, c.MinutesBetween(10, 0).Raw);
            Assert.Equal(-60 * OneRaw, c.MinutesBetween(600, 0).Raw);
        }

        [Fact]
        public void test_clock_minutes_between_equal_ticks_is_zero()
        {
            ISimClock c = CaptureClock();
            Assert.Equal(0L, c.MinutesBetween(0, 0).Raw);
            Assert.Equal(0L, c.MinutesBetween(123456789, 123456789).Raw);
        }

        [Fact]
        public void test_clock_minutes_between_fraction_floors_toward_negative_infinity()
        {
            ISimClock c = CaptureClock();
            // 1 tick = 0.1 minute: 2^32 / 10 = 429496729.6
            Assert.Equal(429496729L, c.MinutesBetween(0, 1).Raw);
            Assert.Equal(-429496730L, c.MinutesBetween(1, 0).Raw);
            // -4 ticks: -1717986918.4 floors to -1717986919
            Assert.Equal(-1717986919L, c.MinutesBetween(7, 3).Raw);
            Assert.Equal(1717986918L, c.MinutesBetween(3, 7).Raw);
        }

        [Fact]
        public void test_clock_minutes_between_matches_floor_oracle_property()
        {
            const ulong seed = 0x7001_C10CUL;
            var rng = new SplitMix64(seed);
            ISimClock c = CaptureClock();
            for (int i = 0; i < 5000; i++)
            {
                // Below 2^34 so |b - a| / 10 stays inside Fx's range.
                ulong a = rng.Next() >> 30;
                ulong b = rng.Next() >> 30;
                long expected = MinutesRawOracle(a, b);
                long actual = c.MinutesBetween(a, b).Raw;
                Assert.True(expected == actual, $"seed {seed:X}, iteration {i}: MinutesBetween({a}, {b}).Raw = {actual}, expected {expected}");
            }
        }

        [Fact]
        public void test_clock_minutes_between_tick_above_int64_throws_overflow()
        {
            ISimClock c = CaptureClock();
            ulong big = (ulong)long.MaxValue + 1UL;
            Assert.Throws<OverflowException>(() => c.MinutesBetween(big, 0));
            Assert.Throws<OverflowException>(() => c.MinutesBetween(0, big));
            Assert.Throws<OverflowException>(() => c.MinutesBetween(ulong.MaxValue, ulong.MaxValue));
        }

        [Fact]
        public void test_clock_minutes_between_result_outside_fx_range_throws_overflow()
        {
            ISimClock c = CaptureClock();
            // 2^31 minutes = 21 474 836 480 ticks is one past Fx.MaxValue.
            Assert.Throws<OverflowException>(() => c.MinutesBetween(0, 21474836480UL));
            Assert.Equal(MinutesRawOracle(0, 21474836479UL), c.MinutesBetween(0, 21474836479UL).Raw);
        }

        [Fact]
        public void test_clock_tick_of_day_time_is_day_offset_plus_floored_second()
        {
            ISimClock c = CaptureClock();
            Assert.Equal(0UL, c.TickOfDayTime(0, 0));
            Assert.Equal(0UL, c.TickOfDayTime(0, 5));
            Assert.Equal(1UL, c.TickOfDayTime(0, 6));
            Assert.Equal(1UL, c.TickOfDayTime(0, 11));
            Assert.Equal(14399UL, c.TickOfDayTime(0, 86399));
            Assert.Equal(14400UL, c.TickOfDayTime(1, 0));
            Assert.Equal(3UL * 14400UL + 1UL, c.TickOfDayTime(3, 7));
            Assert.Equal((ulong)uint.MaxValue * 14400UL + 14399UL, c.TickOfDayTime(uint.MaxValue, 86399));
        }

        [Fact]
        public void test_clock_tick_of_day_time_second_out_of_range_throws()
        {
            ISimClock c = CaptureClock();
            Assert.Throws<ArgumentOutOfRangeException>(() => c.TickOfDayTime(0, 86400));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.TickOfDayTime(2, 86401));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.TickOfDayTime(0, uint.MaxValue));
        }

        [Fact]
        public void test_clock_round_trips_with_tick_of_day_time()
        {
            var failures = new List<string>();
            var probe = new ProbeSystem(1)
            {
                OnTick = (ProbeSystem self, in TickContext ctx) =>
                {
                    ulong back = ctx.Clock.TickOfDayTime(ctx.Clock.DayIndex, ctx.Clock.SecondOfDay);
                    if (back != ctx.Tick)
                    {
                        failures.Add($"tick {ctx.Tick} -> {back}");
                    }
                },
            };
            Harness.Build(new RecordingCheckpointSink(), probe).Step(14400 + 700);
            Assert.True(failures.Count == 0, string.Join("\n", failures.GetRange(0, Math.Min(10, failures.Count))));
            Assert.Equal(15100, probe.TickCalls);
        }
    }
}
