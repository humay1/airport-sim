using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Total order of events within and across ticks. Spec: 08-interfaces-core.md §8.4, §8.6.
    /// </summary>
    public readonly struct EventId : IEquatable<EventId>, IComparable<EventId>
    {
        public ulong Tick { get; }
        public uint Sequence { get; }

        public EventId(ulong tick, uint sequence)
        {
            Tick = tick;
            Sequence = sequence;
        }

        public bool Equals(EventId other) => Tick == other.Tick && Sequence == other.Sequence;
        public override bool Equals(object? obj) => obj is EventId other && Equals(other);
        public override int GetHashCode() => unchecked((Tick.GetHashCode() * 397) ^ Sequence.GetHashCode());

        public int CompareTo(EventId other)
        {
            int tickCompare = Tick.CompareTo(other.Tick);
            return tickCompare != 0 ? tickCompare : Sequence.CompareTo(other.Sequence);
        }

        public static bool operator ==(EventId left, EventId right) => left.Equals(right);
        public static bool operator !=(EventId left, EventId right) => !left.Equals(right);
    }
}
