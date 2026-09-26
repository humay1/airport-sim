using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A world graph node identifier. Spec: 18-interfaces-world.md §18.2.
    /// </summary>
    public readonly struct NodeId : IEquatable<NodeId>
    {
        /// <summary>The raw id.</summary>
        public uint Value { get; }

        /// <summary>Constructs the id from its raw value.</summary>
        public NodeId(uint value)
        {
            Value = value;
        }

        /// <summary>Equality by Value.</summary>
        public bool Equals(NodeId other) => Value == other.Value;

        /// <summary>Equality by Value.</summary>
        public override bool Equals(object? obj) => obj is NodeId other && Equals(other);

        /// <summary>Hash of Value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Equality by Value.</summary>
        public static bool operator ==(NodeId left, NodeId right) => left.Equals(right);

        /// <summary>Inequality by Value.</summary>
        public static bool operator !=(NodeId left, NodeId right) => !left.Equals(right);
    }
}
