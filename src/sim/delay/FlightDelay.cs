using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    /// <summary>The per-flight delay record, as queried (14-interfaces-delay.md §14.3).</summary>
    public readonly struct FlightDelay
    {
        /// <summary>The flight.</summary>
        public FlightId Flight { get; }

        /// <summary>Arrival or departure.</summary>
        public MovementKind Kind { get; }

        /// <summary>True if the flight has a rotation partner.</summary>
        public bool HasRotation { get; }

        /// <summary>The rotation partner; the flight itself if there is none.</summary>
        public FlightId Rotation { get; }

        /// <summary>The flight's FlightTotal node.</summary>
        public DelayEventId Root { get; }

        /// <summary>The delay clock: lateness at the last checkpoint, in ticks.</summary>
        public ulong TotalTicks { get; }

        /// <summary>TotalTicks in sim minutes; derived.</summary>
        public Fx TotalMinutes { get; }

        /// <summary>How many checkpoints the flight has passed.</summary>
        public int CheckpointsReached { get; }

        /// <summary>The actual tick of the last checkpoint; the publication tick until the first.</summary>
        public ulong LastCheckpointActual { get; }

        /// <summary>True once the terminal checkpoint is reached.</summary>
        public bool Finalised { get; }

        /// <summary>The tick of finalisation, or TICK_UNSCHEDULED until then.</summary>
        public ulong FinalisedAt { get; }

        /// <summary>Passengers recorded as having missed the flight (14 §14.9); not delay minutes.</summary>
        public int MissedPassengers { get; }

        /// <summary>The lastBlockedAt of the first PassengersMissedFlight, if any.</summary>
        public NodeId? MissedLastBlockedAt { get; }

        /// <summary>Constructs the record from its thirteen members.</summary>
        public FlightDelay(
            FlightId flight,
            MovementKind kind,
            bool hasRotation,
            FlightId rotation,
            DelayEventId root,
            ulong totalTicks,
            Fx totalMinutes,
            int checkpointsReached,
            ulong lastCheckpointActual,
            bool finalised,
            ulong finalisedAt,
            int missedPassengers,
            NodeId? missedLastBlockedAt)
        {
            Flight = flight;
            Kind = kind;
            HasRotation = hasRotation;
            Rotation = rotation;
            Root = root;
            TotalTicks = totalTicks;
            TotalMinutes = totalMinutes;
            CheckpointsReached = checkpointsReached;
            LastCheckpointActual = lastCheckpointActual;
            Finalised = finalised;
            FinalisedAt = finalisedAt;
            MissedPassengers = missedPassengers;
            MissedLastBlockedAt = missedLastBlockedAt;
        }
    }
}
