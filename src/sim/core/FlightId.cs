using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// An opaque flight identifier. Spec: 08-interfaces-core.md §8.4.
    /// </summary>
    public readonly struct FlightId : IEquatable<FlightId>
    {
        /// <summary>The raw id.</summary>
        public ulong Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public FlightId(ulong value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(FlightId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is FlightId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(FlightId left, FlightId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(FlightId left, FlightId right) => !left.Equals(right);
    }
}
