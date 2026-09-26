using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Total order of events within and across ticks. Spec: 08-interfaces-core.md §8.4, §8.6.
    /// </summary>
    public readonly struct EventId : IEquatable<EventId>, IComparable<EventId>
    {
        /// <summary>The tick the event was published on.</summary>
        public ulong Tick { get; }

        /// <summary>The event's position within its tick's publish order.</summary>
        public uint Sequence { get; }

        /// <summary>Constructs the id from its tick and in-tick sequence.</summary>
        public EventId(ulong tick, uint sequence)
        {
            Tick = tick;
            Sequence = sequence;
        }

        /// <summary>Equality by (Tick, Sequence).</summary>
        public bool Equals(EventId other) => Tick == other.Tick && Sequence == other.Sequence;

        /// <summary>Equality by (Tick, Sequence).</summary>
        public override bool Equals(object? obj) => obj is EventId other && Equals(other);

        /// <summary>Hash of (Tick, Sequence).</summary>
        public override int GetHashCode() => unchecked((Tick.GetHashCode() * 397) ^ Sequence.GetHashCode());

        /// <summary>Total order: by Tick, then by Sequence.</summary>
        public int CompareTo(EventId other)
        {
            int tickCompare = Tick.CompareTo(other.Tick);
            return tickCompare != 0 ? tickCompare : Sequence.CompareTo(other.Sequence);
        }

        /// <summary>Equality by (Tick, Sequence).</summary>
        public static bool operator ==(EventId left, EventId right) => left.Equals(right);

        /// <summary>Inequality by (Tick, Sequence).</summary>
        public static bool operator !=(EventId left, EventId right) => !left.Equals(right);
    }
}
