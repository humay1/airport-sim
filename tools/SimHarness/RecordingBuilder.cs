using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The run-2 builder wrapper of 19-interfaces-harness.md §19.2d. Forwards every
    /// member to the factory builder and records a system only once its forwarded
    /// <c>Register</c> returned. A composer must not call <see cref="Build"/>.
    /// </summary>
    internal sealed class RecordingBuilder : ISimHostBuilder
    {
        private readonly ISimHostBuilder _inner;
        private readonly List<ISimSystem> _recorded = new List<ISimSystem>();

        internal RecordingBuilder(ISimHostBuilder inner)
        {
            _inner = inner;
        }

        internal IReadOnlyList<ISimSystem> Recorded => _recorded;

        public SystemServices Services => _inner.Services;

        public void Register(ISimSystem system)
        {
            _inner.Register(system);
            _recorded.Add(system);
        }

        public ISimHost Build() => _inner.Build();
    }
}
