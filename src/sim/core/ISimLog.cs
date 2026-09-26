namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The deterministic logging sink. Every line carries the tick. Spec: 08-interfaces-core.md §8.10.
    /// </summary>
    public interface ISimLog
    {
        /// <summary>Writes one deterministic log line, tagged with the tick and emitting system.</summary>
        void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args);
    }
}
