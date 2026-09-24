namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A pure function of the tick counter. Holds no mutable state and contributes nothing
    /// to the state hash. Spec: 08-interfaces-core.md §8.2.
    /// </summary>
    public interface ISimClock
    {
        ulong CurrentTick { get; }
        uint DayIndex { get; }
        uint SecondOfDay { get; }
        Fx MinutesBetween(ulong a, ulong b);
        ulong TickOfDayTime(uint dayIndex, uint secondOfDay);
    }
}
