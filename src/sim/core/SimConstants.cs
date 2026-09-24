namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Compile-time constants of <c>sim.core</c>. Spec: 08-interfaces-core.md §8.1.
    /// </summary>
    public static class SimConstants
    {
        /// <summary>Spec: 01-architecture.md, locked.</summary>
        public const int TICK_MS = 100;

        /// <summary>Spec: 08-interfaces-core.md §8.2 — HUMAN DECISION (Q-002, D2).</summary>
        public const int SIM_SECONDS_PER_TICK = 6;

        /// <summary>Spec: 08-interfaces-core.md §8.1, derived.</summary>
        public const ulong TICKS_PER_SIM_MINUTE = 10;

        /// <summary>Spec: 08-interfaces-core.md §8.1, derived.</summary>
        public const ulong TICKS_PER_SIM_HOUR = 600;

        /// <summary>Spec: 08-interfaces-core.md §8.1, derived.</summary>
        public const ulong TICKS_PER_SIM_DAY = 14400;

        /// <summary>Spec: 08-interfaces-core.md §8.9.</summary>
        public const ulong HASH_CHECKPOINT_TICKS = 600;

        /// <summary>Spec: 08-interfaces-core.md §8.7.</summary>
        public const ulong COMMAND_MIN_LEAD_TICKS = 1;

        /// <summary>Spec: 08-interfaces-core.md §8.6.</summary>
        public const int MAX_EVENT_CASCADE_PASSES = 8;

        /// <summary>Spec: 08-interfaces-core.md §8.6.</summary>
        public const int MAX_EVENTS_PER_TICK = 4096;

        /// <summary>Spec: 06-delay-attribution.md.</summary>
        public const int MAX_ATTRIBUTION_DEPTH = 6;

        /// <summary>Spec: 08-interfaces-core.md §8.3.</summary>
        public const int FX_FRACTIONAL_BITS = 32;

        /// <summary>Spec: 08-interfaces-core.md §8.4 — the id sim.core uses as an event Source and log system for its own work.</summary>
        public static readonly SystemId SYSTEM_CORE = new SystemId(0);

        /// <summary>Spec: 08-interfaces-core.md §8.7 — the only player at Phase 1.</summary>
        public static readonly PlayerId PLAYER_LOCAL = new PlayerId(0);
    }
}
