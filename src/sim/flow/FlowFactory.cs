using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow
{
    /// <summary>Constructs sim.flow's public pieces. Spec: 09-interfaces-flow.md §9.11.</summary>
    public static class FlowFactory
    {
        /// <summary>Creates the flow graph file loader.</summary>
        public static IFlowGraphLoader CreateGraphLoader()
        {
            return new FlowGraphLoader();
        }

        /// <summary>
        /// Creates the registered system over <paramref name="graph"/>, resolving each
        /// queue node's profile through <c>services.Content</c> and registering the
        /// <c>SetServersOpen</c> command handler. Throws <see cref="System.FormatException"/>
        /// (message starting <c>"sim.flow: "</c>) for an unresolved queue profile.
        /// </summary>
        public static IFlowSystem CreateSystem(in SystemServices services, in FlowGraph graph, IWorldSystem world)
        {
            return new FlowSystem(services, graph, world);
        }
    }
}
