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
            checked
            {
                long delta = (long)b - (long)a;
                return Fx.FromRatio(delta, (long)SimConstants.TICKS_PER_SIM_MINUTE);
            }
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
