namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.4. Shape only at T-001 — counter behaviour has no
    /// owning task per Q-014.
    /// </summary>
    public interface IIdAllocator
    {
        /// <summary>Allocates the next entity id owned by the given system.</summary>
        EntityId Next(SystemId owner);
    }
}
