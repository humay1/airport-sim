using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.7. <see cref="SimConstants.PLAYER_LOCAL"/> is the only
    /// player at Phase 1.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        /// <summary>The player's ordinal value.</summary>
        public ushort Value { get; }

        /// <summary>Constructs the id from its ordinal value.</summary>
        public PlayerId(ushort value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(PlayerId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }
}
