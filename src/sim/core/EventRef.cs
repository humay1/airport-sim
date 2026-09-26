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

        /// <summary>The causing event's id. Meaningful only when <see cref="HasValue"/> is true.</summary>
        public EventId Id { get; }

        /// <summary>True if this reference names a causing event; false for <see cref="None"/>.</summary>
        public bool HasValue { get; }

        /// <summary>Constructs the reference from its id and presence flag.</summary>
        public EventRef(EventId id, bool hasValue)
        {
            Id = id;
            HasValue = hasValue;
        }
    }
}
