using System;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>Parses a setup file (13-interfaces-turnaround.md §13.10a).</summary>
    public interface ITurnaroundSetupLoader
    {
        /// <summary>Parses the file and applies the §13.4 validation. Throws FormatException naming the source on failure.</summary>
        TurnaroundSetup Load(ReadOnlySpan<byte> file, string sourceName);
    }
}
