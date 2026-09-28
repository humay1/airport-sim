using System;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// Parses the §18.2 fixture format into a <see cref="WalkGraph"/>. Spec:
    /// 18-interfaces-world.md §18.2.
    /// </summary>
    public interface IWalkGraphLoader
    {
        /// <summary>
        /// Parses <paramref name="file"/>, naming <paramref name="sourceName"/> in any
        /// failure message. Throws <see cref="FormatException"/> for a syntax, shape,
        /// range or validation failure, and <see cref="ArgumentNullException"/> for a
        /// null <paramref name="sourceName"/>.
        /// </summary>
        WalkGraph Load(ReadOnlySpan<byte> file, string sourceName);
    }
}
