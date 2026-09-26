namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The event transport: publish plus subscribe. Spec: 08-interfaces-core.md §8.6.
    /// </summary>
    public interface IEventBus : IEventPublisher
    {
        /// <summary>
        /// Registers <paramref name="handler"/> for <paramref name="subscriber"/>. One handler per
        /// (subscriber, T). Construction-time only; throws after <c>Build</c>.
        /// </summary>
        void Subscribe<T>(SystemId subscriber, SimEventHandler<T> handler) where T : struct, ISimEvent;
    }
}
