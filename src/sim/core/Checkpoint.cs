namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One recorded state-hash checkpoint. Spec: 08-interfaces-core.md §8.9.
    /// </summary>
    public readonly struct Checkpoint
    {
        public ulong Tick { get; }
        public ulong WorldHash { get; }

        /// <summary>The core section: next command sequence, pending commands, id counters.</summary>
        public ulong CoreHash { get; }

        /// <summary>One entry per registered system, in registry order (§8.5).</summary>
        public ulong[] SystemHashes { get; }

        public Checkpoint(ulong tick, ulong worldHash, ulong coreHash, ulong[] systemHashes)
        {
            Tick = tick;
            WorldHash = worldHash;
            CoreHash = coreHash;
            SystemHashes = systemHashes;
        }
    }
}
