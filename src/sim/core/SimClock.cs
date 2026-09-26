using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="ISimClock"/> implementation: a pure function of the tick counter.
    /// Spec: 08-interfaces-core.md §8.2 ("Tick numbering and clock arithmetic", Q-014).
    /// </summary>
    internal sealed class SimClock : ISimClock
    {
        private readonly SimHost _host;

        internal SimClock(SimHost host)
        {
            _host = host;
        }

        public ulong CurrentTick => _host.CurrentTick;

        public uint DayIndex
        {
            get
            {
                ulong day = CurrentTick / SimConstants.TICKS_PER_SIM_DAY;
                if (day > uint.MaxValue)
                {
                    throw new OverflowException("DayIndex exceeds uint32 range");
                }
                return (uint)day;
            }
        }

        public uint SecondOfDay =>
            (uint)((CurrentTick % SimConstants.TICKS_PER_SIM_DAY) * (ulong)SimConstants.SIM_SECONDS_PER_TICK);

        // |long.MinValue|, as a literal, never computed by negating long.MinValue.
        private const ulong SignBitMagnitude = 1UL << 63;

        public Fx MinutesBetween(ulong a, ulong b)
        {
            // Signed b - a (07-conventions.md "Error handling": explicit overflow
            // checks, never a checked context, never a BCL operator's own exception).
            long delta;
            if (b >= a)
            {
                ulong mag = b - a;
                if (mag > long.MaxValue)
                {
                    throw new OverflowException("SimClock.MinutesBetween: delta exceeds int64 range.");
                }
                delta = (long)mag;
            }
            else
            {
                ulong mag = a - b;
                if (mag > SignBitMagnitude)
                {
                    throw new OverflowException("SimClock.MinutesBetween: delta exceeds int64 range.");
                }
                delta = mag == SignBitMagnitude ? long.MinValue : -(long)mag;
            }

            return Fx.FromRatio(delta, (long)SimConstants.TICKS_PER_SIM_MINUTE);
        }

        public ulong TickOfDayTime(uint dayIndex, uint secondOfDay)
        {
            if (secondOfDay >= 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(secondOfDay));
            }

            return (ulong)dayIndex * SimConstants.TICKS_PER_SIM_DAY + secondOfDay / (uint)SimConstants.SIM_SECONDS_PER_TICK;
        }
    }
}
