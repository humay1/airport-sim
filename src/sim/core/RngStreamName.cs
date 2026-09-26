namespace AirportSim.Sim.Core
{
    /// <summary>
    /// <c>"&lt;module&gt;.&lt;purpose&gt;"</c>, declared as constants by the owning module.
    /// Ordinal equality. Spec: 08-interfaces-core.md §8.8 (Q-014). Shape only at T-001;
    /// T-002 gives the RNG service behaviour.
    /// </summary>
    public readonly struct RngStreamName
    {
        /// <summary>The stream's dotted name.</summary>
        public string Value { get; }

        /// <summary>Constructs the name from its dotted string.</summary>
        public RngStreamName(string value)
        {
            Value = value;
        }
    }
}
