using System;

namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7. Shape only at T-001; T-005 gives it behaviour.</summary>
    public interface ICommandHandler
    {
        /// <summary>The command kind this handler admits.</summary>
        CommandKind Kind { get; }

        /// <summary>Validates a command's payload before admission.</summary>
        CommandRejection Validate(ReadOnlySpan<byte> payload);

        /// <summary>Applies an admitted command during phase 1 of a tick.</summary>
        void Apply(in Command cmd, in TickContext ctx);
    }
}
