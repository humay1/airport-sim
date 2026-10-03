using System.Collections.Generic;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>The job catalogue: exactly one entry per JobKind (13-interfaces-turnaround.md §13.4).</summary>
    public readonly struct TurnaroundCatalogue
    {
        /// <summary>The entries.</summary>
        public IReadOnlyList<JobDef> Jobs { get; }

        /// <summary>Creates a catalogue over the given entries.</summary>
        public TurnaroundCatalogue(IReadOnlyList<JobDef> jobs)
        {
            Jobs = jobs;
        }
    }
}
