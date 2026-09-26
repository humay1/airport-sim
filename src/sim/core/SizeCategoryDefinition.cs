namespace AirportSim.Sim.Core
{
    /// <summary>An aircraft size category definition. Spec: 08-interfaces-core.md §8.11.</summary>
    public readonly struct SizeCategoryDefinition : IContentDefinition
    {
        /// <summary>The definition's content id.</summary>
        public ContentId Id { get; }

        /// <summary>The category's ordinal, used to order size categories.</summary>
        public int Ordinal { get; }

        /// <summary>The definition's content kind.</summary>
        public ContentKind Kind => ContentKind.SizeCategory;

        /// <summary>Constructs the definition from its id and ordinal.</summary>
        public SizeCategoryDefinition(ContentId id, int ordinal)
        {
            Id = id;
            Ordinal = ordinal;
        }
    }
}
