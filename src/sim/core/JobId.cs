using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A turnaround job identifier. Spec: 13-interfaces-turnaround.md §13.3.
    /// </summary>
    public readonly struct JobId : IEquatable<JobId>
    {
        /// <summary>The raw id.</summary>
        public ulong Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public JobId(ulong value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(JobId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is JobId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(JobId left, JobId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(JobId left, JobId right) => !left.Equals(right);
    }
}
