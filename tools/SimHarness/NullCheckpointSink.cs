using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// Discards every checkpoint. Local to the harness only: <c>sim.core</c> ships no null
    /// or empty implementations of its published interfaces (spec/08-interfaces-core.md
    /// §8.11a, Q-014 A7).
    /// </summary>
    internal sealed class NullCheckpointSink : ICheckpointSink
    {
        public void Record(in Checkpoint cp)
        {
            // Discarded: the harness proves the loop shape, not checkpoint persistence.
        }
    }
}
