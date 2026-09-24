using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The registry position of a system, 1-14 (8 reserved), or 0 for <see cref="SimConstants.SYSTEM_CORE"/>.
    /// Spec: 08-interfaces-core.md §8.4, §8.5.
    /// </summary>
    public readonly struct SystemId : IEquatable<SystemId>
    {
        public ushort Value { get; }

        public SystemId(ushort value)
        {
            Value = value;
        }

        public bool Equals(SystemId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is SystemId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(SystemId left, SystemId right) => left.Equals(right);
        public static bool operator !=(SystemId left, SystemId right) => !left.Equals(right);
    }
}
