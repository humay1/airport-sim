using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// A payload-free event, never subscribed to, published only to reuse
    /// <c>IEventBus.Publish</c>'s own phase check (08-interfaces-core.md §8.6):
    /// it throws <see cref="System.InvalidOperationException"/> outside phases
    /// 1-3 of a tick. <see cref="IFlowSystem.Absorb"/> has no
    /// <c>TickContext</c> of its own to consult (it is a downward call from
    /// another system's <c>Tick</c>, §9.7), so this is the only host-wide
    /// signal available for the "outside a tick" guard (Q-033).
    /// </summary>
    internal readonly struct AbsorbPhaseGuard : ISimEvent
    {
    }
}
