namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A pure function of the tick counter. Holds no mutable state and contributes nothing
    /// to the state hash. Spec: 08-interfaces-core.md §8.2.
    /// </summary>
    public interface ISimClock
    {
        /// <summary>The tick currently executing.</summary>
        ulong CurrentTick { get; }

        /// <summary>The day index of <see cref="CurrentTick"/>.</summary>
        uint DayIndex { get; }

        /// <summary>The second of the day of <see cref="CurrentTick"/>.</summary>
        uint SecondOfDay { get; }

        /// <summary>Signed minutes from tick a to tick b. Spec: 08-interfaces-core.md §8.2.</summary>
        Fx MinutesBetween(ulong a, ulong b);

        /// <summary>The tick of the given day index and second of day. Throws if secondOfDay &gt;= 86400.</summary>
        ulong TickOfDayTime(uint dayIndex, uint secondOfDay);
    }
}
