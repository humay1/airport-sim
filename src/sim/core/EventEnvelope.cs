namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Fields every event carries, assigned by the bus on publish. Spec: 10-events.md §10.2,
    /// 08-interfaces-core.md §8.6 ("Envelope and publication").
    /// </summary>
    public readonly struct EventEnvelope
    {
        /// <summary>The event's total-order id.</summary>
        public EventId Id { get; }

        /// <summary>The tick the event was published on.</summary>
        public ulong Tick { get; }

        /// <summary>The system that published the event.</summary>
        public SystemId Source { get; }

        /// <summary>The event that caused this one, or <see cref="EventRef.None"/> for a root event.</summary>
        public EventRef Cause { get; }

        /// <summary>Constructs the envelope from its four fields.</summary>
        public EventEnvelope(EventId id, ulong tick, SystemId source, EventRef cause)
        {
            Id = id;
            Tick = tick;
            Source = source;
            Cause = cause;
        }
    }
}
