using System;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The headless console entry point <c>ci/run-checks.sh</c> invokes
    /// (<c>dotnet run --project tools/SimHarness</c>). A thin host over <see cref="ISimHost"/>:
    /// it builds a sim and steps it. It does not implement the <c>determinism</c>,
    /// <c>saveload</c>, <c>promotion</c> or <c>budget</c> subcommands
    /// <c>ci/run-checks.sh</c> also calls; those are T-006's (spec/tasks/T-006-determinism-ci-gates.md).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var log = new NullSimLog();
            var checkpoints = new NullCheckpointSink();
            var content = new EmptyContentIndex();

            var config = new SimHostConfig(masterSeed: 1, content: content, checkpoints: checkpoints, log: log);
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            ISimHost host = builder.Build();

            host.Step(checked((uint)SimConstants.TICKS_PER_SIM_DAY));

            Console.WriteLine("tick=" + host.CurrentTick.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.WriteLine("hash=" + host.WorldStateHash().ToString("x16", System.Globalization.CultureInfo.InvariantCulture));
            return 0;
        }
    }
}
