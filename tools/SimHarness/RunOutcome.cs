using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// What one full run produced: its checkpoints, in order, and its final
    /// <c>WorldStateHash()</c>. Spec: 19-interfaces-harness.md §19.1, §19.2 (Q-026).
    /// </summary>
    internal readonly struct RunOutcome
    {
        public Checkpoint[] Checkpoints { get; }

        public ulong FinalHash { get; }

        public RunOutcome(Checkpoint[] checkpoints, ulong finalHash)
        {
            Checkpoints = checkpoints;
            FinalHash = finalHash;
        }
    }
}
