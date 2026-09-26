namespace AirportSim.Sim.Core
{
    /// <summary>A turnaround job's lifecycle status. Spec: 13-interfaces-turnaround.md §13.3.</summary>
    public enum JobStatus
    {
        /// <summary>Waiting on a resource or a dependency.</summary>
        Blocked,

        /// <summary>Running.</summary>
        Active,

        /// <summary>Finished.</summary>
        Completed
    }
}
