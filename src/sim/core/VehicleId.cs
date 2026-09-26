using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A ground-handling vehicle identifier. Spec: 13-interfaces-turnaround.md §13.3.
    /// </summary>
    public readonly struct VehicleId : IEquatable<VehicleId>
    {
        /// <summary>The raw id.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public VehicleId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(VehicleId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is VehicleId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(VehicleId left, VehicleId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(VehicleId left, VehicleId right) => !left.Equals(right);
    }
}
