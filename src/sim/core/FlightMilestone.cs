namespace AirportSim.Sim.Core
{
    /// <summary>A flight lifecycle milestone, in the fixed spec order. Spec: 10-events.md §10.4.</summary>
    public enum FlightMilestone
    {
        /// <summary>The flight plan was published.</summary>
        PlanPublished,

        /// <summary>The inbound aircraft is airborne.</summary>
        InboundAirborne,

        /// <summary>The aircraft has landed.</summary>
        Landed,

        /// <summary>The aircraft is off the runway.</summary>
        OffRunway,

        /// <summary>The aircraft is on stand.</summary>
        OnStand,

        /// <summary>The doors are open.</summary>
        DoorsOpen,

        /// <summary>Deboarding is complete.</summary>
        DeboardComplete,

        /// <summary>The aircraft is ready to board.</summary>
        ReadyToBoard,

        /// <summary>Boarding is complete.</summary>
        BoardingComplete,

        /// <summary>The doors are closed.</summary>
        DoorsClosed,

        /// <summary>Pushback.</summary>
        Pushback,

        /// <summary>Takeoff roll.</summary>
        TakeoffRoll,

        /// <summary>Airborne.</summary>
        Airborne
    }
}
