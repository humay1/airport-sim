namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A registered module system, ticked once per tick in registry order. Spec: 08-interfaces-core.md §8.5.
    /// </summary>
    public interface ISimSystem
    {
        SystemId Id { get; }

        /// <summary>Stable, matches the module name.</summary>
        string Name { get; }

        void Tick(in TickContext ctx);

        ulong ComputeStateHash();
    }
}
