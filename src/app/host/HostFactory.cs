using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.App.Host
{
    /// <summary>The module's one factory: stateless static methods only. Spec: 16 §16.3, §16.4, §16.5, §16.8; 08 §8.11a.</summary>
    public static class HostFactory
    {
        /// <summary>Loads the content definitions from a source with 08 §8.11's loader. A failure throws <see cref="System.FormatException"/>.</summary>
        public static IReadOnlyList<IContentDefinition> LoadContent(IContentSource source)
        {
            return ContentLoaderFactory.Create().Load(source);
        }

        /// <summary>Creates the sim composer over the content definitions (§16.4).</summary>
        public static ISimComposer CreateSimComposer(IReadOnlyList<IContentDefinition> content)
        {
            return new SimComposer(content);
        }

        /// <summary>Creates the presentation composer (§16.5).</summary>
        public static IPresentationComposer CreatePresentationComposer()
        {
            return new PresentationComposer();
        }

        /// <summary>Creates the command-line parser (§16.8).</summary>
        public static IHostCommandLine CreateCommandLine()
        {
            return new HostCommandLine();
        }

        /// <summary>Creates the headless checkpoint run over a sim composer (§16.8).</summary>
        public static IHeadlessRun CreateHeadlessRun(ISimComposer composer)
        {
            return new HeadlessRun(composer);
        }
    }
}
