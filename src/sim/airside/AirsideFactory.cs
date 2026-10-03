using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Airside
{
    /// <summary>Constructs the airside module. Spec: 12-interfaces-airside.md §12.12a.</summary>
    public static class AirsideFactory
    {
        /// <summary>Creates the layout loader.</summary>
        public static IAirsideLayoutLoader CreateLayoutLoader()
        {
            return new LayoutLoader();
        }

        /// <summary>
        /// Creates the system. <paramref name="layout"/> must come from the loader.
        /// <paramref name="flow"/> null disables Absorb and the boarding hold.
        /// <paramref name="turnaroundRegistered"/> selects the event handshake over the fallback.
        /// </summary>
        public static IAirsideSystem CreateSystem(
            in SystemServices services,
            in AirsideLayout layout,
            in AirsideRules rules,
            IScheduleSystem schedule,
            IFlowSystem? flow,
            bool turnaroundRegistered)
        {
            return new AirsideSystem(services, layout, rules, schedule, flow, turnaroundRegistered);
        }
    }
}
