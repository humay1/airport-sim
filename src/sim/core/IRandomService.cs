namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.8. Shape only at T-001; T-002 gives it behaviour.
    /// </summary>
    public interface IRandomService
    {
        /// <summary>The live, named draw stream, derived from <see cref="MasterSeed"/>.</summary>
        IRandomStream Stream(RngStreamName name);

        /// <summary>The seed the whole service was constructed from.</summary>
        ulong MasterSeed { get; }
    }
}
