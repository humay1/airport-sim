namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11a.</summary>
    public readonly struct SimHostConfig
    {
        public ulong MasterSeed { get; }
        public IContentIndex Content { get; }
        public ICheckpointSink Checkpoints { get; }
        public ISimLog Log { get; }

        public SimHostConfig(ulong masterSeed, IContentIndex content, ICheckpointSink checkpoints, ISimLog log)
        {
            MasterSeed = masterSeed;
            Content = content;
            Checkpoints = checkpoints;
            Log = log;
        }
    }
}
