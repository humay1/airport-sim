using AirportSim.Sim.Core;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// The per-row, day-independent form of <see cref="FlightRecord"/>, produced by
    /// <see cref="IScheduleLoader"/> and consumed by <see cref="ScheduleFactory.CreateSystem"/>
    /// to materialise a day's occurrences. Spec: 11-interfaces-schedule.md §11.4. Internal
    /// in shape (07-conventions.md L5): the type itself is public only because it appears in
    /// <see cref="ScheduleTable.Rows"/>'s public signature; the spec names no field of it, so
    /// every member is <c>internal</c> and only <see cref="ScheduleLoader"/> constructs one.
    /// </summary>
    public readonly struct FlightTemplate
    {
        /// <summary>The fixture's <c>flight_ref</c>, for diagnostics only; the sim keys on <see cref="FlightId"/>.</summary>
        internal string FlightRef { get; }

        /// <summary>The row's 0-based index after an ordinal sort of <see cref="FlightRef"/> (§11.3).</summary>
        internal int RowOrdinal { get; }

        /// <summary>The day of the row's first (or only) occurrence.</summary>
        internal uint FirstDay { get; }

        /// <summary>Whether the row recurs on every day from <see cref="FirstDay"/> onward.</summary>
        internal bool RepeatDaily { get; }

        /// <summary>Whether this movement is an arrival or a departure.</summary>
        internal MovementKind Kind { get; }

        /// <summary>The operating airline.</summary>
        internal AirlineId Airline { get; }

        /// <summary>The aircraft type, a content id resolved at construction.</summary>
        internal ContentId AircraftType { get; }

        /// <summary>The scheduled minute of day, 0..1439, from <c>sched_hhmm</c>.</summary>
        internal uint MinuteOfDay { get; }

        /// <summary>The linked counterpart's <see cref="RowOrdinal"/>, or -1 when <see cref="HasRotation"/> is false.</summary>
        internal int RotationRowOrdinal { get; }

        /// <summary>Whether this row has a rotation link.</summary>
        internal bool HasRotation { get; }

        /// <summary>The minimum turnaround time.</summary>
        internal Fx MinTurnaround { get; }

        /// <summary>The passenger profile, a content id resolved at construction.</summary>
        internal ContentId PaxProfile { get; }

        /// <summary>Departing passengers; 0 for an Arrival.</summary>
        internal int PaxCount { get; }

        /// <summary>Share (0..1000) of <see cref="PaxCount"/> carrying hold baggage.</summary>
        internal int HoldBagPermille { get; }

        /// <summary>Share (0..1000) of <see cref="PaxCount"/> requiring assistance.</summary>
        internal int AssistPermille { get; }

        /// <summary>The landside Source node passengers enter at; Departures only.</summary>
        internal NodeId EntryNode { get; }

        /// <summary>Constructs the template from its members, in declared order. Only <see cref="ScheduleLoader"/> calls this.</summary>
        internal FlightTemplate(
            string flightRef,
            int rowOrdinal,
            uint firstDay,
            bool repeatDaily,
            MovementKind kind,
            AirlineId airline,
            ContentId aircraftType,
            uint minuteOfDay,
            int rotationRowOrdinal,
            bool hasRotation,
            Fx minTurnaround,
            ContentId paxProfile,
            int paxCount,
            int holdBagPermille,
            int assistPermille,
            NodeId entryNode)
        {
            FlightRef = flightRef;
            RowOrdinal = rowOrdinal;
            FirstDay = firstDay;
            RepeatDaily = repeatDaily;
            Kind = kind;
            Airline = airline;
            AircraftType = aircraftType;
            MinuteOfDay = minuteOfDay;
            RotationRowOrdinal = rotationRowOrdinal;
            HasRotation = hasRotation;
            MinTurnaround = minTurnaround;
            PaxProfile = paxProfile;
            PaxCount = paxCount;
            HoldBagPermille = holdBagPermille;
            AssistPermille = assistPermille;
            EntryNode = entryNode;
        }
    }
}
