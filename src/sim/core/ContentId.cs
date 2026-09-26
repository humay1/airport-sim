using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Ordinal equality and order; hashed as its UTF-8 bytes. Spec: 08-interfaces-core.md §8.11.
    /// </summary>
    public readonly struct ContentId : IEquatable<ContentId>
    {
        /// <summary>The content's stable string id.</summary>
        public string Value { get; }

        /// <summary>Constructs the id from its stable string value.</summary>
        public ContentId(string value)
        {
            Value = value;
        }

        /// <summary>Ordinal equality by Value.</summary>
        public bool Equals(ContentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>Ordinal equality by Value.</summary>
        public override bool Equals(object? obj) => obj is ContentId other && Equals(other);

        /// <summary>Ordinal hash of Value.</summary>
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        /// <summary>Ordinal equality by Value.</summary>
        public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

        /// <summary>Ordinal inequality by Value.</summary>
        public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
    }
}
