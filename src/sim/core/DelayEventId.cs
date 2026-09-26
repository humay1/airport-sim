using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A delay-node identifier, allocated by sim.delay at runtime; 0 means none. Spec: 14-interfaces-delay.md §14.3.
    /// </summary>
    public readonly struct DelayEventId : IEquatable<DelayEventId>
    {
        /// <summary>The raw id.</summary>
        public ulong Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public DelayEventId(ulong value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(DelayEventId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is DelayEventId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(DelayEventId left, DelayEventId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(DelayEventId left, DelayEventId right) => !left.Equals(right);
    }
}
