namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Publishes events onto the tick's queue. Spec: 08-interfaces-core.md §8.6.
    /// </summary>
    public interface IEventPublisher
    {
        /// <summary>
        /// Queues <paramref name="evt"/> for dispatch; never calls a handler directly.
        /// <paramref name="cause"/> is the event that caused this one, or <see cref="EventRef.None"/> for a root event.
        /// </summary>
        EventId Publish<T>(in T evt, in EventRef cause) where T : struct, ISimEvent;
    }
}
