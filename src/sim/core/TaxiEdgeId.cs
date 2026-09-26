using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A taxiway graph edge identifier. Spec: 12-interfaces-airside.md §12.9.
    /// </summary>
    public readonly struct TaxiEdgeId : IEquatable<TaxiEdgeId>
    {
        /// <summary>The raw id.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public TaxiEdgeId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(TaxiEdgeId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is TaxiEdgeId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(TaxiEdgeId left, TaxiEdgeId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(TaxiEdgeId left, TaxiEdgeId right) => !left.Equals(right);
    }
}
