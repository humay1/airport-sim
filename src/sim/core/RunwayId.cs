using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A runway identifier. Spec: 12-interfaces-airside.md §12.4.
    /// </summary>
    public readonly struct RunwayId : IEquatable<RunwayId>
    {
        /// <summary>The raw id.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public RunwayId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(RunwayId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is RunwayId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(RunwayId left, RunwayId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(RunwayId left, RunwayId right) => !left.Equals(right);
    }
}
