using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Parses <c>data/**/*.json</c> bytes into content definitions. Spec:
    /// 08-interfaces-core.md §8.11 "The loader" (Q-011). Construct through
    /// <see cref="ContentLoaderFactory"/>.
    /// </summary>
    public interface IContentLoader
    {
        /// <summary>
        /// Reads every recognised content file from <paramref name="source"/> and
        /// validates it. Throws <see cref="System.FormatException"/> on any load
        /// failure, its message starting with the offending file's path
        /// (07-conventions.md "Error handling", Q-030).
        /// </summary>
        IReadOnlyList<IContentDefinition> Load(IContentSource source);
    }
}
