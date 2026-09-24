using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// An opaque entity identifier. Spec: 08-interfaces-core.md §8.4.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public ulong Value { get; }

        public EntityId(ulong value)
        {
            Value = value;
        }

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
