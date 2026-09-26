namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11a.</summary>
    public readonly struct SimHostConfig
    {
        /// <summary>The seed the RNG service is built from at <c>Build</c>.</summary>
        public ulong MasterSeed { get; }

        /// <summary>The content index, non-null.</summary>
        public IContentIndex Content { get; }

        /// <summary>The checkpoint sink, non-null.</summary>
        public ICheckpointSink Checkpoints { get; }

        /// <summary>The log sink, non-null.</summary>
        public ISimLog Log { get; }

        /// <summary>Constructs the config. None of the reference members may be null.</summary>
        public SimHostConfig(ulong masterSeed, IContentIndex content, ICheckpointSink checkpoints, ISimLog log)
        {
            MasterSeed = masterSeed;
            Content = content;
            Checkpoints = checkpoints;
            Log = log;
        }
    }
}
