namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11. Concrete definition structs are T-026's.</summary>
    public interface IContentDefinition
    {
        ContentId Id { get; }
        ContentKind Kind { get; }
    }
}
