using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// <c>"&lt;module&gt;.&lt;purpose&gt;"</c>, declared as constants by the owning module.
    /// Ordinal equality. Spec: 08-interfaces-core.md §8.8 (Q-014, Q-019, Q-023). The
    /// whole value must match <c>sim\.[a-z]+\.[a-z0-9_]+</c>, anchored at both ends
    /// (<c>\A…\z</c>).
    /// </summary>
    public readonly struct RngStreamName : IEquatable<RngStreamName>
    {
        /// <summary>The stream's dotted name.</summary>
        public string Value { get; }

        /// <summary>
        /// Constructs the name from its dotted string. Throws
        /// <see cref="ArgumentNullException"/> if <paramref name="value"/> is null, or
        /// <see cref="ArgumentException"/> if it does not match
        /// <c>sim\.[a-z]+\.[a-z0-9_]+</c> exactly (08-interfaces-core.md §8.8 "Names").
        /// </summary>
        public RngStreamName(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (!IsWellFormed(value))
            {
                throw new ArgumentException(
                    $"RngStreamName: '{value}' does not match sim.<module>.<purpose> (08-interfaces-core.md §8.8).",
                    nameof(value));
            }

            Value = value;
        }

        // sim\.[a-z]+\.[a-z0-9_]+ anchored at both ends. Hand-rolled rather than
        // System.Text.RegularExpressions: the grammar is simple enough that a
        // regex buys nothing but an extra dependency and a culture footgun.
        private static bool IsWellFormed(string value)
        {
            const string prefix = "sim.";
            int length = value.Length;
            if (length <= prefix.Length || !value.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            int i = prefix.Length;
            int moduleStart = i;
            while (i < length && value[i] >= 'a' && value[i] <= 'z')
            {
                i++;
            }

            if (i == moduleStart || i >= length || value[i] != '.')
            {
                return false;
            }

            i++; // past the middle '.'
            int purposeStart = i;
            while (i < length && IsPurposeChar(value[i]))
            {
                i++;
            }

            return i == length && i > purposeStart;
        }

        private static bool IsPurposeChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
        }

        /// <summary>Equality by <see cref="Value"/>, ordinal. spec/08-interfaces-core.md §8.8 (Q-014).</summary>
        public bool Equals(RngStreamName other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        /// <summary>Equality by <see cref="Value"/>, ordinal.</summary>
        public override bool Equals(object? obj) => obj is RngStreamName other && Equals(other);

        /// <summary>Hash of <see cref="Value"/>, ordinal.</summary>
        public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        /// <summary>Same as <see cref="Equals(RngStreamName)"/>.</summary>
        public static bool operator ==(RngStreamName a, RngStreamName b) => a.Equals(b);

        /// <summary>Same as the negation of <see cref="Equals(RngStreamName)"/>.</summary>
        public static bool operator !=(RngStreamName a, RngStreamName b) => !a.Equals(b);
    }
}
