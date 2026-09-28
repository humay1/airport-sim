using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.Sim.Schedule
{
    /// <summary>Constructs sim.schedule's public pieces. Spec: 11-interfaces-schedule.md §11.9a.</summary>
    public static class ScheduleFactory
    {
        /// <summary>Creates the fixture loader.</summary>
        public static IScheduleLoader CreateLoader()
        {
            return new ScheduleLoader();
        }

        /// <summary>
        /// Creates the registered system over <paramref name="table"/>, resolving each row's
        /// <c>aircraft_type</c> and <c>pax_profile</c> through <c>services.Content</c>. Throws
        /// <see cref="System.FormatException"/> (message starting <c>"sim.schedule: "</c>, naming
        /// the row's <c>flight_ref</c>, the column and the unresolved id) for a resolution
        /// failure. <paramref name="flow"/> is null when <c>sim.flow</c> is not registered (§11.6).
        /// </summary>
        public static IScheduleSystem CreateSystem(in SystemServices services, in ScheduleTable table, IFlowSystem? flow)
        {
            return new ScheduleSystem(services, table, flow);
        }
    }
}
