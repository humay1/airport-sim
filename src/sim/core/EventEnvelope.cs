namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Fields every event carries, assigned by the bus on publish. Spec: 10-events.md §10.2,
    /// 08-interfaces-core.md §8.6 ("Envelope and publication").
    /// </summary>
    public readonly struct EventEnvelope
    {
        public EventId Id { get; }
        public ulong Tick { get; }
        public SystemId Source { get; }
        public EventRef Cause { get; }

        public EventEnvelope(EventId id, ulong tick, SystemId source, EventRef cause)
        {
            Id = id;
            Tick = tick;
            Source = source;
            Cause = cause;
        }
    }
}
