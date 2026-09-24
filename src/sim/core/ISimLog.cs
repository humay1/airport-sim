namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The deterministic logging sink. Every line carries the tick. Spec: 08-interfaces-core.md §8.10.
    /// </summary>
    public interface ISimLog
    {
        void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args);
    }
}
