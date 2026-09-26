using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// Registers systems on a fresh builder for one run. Must not call
    /// <see cref="ISimHostBuilder.Build"/>; the harness calls it exactly once
    /// per run and builds itself. Spec: 19-interfaces-harness.md §19.1 (Q-026).
    /// </summary>
    public delegate void SimComposer(ISimHostBuilder builder);
}
