using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A passenger cohort identifier, allocated by sim.flow. Spec: 09-interfaces-flow.md.
    /// </summary>
    public readonly struct CohortId : IEquatable<CohortId>
    {
        /// <summary>The raw id.</summary>
        public ulong Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public CohortId(ulong value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(CohortId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is CohortId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(CohortId left, CohortId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(CohortId left, CohortId right) => !left.Equals(right);
    }
}
