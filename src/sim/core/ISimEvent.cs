namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Marker for an event payload struct. The struct holds payload fields only;
    /// the bus carries the envelope beside it. Spec: 08-interfaces-core.md §8.6, 10-events.md §10.2.
    /// </summary>
    public interface ISimEvent
    {
    }
}
