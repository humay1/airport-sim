namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Non-generic face of a per-event-type <see cref="Channel{T}"/>, so the bus can hold
    /// them in one lookup keyed by <see cref="System.Type"/> (a keyed lookup, never iterated
    /// for ordering — 07-conventions.md "Runtime portability" rule 1).
    /// </summary>
    internal interface IChannel
    {
        void Clear();
        void Dispatch(int index, EventBus bus, in TickContext ctx);
    }
}
