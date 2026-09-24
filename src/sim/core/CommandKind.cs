namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.7. Values are saved in command logs: never renumbered.
    /// A new kind is appended with the next value, by amendment, by the task that owns it.
    /// T-001 declares only <see cref="NoOp"/> (Q-014); later tasks (e.g. T-005) append the rest.
    /// </summary>
    public enum CommandKind : ushort
    {
        NoOp = 0
    }
}
