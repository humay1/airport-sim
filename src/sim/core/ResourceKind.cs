namespace AirportSim.Sim.Core
{
    /// <summary>What kind of resource a blocked turnaround job is waiting on. Spec: 13-interfaces-turnaround.md §13.3.</summary>
    public enum ResourceKind
    {
        /// <summary>Waiting on a vehicle.</summary>
        Vehicle,

        /// <summary>Waiting on another job it depends on.</summary>
        JobDependency,

        /// <summary>Waiting on crew.</summary>
        Crew
    }
}
