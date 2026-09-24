using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The only entry point the presentation layer has for advancing the sim.
    /// Spec: 08-interfaces-core.md §8.5, §8.7 (Q-020).
    /// </summary>
    public interface ISimHost
    {
        ulong CurrentTick { get; }

        /// <summary>Advances exactly this many ticks, synchronously. No time argument, ever.</summary>
        void Step(uint ticks);

        /// <summary>On demand, outside checkpoints.</summary>
        ulong WorldStateHash();

        bool TrySubmit(in Command cmd, out CommandRejection reason);

        /// <summary>Every admitted command with <c>cmd.Tick &gt;= tick</c>, applied or still
        /// pending, in the total order (Tick, Issuer, Sequence).</summary>
        IReadOnlyList<Command> CommandLogSince(ulong tick);
    }
}
