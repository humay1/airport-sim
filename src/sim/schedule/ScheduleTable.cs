using System.Collections.Generic;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// The validated, parsed fixture: rows plus the hash of the raw bytes they came
    /// from. Spec: 11-interfaces-schedule.md §11.4.
    /// </summary>
    public readonly struct ScheduleTable
    {
        /// <summary>Ascending <c>flight_ref</c>, ordinal (§11.3).</summary>
        public IReadOnlyList<FlightTemplate> Rows { get; }

        /// <summary>FNV-1a-64 over the raw file bytes.</summary>
        public ulong FixtureHash { get; }

        /// <summary>Constructs the table from its rows and fixture hash.</summary>
        public ScheduleTable(IReadOnlyList<FlightTemplate> rows, ulong fixtureHash)
        {
            Rows = rows;
            FixtureHash = fixtureHash;
        }
    }
}
