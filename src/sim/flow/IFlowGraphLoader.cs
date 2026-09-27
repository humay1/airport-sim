using System;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow
{
    /// <summary>Parses the §9.11 flow graph file. Spec: 09-interfaces-flow.md §9.11 (Q-032).</summary>
    public interface IFlowGraphLoader
    {
        /// <summary>
        /// Parses and validates <paramref name="file"/> against <paramref name="world"/>, naming
        /// <paramref name="sourceName"/> in any failure message. Throws
        /// <see cref="FormatException"/> for a syntax, shape, range or validation failure, and
        /// <see cref="ArgumentNullException"/> for a null <paramref name="sourceName"/> or
        /// <paramref name="world"/>.
        /// </summary>
        FlowGraph Load(ReadOnlySpan<byte> file, string sourceName, IWorldSystem world);
    }
}
