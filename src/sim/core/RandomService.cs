using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="IRandomService"/> implementation. Spec:
    /// 08-interfaces-core.md §8.8 (Q-019, Q-023). Constructed only through
    /// <see cref="RandomServiceFactory"/>. <see cref="Stream"/> returns the same
    /// live <see cref="RandomStream"/> every time it is called with an equal name
    /// in a session: created at the first call, never reset or forked afterwards.
    /// </summary>
    internal sealed class RandomService : IRandomService
    {
        private readonly Dictionary<string, RandomStream> _streams = new Dictionary<string, RandomStream>(StringComparer.Ordinal);

        internal RandomService(ulong masterSeed)
        {
            MasterSeed = masterSeed;
        }

        /// <inheritdoc/>
        public ulong MasterSeed { get; }

        /// <inheritdoc/>
        public IRandomStream Stream(RngStreamName name)
        {
            string? value = name.Value;
            if (value is null)
            {
                throw new ArgumentException(
                    "IRandomService.Stream: default(RngStreamName) is not a valid stream name (08-interfaces-core.md §8.8).",
                    nameof(name));
            }

            if (!_streams.TryGetValue(value, out RandomStream? stream))
            {
                stream = new RandomStream(MasterSeed, value);
                _streams.Add(value, stream);
            }

            return stream;
        }
    }
}
