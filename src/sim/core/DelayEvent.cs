namespace AirportSim.Sim.Core
{
    /// <summary>A delay attribution node was recorded. Spec: 10-events.md §10.9, 14-interfaces-delay.md §14.3.</summary>
    public readonly struct DelayEvent : ISimEvent
    {
        /// <summary>The recorded delay node.</summary>
        public DelayNode Node { get; }

        /// <summary>Constructs the event from its member.</summary>
        public DelayEvent(DelayNode node)
        {
            Node = node;
        }
    }
}
