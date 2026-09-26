using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// Discards every log line. Local to the harness only: <c>sim.core</c> ships no null
    /// or empty implementations of its published interfaces (spec/08-interfaces-core.md
    /// §8.11a, Q-014 A7).
    /// </summary>
    internal sealed class NullSimLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
            // Discarded: the harness is a thin host, not a log consumer.
        }
    }
}
