using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The host's internal seam over command admission, application and the log. Spec:
    /// 08-interfaces-core.md §8.7 ("Queue semantics", Q-020) — declared <c>internal</c>, an
    /// exception to 07-conventions.md L5. Tests, the harness and sim.save reach it only
    /// through <see cref="ISimHost.CommandLogSince"/>.
    /// </summary>
    internal interface ICommandQueue
    {
        /// <summary>Admission: TooLate, then NotPermitted, then UnknownKind, then the kind's check.</summary>
        bool TrySubmit(in Command cmd, out CommandRejection reason);

        /// <summary>Applies every command due at this tick, phase 1 only, in (Tick, Issuer, Sequence) order.</summary>
        void ApplyDue(ulong tick);

        /// <summary>Every admitted command with cmd.Tick &gt;= tick, applied or still pending, in total order.</summary>
        IReadOnlyList<Command> LogSince(ulong tick);
    }
}
