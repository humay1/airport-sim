using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// One catalogue entry (13-interfaces-turnaround.md §13.4): what a
    /// <see cref="JobKind"/> needs and how long it nominally takes.
    /// </summary>
    public readonly struct JobDef
    {
        /// <summary>The job kind this entry defines.</summary>
        public JobKind Kind { get; }

        /// <summary>The vehicle kind the job needs, or null when it needs none (Deboard, Boarding).</summary>
        public VehicleKind? RequiresVehicle { get; }

        /// <summary>Ticks from start to completion. At least 1 (§13.4).</summary>
        public uint NominalDurationTicks { get; }

        /// <summary>The delay category copied onto the job's blocking events (§13.9).</summary>
        public DelayCategory Category { get; }

        /// <summary>Creates a catalogue entry.</summary>
        public JobDef(JobKind kind, VehicleKind? requiresVehicle, uint nominalDurationTicks, DelayCategory category)
        {
            Kind = kind;
            RequiresVehicle = requiresVehicle;
            NominalDurationTicks = nominalDurationTicks;
            Category = category;
        }
    }
}
