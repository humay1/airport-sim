namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The mechanical source of a delay allocation. Spec: 14-interfaces-delay.md §14.3.
    /// <see cref="PassengerHold"/> is appended last (D6); the seven earlier ordinals never move.
    /// </summary>
    public enum DelaySource
    {
        /// <summary>The flight's total delay, not a single mechanical source.</summary>
        FlightTotal,

        /// <summary>The inbound aircraft was itself delayed.</summary>
        InboundAircraft,

        /// <summary>Held for a runway slot.</summary>
        RunwayHold,

        /// <summary>Held on a taxiway.</summary>
        TaxiwayHold,

        /// <summary>No stand was available.</summary>
        StandUnavailable,

        /// <summary>Waiting on a turnaround job.</summary>
        TurnaroundJobWait,

        /// <summary>No mechanical source could be attributed.</summary>
        Unexplained,

        /// <summary>Held for outstanding passengers (D6).</summary>
        PassengerHold
    }
}
