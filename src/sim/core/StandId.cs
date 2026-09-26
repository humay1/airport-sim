using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A stand identifier. Spec: 12-interfaces-airside.md §12.4.
    /// </summary>
    public readonly struct StandId : IEquatable<StandId>
    {
        /// <summary>The raw id.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public StandId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(StandId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is StandId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(StandId left, StandId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(StandId left, StandId right) => !left.Equals(right);
    }
}
