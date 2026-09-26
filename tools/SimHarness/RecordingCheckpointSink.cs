using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// Records every checkpoint, in order, for one run. Local to the harness only:
    /// <c>sim.core</c> ships no null or empty implementations of its published
    /// interfaces (spec/08-interfaces-core.md §8.11a, Q-014 A7).
    /// </summary>
    internal sealed class RecordingCheckpointSink : ICheckpointSink
    {
        private readonly List<Checkpoint> _checkpoints = new List<Checkpoint>();

        public void Record(in Checkpoint cp)
        {
            _checkpoints.Add(cp);
        }

        public Checkpoint[] ToArray() => _checkpoints.ToArray();
    }
}
