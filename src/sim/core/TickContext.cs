namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Everything a system's <c>Tick</c> needs for one tick. Spec: 08-interfaces-core.md §8.5.
    /// Must not allocate on the hot path.
    /// </summary>
    public readonly struct TickContext
    {
        /// <summary>The tick currently executing.</summary>
        public ulong Tick { get; }

        /// <summary>The clock, a pure function of Tick.</summary>
        public ISimClock Clock { get; }

        /// <summary>The publish side of the event bus.</summary>
        public IEventPublisher Events { get; }

        /// <summary>The seeded RNG service.</summary>
        public IRandomService Rng { get; }

        /// <summary>The read-only content index, the config's instance.</summary>
        public IContentIndex Content { get; }

        /// <summary>The log sink, the config's instance.</summary>
        public ISimLog Log { get; }

        /// <summary>Constructs the context from its six members.</summary>
        public TickContext(ulong tick, ISimClock clock, IEventPublisher events, IRandomService rng, IContentIndex content, ISimLog log)
        {
            Tick = tick;
            Clock = clock;
            Events = events;
            Rng = rng;
            Content = content;
            Log = log;
        }
    }
}
