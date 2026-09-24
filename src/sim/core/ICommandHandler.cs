using System;

namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.7. Shape only at T-001; T-005 gives it behaviour.</summary>
    public interface ICommandHandler
    {
        CommandKind Kind { get; }
        CommandRejection Validate(ReadOnlySpan<byte> payload);
        void Apply(in Command cmd, in TickContext ctx);
    }
}
