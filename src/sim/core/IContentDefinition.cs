namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11. Concrete definition structs are T-026's.</summary>
    public interface IContentDefinition
    {
        /// <summary>The definition's content id.</summary>
        ContentId Id { get; }

        /// <summary>The definition's content kind.</summary>
        ContentKind Kind { get; }
    }
}
