using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Read-only, immutable-for-the-session content data. Spec: 08-interfaces-core.md §8.11.
    /// Shape only at T-001; no concrete implementation or factory is built here
    /// (<c>ContentIndexFactory</c> is unowned per Q-014).
    /// </summary>
    public interface IContentIndex
    {
        /// <summary>Looks up a definition by id, typed as T; false if absent or of a different kind.</summary>
        bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition;

        /// <summary>Every content id of the given kind, in ordinal id order.</summary>
        IReadOnlyList<ContentId> AllOf(ContentKind kind);
    }
}
