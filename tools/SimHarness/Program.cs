using System;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The headless console entry point <c>ci/run-checks.sh</c> invokes
    /// (<c>dotnet run --project tools/SimHarness</c>). Spec: 19-interfaces-harness.md
    /// §19.1 (Q-026): <c>Program.Main</c> is exactly <see cref="HarnessCli.Run"/>,
    /// returned as the process exit code.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            return HarnessCli.Run(args, Console.Out, Console.Error);
        }
    }
}
