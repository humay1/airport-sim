namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Spec: 08-interfaces-core.md §8.8. Construct through
    /// <see cref="RandomServiceFactory"/>.
    /// </summary>
    public interface IRandomService
    {
        /// <summary>
        /// The live, named draw stream, derived from <see cref="MasterSeed"/>. Returns
        /// the same live stream every time it is called with an equal name in a
        /// session; created at the first call, never reset or forked afterwards.
        /// Throws <see cref="System.ArgumentException"/> for <c>default(RngStreamName)</c>.
        /// </summary>
        IRandomStream Stream(RngStreamName name);

        /// <summary>The seed the whole service was constructed from.</summary>
        ulong MasterSeed { get; }
    }
}
