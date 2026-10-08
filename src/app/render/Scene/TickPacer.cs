using System;

namespace AirportSim.App.Render
{
    /// <summary>The integer-accumulator tick pacer. Spec: 15 §15.8 (D4, Q-098). Presentation state, never saved.</summary>
    internal sealed class TickPacer : ITickPacer
    {
        // Far above any real frame; keeps elapsed x speed from overflowing. Any such value hits the catch-up cap anyway.
        private const long MaxElapsedMicroseconds = 1000000000000000L;

        private long _accumulator;

        public long SubTickMicroseconds => _accumulator;

        public uint Advance(long elapsedRealMicroseconds, bool paused, GameSpeed speed)
        {
            if (elapsedRealMicroseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsedRealMicroseconds), "elapsed real time must not be negative");
            }

            if (speed != GameSpeed.X1 && speed != GameSpeed.X2 && speed != GameSpeed.X4)
            {
                throw new ArgumentOutOfRangeException(nameof(speed), "unknown game speed");
            }

            if (paused)
            {
                return 0;
            }

            long elapsed = elapsedRealMicroseconds > MaxElapsedMicroseconds ? MaxElapsedMicroseconds : elapsedRealMicroseconds;
            _accumulator += elapsed * (int)speed;
            long ticks = _accumulator / RenderConstants.REAL_MICROSECONDS_PER_TICK_1X;
            _accumulator -= ticks * RenderConstants.REAL_MICROSECONDS_PER_TICK_1X;
            if (ticks > RenderConstants.MAX_CATCHUP_TICKS_PER_FRAME)
            {
                ticks = RenderConstants.MAX_CATCHUP_TICKS_PER_FRAME;
                _accumulator = 0;
            }

            return (uint)ticks;
        }
    }
}
