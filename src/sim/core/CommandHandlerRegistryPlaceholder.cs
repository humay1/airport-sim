using System;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A placeholder <see cref="ICommandHandlerRegistry"/>. T-005 gives command
    /// registration, admission and dispatch their real behaviour (08-interfaces-core.md
    /// §8.7, Q-020). Shape only at T-001 (Q-014); nothing in T-001's own fixture registers
    /// a handler, since <see cref="ISimHost.TrySubmit"/> always rejects with
    /// <see cref="CommandRejection.UnknownKind"/>.
    /// </summary>
    internal sealed class CommandHandlerRegistryPlaceholder : ICommandHandlerRegistry
    {
        public void Register(SystemId owner, ICommandHandler handler)
        {
            throw new InvalidOperationException(
                "ICommandHandlerRegistry is a T-001 placeholder; T-005 gives it real behaviour (08-interfaces-core.md §8.7).");
        }
    }
}
