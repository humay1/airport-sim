using System;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// Parses a Phase 0 CSV fixture into a <see cref="ScheduleTable"/>.
    /// Spec: 11-interfaces-schedule.md §11.4.
    /// </summary>
    public interface IScheduleLoader
    {
        /// <summary>
        /// Parses <paramref name="csv"/>, named <paramref name="sourceName"/> in any error
        /// message. Throws <see cref="FormatException"/>, whose message starts with
        /// <c>sourceName + ": "</c> and names the offending line, for any rule
        /// violation in §11.4.
        /// </summary>
        ScheduleTable Load(ReadOnlySpan<byte> csv, string sourceName);
    }
}
