namespace AirportSim.Sim.Core
{
    /// <summary>An aircraft type definition. Spec: 08-interfaces-core.md §8.11.</summary>
    public readonly struct AircraftDefinition : IContentDefinition
    {
        /// <summary>The definition's content id.</summary>
        public ContentId Id { get; }

        /// <summary>The size category this aircraft type resolves to.</summary>
        public ContentId SizeCategory { get; }

        /// <summary>The definition's content kind.</summary>
        public ContentKind Kind => ContentKind.Aircraft;

        /// <summary>Constructs the definition from its id and size category.</summary>
        public AircraftDefinition(ContentId id, ContentId sizeCategory)
        {
            Id = id;
            SizeCategory = sizeCategory;
        }
    }
}
