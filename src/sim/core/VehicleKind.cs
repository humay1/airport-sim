namespace AirportSim.Sim.Core
{
    /// <summary>A ground-handling vehicle kind. Spec: 13-interfaces-turnaround.md §13.3.</summary>
    public enum VehicleKind
    {
        /// <summary>A cleaning crew.</summary>
        CleaningCrew,

        /// <summary>A catering truck.</summary>
        CateringTruck,

        /// <summary>A fuel truck.</summary>
        FuelTruck,

        /// <summary>A baggage tractor.</summary>
        BaggageTractor,

        /// <summary>A pushback tug.</summary>
        PushbackTug
    }
}
