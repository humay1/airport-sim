using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// An opaque entity identifier. Spec: 08-interfaces-core.md §8.4.
    /// </summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        /// <summary>The raw id: <c>(owner &lt;&lt; 48) | counter</c>.</summary>
        public ulong Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public EntityId(ulong value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(EntityId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
    }
}
