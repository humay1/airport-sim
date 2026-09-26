using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// A content index holding no definitions. Local to the harness only: <c>sim.core</c>
    /// ships no null or empty implementations of its published interfaces
    /// (spec/08-interfaces-core.md §8.11a, Q-014 A7). Equivalent to
    /// <c>ContentIndexFactory.Create</c> with an empty list, spelled out here so the
    /// harness stays a thin host over the real construction surface without depending on
    /// a loader.
    /// </summary>
    internal sealed class EmptyContentIndex : IContentIndex
    {
        public bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
        {
            definition = default!;
            return false;
        }

        public IReadOnlyList<ContentId> AllOf(ContentKind kind) => System.Array.Empty<ContentId>();
    }
}
