namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7 ("Dispatch", Q-010).</summary>
    public interface ICommandHandlerRegistry
    {
        /// <summary>Registers a handler for its command kind, owned by the given system.</summary>
        void Register(SystemId owner, ICommandHandler handler);
    }
}
