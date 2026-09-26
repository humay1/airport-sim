using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A taxiway graph node identifier. Spec: 12-interfaces-airside.md §12.9.
    /// </summary>
    public readonly struct TaxiNodeId : IEquatable<TaxiNodeId>
    {
        /// <summary>The raw id.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public TaxiNodeId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(TaxiNodeId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is TaxiNodeId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(TaxiNodeId left, TaxiNodeId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(TaxiNodeId left, TaxiNodeId right) => !left.Equals(right);
    }
}
