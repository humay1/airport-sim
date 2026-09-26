namespace AirportSim.Sim.Core
{
    /// <summary>
    /// A subscriber's handler for one event type. Named <c>SimEventHandler</c>, not
    /// <c>EventHandler</c>, because that collides with <see cref="System.EventHandler{TEventArgs}"/>.
    /// Spec: 08-interfaces-core.md §8.6.
    /// </summary>
    public delegate void SimEventHandler<T>(in EventEnvelope envelope, in T evt, in TickContext ctx)
        where T : struct, ISimEvent;
}
