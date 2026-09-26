namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11a.</summary>
    public interface ISimHostBuilder
    {
        /// <summary>The downward services available to systems during construction.</summary>
        SystemServices Services { get; }

        /// <summary>Strictly ascending registry position (§8.5).</summary>
        void Register(ISimSystem system);

        /// <summary>Callable once. The builder cannot be reused afterwards.</summary>
        ISimHost Build();
    }
}
