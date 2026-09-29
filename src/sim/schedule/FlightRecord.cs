using AirportSim.Sim.Core;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// A flight's immutable-for-the-session record, day-materialised from its
    /// fixture row. Spec: 11-interfaces-schedule.md §11.3.
    /// </summary>
    public readonly struct FlightRecord
    {
        /// <summary>The derived id: DayIndex * FLIGHT_ID_DAY_STRIDE + RowOrdinal + 1 (§11.3).</summary>
        public FlightId Id { get; }

        /// <summary>The operating airline, FNV-1a-32 of the fixture's airline code (§11.4).</summary>
        public AirlineId Airline { get; }

        /// <summary>The aircraft type, resolved through content at construction.</summary>
        public ContentId AircraftType { get; }

        /// <summary>Whether this movement is an arrival or a departure.</summary>
        public MovementKind Kind { get; }

        /// <summary>The day this occurrence was materialised for.</summary>
        public uint DayIndex { get; }

        /// <summary>STA for an Arrival, STD for a Departure.</summary>
        public ulong ScheduledTick { get; }

        /// <summary>ScheduledTick - PLAN_PUBLISH_LEAD_TICKS, clamped to 0 (§11.5).</summary>
        public ulong PublishTick { get; }

        /// <summary>The linked counterpart's id, or this flight's own id if <see cref="HasRotation"/> is false.</summary>
        public FlightId Rotation { get; }

        /// <summary>Whether this flight has a rotation link.</summary>
        public bool HasRotation { get; }

        /// <summary>The minimum turnaround time.</summary>
        public Fx MinTurnaround { get; }

        /// <summary>The passenger profile, resolved through content at construction.</summary>
        public ContentId PaxProfile { get; }

        /// <summary>Departing passengers; 0 for an Arrival at Phase 0 (§11.1).</summary>
        public int PaxCount { get; }

        /// <summary>Share (0..1000) of <see cref="PaxCount"/> carrying hold baggage.</summary>
        public int HoldBagPermille { get; }

        /// <summary>Share (0..1000) of <see cref="PaxCount"/> requiring assistance.</summary>
        public int AssistPermille { get; }

        /// <summary>The landside Source node passengers enter at; Departures only.</summary>
        public NodeId EntryNode { get; }

        /// <summary>Constructs the record from its members, in declared order.</summary>
        public FlightRecord(
            FlightId id,
            AirlineId airline,
            ContentId aircraftType,
            MovementKind kind,
            uint dayIndex,
            ulong scheduledTick,
            ulong publishTick,
            FlightId rotation,
            bool hasRotation,
            Fx minTurnaround,
            ContentId paxProfile,
            int paxCount,
            int holdBagPermille,
            int assistPermille,
            NodeId entryNode)
        {
            Id = id;
            Airline = airline;
            AircraftType = aircraftType;
            Kind = kind;
            DayIndex = dayIndex;
            ScheduledTick = scheduledTick;
            PublishTick = publishTick;
            Rotation = rotation;
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
