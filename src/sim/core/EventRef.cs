namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A reference to the event that caused another event, or none for a root event.
    /// Spec: 10-events.md §10.2.
    /// </summary>
    public readonly struct EventRef
    {
        /// <summary>The root-event value: no cause.</summary>
        public static readonly EventRef None = new EventRef(default, false);

        public EventId Id { get; }
        public bool HasValue { get; }

        public EventRef(EventId id, bool hasValue)
        {
            Id = id;
            HasValue = hasValue;
        }
    }
}
