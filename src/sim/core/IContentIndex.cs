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
        bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition;
        IReadOnlyList<ContentId> AllOf(ContentKind kind);
    }
}
