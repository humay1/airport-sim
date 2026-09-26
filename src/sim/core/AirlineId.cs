using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// An airline identifier. Spec: 11-interfaces-schedule.md §11.3.
    /// </summary>
    public readonly struct AirlineId : IEquatable<AirlineId>
    {
        /// <summary>The raw id.</summary>
        public uint Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public AirlineId(uint value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(AirlineId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is AirlineId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(AirlineId left, AirlineId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(AirlineId left, AirlineId right) => !left.Equals(right);
    }
}
