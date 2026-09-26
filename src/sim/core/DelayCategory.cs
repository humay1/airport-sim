namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The category a delay is attributed to. Spec: 06-delay-attribution.md. The
    /// member list is fixed and extended only by spec amendment: a reorder
    /// changes every serialised or hashed value that stores the enum's ordinal.
    /// Member names are PascalCase (07-conventions.md L10, Q-028); the IDL's
    /// snake_case spelling (for example <c>"security_queue"</c>) survives only
    /// in data, where the content loader maps it to the matching member.
    /// </summary>
    public enum DelayCategory
    {
        /// <summary>The inbound aircraft was itself delayed.</summary>
        LateInbound,

        /// <summary>Runway congestion.</summary>
        RunwayCongestion,

        /// <summary>Taxiway congestion.</summary>
        TaxiCongestion,

        /// <summary>No stand was available.</summary>
        StandUnavailable,

        /// <summary>Ground handling.</summary>
        GroundHandling,

        /// <summary>Fuelling.</summary>
        Fuel,

        /// <summary>Catering.</summary>
        Catering,

        /// <summary>Cleaning.</summary>
        Cleaning,

        /// <summary>Loading.</summary>
        Loading,

        /// <summary>Pushback.</summary>
        Pushback,

        /// <summary>Crew.</summary>
        Crew,

        /// <summary>Passengers arrived late.</summary>
        PassengerLate,

        /// <summary>The security queue.</summary>
        SecurityQueue,

        /// <summary>The immigration queue.</summary>
        ImmigrationQueue,

        /// <summary>Baggage.</summary>
        Baggage,

        /// <summary>Weather.</summary>
        Weather,

        /// <summary>De-icing.</summary>
        Deicing,

        /// <summary>ATC flow control.</summary>
        AtcFlow,

        /// <summary>An incident.</summary>
        Incident,

        /// <summary>A policy constraint.</summary>
        PolicyConstraint,

        /// <summary>Delay propagated from another cause.</summary>
        Propagated
    }
}
