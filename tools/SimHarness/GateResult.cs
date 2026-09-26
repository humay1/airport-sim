namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The outcome of one determinism gate. Spec: 19-interfaces-harness.md §19.1, §19.3
    /// (Q-026).
    /// </summary>
    public readonly struct GateResult
    {
        /// <summary>True if the gate's runs compared equal (or, for budget, stayed inside tier).</summary>
        public bool Passed { get; }

        /// <summary>Exactly the §19.3 stdout line, without its trailing newline.</summary>
        public string Report { get; }

        /// <summary>Constructs the result from its two fields.</summary>
        public GateResult(bool passed, string report)
        {
            Passed = passed;
            Report = report;
        }
    }
}
