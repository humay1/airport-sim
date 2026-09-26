namespace AirportSim.Sim.Core
{
    /// <summary>A turnaround job kind, in the fixed dependency order. Spec: 13-interfaces-turnaround.md §13.3.</summary>
    public enum JobKind
    {
        /// <summary>Deboarding passengers.</summary>
        Deboard,

        /// <summary>Unloading baggage.</summary>
        BaggageUnload,

        /// <summary>Cleaning the cabin.</summary>
        CabinClean,

        /// <summary>Catering.</summary>
        Catering,

        /// <summary>Fuelling.</summary>
        Fuel,

        /// <summary>Loading baggage.</summary>
        BaggageLoad,

        /// <summary>Pushback preparation.</summary>
        PushbackPrep,

        /// <summary>Boarding passengers.</summary>
        Boarding
    }
}
