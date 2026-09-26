namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One recorded state-hash checkpoint. Spec: 08-interfaces-core.md §8.9.
    /// </summary>
    public readonly struct Checkpoint
    {
        /// <summary>The tick the checkpoint was taken on: every tick where <c>tick % 600 == 0</c>.</summary>
        public ulong Tick { get; }

        /// <summary>Equal to <c>WorldStateHash()</c> right after this tick's <c>Step</c>.</summary>
        public ulong WorldHash { get; }

        /// <summary>The core section: next command sequence, pending commands, id counters.</summary>
        public ulong CoreHash { get; }

        /// <summary>One entry per registered system, in registry order (§8.5).</summary>
        public ulong[] SystemHashes { get; }

        /// <summary>Constructs the checkpoint from its four recorded fields.</summary>
        public Checkpoint(ulong tick, ulong worldHash, ulong coreHash, ulong[] systemHashes)
        {
            Tick = tick;
            WorldHash = worldHash;
            CoreHash = coreHash;
            SystemHashes = systemHashes;
        }
    }
}
