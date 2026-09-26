namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The only entry point the presentation layer has for advancing the sim.
    /// Spec: 08-interfaces-core.md §8.5. <c>CommandLogSince</c> (§8.7, Q-020) is not
    /// declared here: it is T-005's addition to this interface.
    /// </summary>
    public interface ISimHost
    {
        /// <summary>The tick that will execute next.</summary>
        ulong CurrentTick { get; }

        /// <summary>Advances exactly this many ticks, synchronously. No time argument, ever.</summary>
        void Step(uint ticks);

        /// <summary>On demand, outside checkpoints.</summary>
        ulong WorldStateHash();

        /// <summary>Attempts to submit a command; false with the rejection reason if inadmissible.</summary>
        bool TrySubmit(in Command cmd, out CommandRejection reason);
    }
}
