namespace AirportSim.Sim.Core
{
    /// <summary>A flight plan was published. Spec: 10-events.md §10.9.</summary>
    public readonly struct FlightPlanPublished : ISimEvent
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>Whether this is an arrival or a departure.</summary>
        public MovementKind Kind { get; }

        /// <summary>The rotation's other flight, or <see cref="Flight"/> itself if <see cref="HasRotation"/> is false.</summary>
        public FlightId Rotation { get; }

        /// <summary>Whether this flight has a rotation.</summary>
        public bool HasRotation { get; }

        /// <summary>The operating airline.</summary>
        public AirlineId Airline { get; }

        /// <summary>The aircraft type.</summary>
        public ContentId AircraftType { get; }

        /// <summary>The scheduled arrival tick.</summary>
        public ulong SchedArr { get; }

        /// <summary>The scheduled departure tick.</summary>
        public ulong SchedDep { get; }

        /// <summary>The minimum turnaround time.</summary>
        public Fx MinTurnaround { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public FlightPlanPublished(
            FlightId flight,
            MovementKind kind,
            FlightId rotation,
            bool hasRotation,
            AirlineId airline,
            ContentId aircraftType,
            ulong schedArr,
            ulong schedDep,
            Fx minTurnaround)
        {
            Flight = flight;
            Kind = kind;
            Rotation = rotation;
            HasRotation = hasRotation;
            Airline = airline;
            AircraftType = aircraftType;
            SchedArr = schedArr;
            SchedDep = schedDep;
            MinTurnaround = minTurnaround;
        }
    }
}
