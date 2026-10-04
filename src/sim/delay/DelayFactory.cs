using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    /// <summary>Constructs sim.delay (14-interfaces-delay.md §14.13a).</summary>
    public static class DelayFactory
    {
        /// <summary>Creates the system and subscribes to the consumed events (§14.12) on the services' bus.</summary>
        public static IDelaySystem CreateSystem(in SystemServices services)
        {
            return new DelaySystem(services);
        }
    }
}
