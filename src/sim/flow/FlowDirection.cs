namespace AirportSim.Sim.Flow
{
    /// <summary>A cohort's journey direction. Spec: 09-interfaces-flow.md §9.2.</summary>
    public enum FlowDirection
    {
        /// <summary>Heading to a gate to board.</summary>
        Departing,

        /// <summary>Arriving from a gate.</summary>
        Arriving,

        /// <summary>Transferring between flights.</summary>
        Transferring
    }
}
