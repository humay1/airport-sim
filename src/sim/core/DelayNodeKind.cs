namespace AirportSim.Sim.Core
{
    /// <summary>Whether a delay node is a flight's total or one allocation within it. Spec: 14-interfaces-delay.md §14.3.</summary>
    public enum DelayNodeKind
    {
        /// <summary>The flight's total delay.</summary>
        FlightTotal,

        /// <summary>One allocation of delay to a cause.</summary>
        Allocation
    }
}
