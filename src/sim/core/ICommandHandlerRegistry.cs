namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7. Shape only at T-001; T-005 gives it behaviour.</summary>
    public interface ICommandHandlerRegistry
    {
        /// <summary>Registers a handler for its command kind, owned by the given system.</summary>
        void Register(SystemId owner, ICommandHandler handler);
    }
}
