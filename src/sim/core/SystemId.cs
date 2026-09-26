using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The registry position of a system, 1-14 (8 reserved), or 0 for <see cref="SimConstants.SYSTEM_CORE"/>.
    /// Spec: 08-interfaces-core.md §8.4, §8.5.
    /// </summary>
    public readonly struct SystemId : IEquatable<SystemId>
    {
        /// <summary>The registry position.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its registry position.</summary>
        public SystemId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(SystemId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is SystemId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(SystemId left, SystemId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(SystemId left, SystemId right) => !left.Equals(right);
    }
}
