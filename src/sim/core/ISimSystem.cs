namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A registered module system, ticked once per tick in registry order. Spec: 08-interfaces-core.md §8.5.
    /// </summary>
    public interface ISimSystem
    {
        /// <summary>The system's registry position.</summary>
        SystemId Id { get; }

        /// <summary>Stable, matches the module name.</summary>
        string Name { get; }

        /// <summary>Advances the system by one tick (phase 2 of the loop, in registry order).</summary>
        void Tick(in TickContext ctx);

        /// <summary>Hashes the system's own serialisable state for the world hash.</summary>
        ulong ComputeStateHash();
    }
}
