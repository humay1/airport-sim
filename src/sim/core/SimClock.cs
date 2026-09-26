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

        public Fx MinutesBetween(ulong a, ulong b)
        {
            // 08 §8.2: "A tick above int64 range throws OverflowException" — a and b
            // themselves are checked, not the magnitude of their difference. Once both
            // are known to be <= long.MaxValue, (long)b - (long)a always fits long
            // (07-conventions.md "Error handling": explicit checks, never a checked
            // context, never a BCL operator's own exception).
            if (a > long.MaxValue)
            {
                throw new OverflowException("SimClock.MinutesBetween: a exceeds int64 range.");
            }
            if (b > long.MaxValue)
            {
                throw new OverflowException("SimClock.MinutesBetween: b exceeds int64 range.");
            }

            long delta = unchecked((long)b - (long)a);
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
