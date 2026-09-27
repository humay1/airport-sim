using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Raw content bytes for <see cref="IContentLoader"/>. Spec: 08-interfaces-core.md
    /// §8.11 "The loader" (Q-011).
    /// </summary>
    public interface IContentSource
    {
        /// <summary>Every file's path, relative to <c>data/</c>, '/'-separated, in any order.</summary>
        IReadOnlyList<string> Files();

        /// <summary>The raw bytes of the file at <paramref name="path"/>.</summary>
        byte[] ReadAll(string path);
    }
}
