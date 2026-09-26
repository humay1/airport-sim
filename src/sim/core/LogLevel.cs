namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.10.</summary>
    public enum LogLevel : byte
    {
        /// <summary>Diagnostic detail, not expected in normal operation.</summary>
        Debug = 0,

        /// <summary>Routine progress.</summary>
        Info = 1,

        /// <summary>Recoverable but noteworthy condition.</summary>
        Warning = 2,

        /// <summary>A broken invariant or unrecoverable condition.</summary>
        Error = 3
    }
}
