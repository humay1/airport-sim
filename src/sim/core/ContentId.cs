using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Ordinal equality and order; hashed as its UTF-8 bytes. Spec: 08-interfaces-core.md §8.11.
    /// </summary>
    public readonly struct ContentId : IEquatable<ContentId>
    {
        public string Value { get; }

        public ContentId(string value)
        {
            Value = value;
        }

        public bool Equals(ContentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ContentId other && Equals(other);
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);
        public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
    }
}
