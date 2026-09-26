using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A world graph edge identifier. Spec: 18-interfaces-world.md §18.2.
    /// </summary>
    public readonly struct EdgeId : IEquatable<EdgeId>
    {
        /// <summary>The raw id.</summary>
        public uint Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public EdgeId(uint value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(EdgeId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is EdgeId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(EdgeId left, EdgeId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(EdgeId left, EdgeId right) => !left.Equals(right);
    }
}
