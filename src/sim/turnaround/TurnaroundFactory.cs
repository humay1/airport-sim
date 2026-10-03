using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>Constructs sim.turnaround (13-interfaces-turnaround.md §13.10a).</summary>
    public static class TurnaroundFactory
    {
        /// <summary>Creates the setup loader.</summary>
        public static ITurnaroundSetupLoader CreateSetupLoader()
        {
            return new TurnaroundSetupLoader();
        }

        /// <summary>
        /// Creates the system. Validates the setup as the loader does (§13.4)
        /// and subscribes to FlightMilestoneReached on the services' bus.
        /// </summary>
        public static ITurnaroundSystem CreateSystem(in SystemServices services, in TurnaroundSetup setup, IScheduleSystem schedule)
        {
            return new TurnaroundSystem(services, setup, schedule);
        }
    }
}
