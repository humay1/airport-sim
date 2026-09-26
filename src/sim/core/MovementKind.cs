namespace AirportSim.Sim.Core
{
    /// <summary>Whether a flight movement is an arrival or a departure. Spec: 11-interfaces-schedule.md §11.3.</summary>
    public enum MovementKind
    {
        /// <summary>The aircraft is arriving.</summary>
        Arrival,

        /// <summary>The aircraft is departing.</summary>
        Departure
    }
}
