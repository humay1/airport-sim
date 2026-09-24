using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.7. <see cref="SimConstants.PLAYER_LOCAL"/> is the only
    /// player at Phase 1.
    /// </summary>
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public ushort Value { get; }

        public PlayerId(ushort value)
        {
            Value = value;
        }

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(PlayerId left, PlayerId right) => left.Equals(right);
        public static bool operator !=(PlayerId left, PlayerId right) => !left.Equals(right);
    }
}
