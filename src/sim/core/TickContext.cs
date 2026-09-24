namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Everything a system's <c>Tick</c> needs for one tick. Spec: 08-interfaces-core.md §8.5.
    /// Must not allocate on the hot path.
    /// </summary>
    public readonly struct TickContext
    {
        public ulong Tick { get; }
        public ISimClock Clock { get; }
        public IEventPublisher Events { get; }
        public IRandomService Rng { get; }
        public IContentIndex Content { get; }
        public ISimLog Log { get; }

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
