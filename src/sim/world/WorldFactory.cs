using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>Constructs sim.world's public pieces. Spec: 18-interfaces-world.md §18.4.</summary>
    public static class WorldFactory
    {
        /// <summary>Creates the fixture loader.</summary>
        public static IWalkGraphLoader CreateGraphLoader()
        {
            return new WalkGraphLoader();
        }

        /// <summary>
        /// Creates the registered system over the given graph. <paramref name="services"/>
        /// is unused: sim.world emits no events, consumes no commands, allocates no ids
        /// and draws no RNG.
        /// </summary>
        public static IWorldSystem CreateSystem(in SystemServices services, in WalkGraph graph)
        {
            return new WorldSystem(graph);
        }
    }
}
