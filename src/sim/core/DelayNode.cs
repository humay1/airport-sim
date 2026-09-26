namespace AirportSim.Sim.Core
{
    /// <summary>
    /// One node of a flight's delay attribution tree: either its total delay or one
    /// allocation within it. Spec: 14-interfaces-delay.md §14.3. Relocated to sim.core
    /// (Q-018) so that every emitting module can reference it without depending on
    /// sim.delay. <see cref="Minutes"/> is derived and never hashed.
    /// </summary>
    public readonly struct DelayNode
    {
        /// <summary>This node's id, allocated by sim.delay; 0 means none.</summary>
        public DelayEventId Id { get; }

        /// <summary>Whether this is the flight's total or one allocation.</summary>
        public DelayNodeKind Kind { get; }

        /// <summary>The flight this delay is attributed to.</summary>
        public FlightId Subject { get; }

        /// <summary>The parent node's id in the attribution tree.</summary>
        public DelayEventId Parent { get; }

        /// <summary>The delay category, absent for a node with no category (the flight total).</summary>
        public DelayCategory? Category { get; }

        /// <summary>The delay, in ticks.</summary>
        public ulong Ticks { get; }

        /// <summary>The delay, in sim-minutes. Derived from <see cref="Ticks"/>; never hashed.</summary>
        public Fx Minutes { get; }

        /// <summary>Whether this node is a root cause rather than a propagated allocation.</summary>
        public bool RootCause { get; }

        /// <summary>A linked flight this delay propagated from or to, or a default value if none.</summary>
        public FlightId LinkedFlight { get; }

        /// <summary>The event that caused this node to be recorded.</summary>
        public EventRef SourceEvent { get; }

        /// <summary>The mechanical explanation for this allocation.</summary>
        public DelayExplanation Explanation { get; }

        /// <summary>The tick this node was created at.</summary>
        public ulong CreatedAt { get; }

        /// <summary>Constructs the node from its members, in declared order.</summary>
        public DelayNode(
            DelayEventId id,
            DelayNodeKind kind,
            FlightId subject,
            DelayEventId parent,
            DelayCategory? category,
            ulong ticks,
            Fx minutes,
            bool rootCause,
            FlightId linkedFlight,
            EventRef sourceEvent,
            DelayExplanation explanation,
            ulong createdAt)
        {
            Id = id;
            Kind = kind;
            Subject = subject;
            Parent = parent;
            Category = category;
            Ticks = ticks;
            Minutes = minutes;
            RootCause = rootCause;
            LinkedFlight = linkedFlight;
            SourceEvent = sourceEvent;
            Explanation = explanation;
            CreatedAt = createdAt;
        }
    }
}
