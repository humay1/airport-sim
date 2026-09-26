using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="ISimHostBuilder"/> implementation. Spec: 08-interfaces-core.md §8.11a.
    /// </summary>
    internal sealed class SimHostBuilder : ISimHostBuilder
    {
        private readonly EventBus _eventBus;
        private readonly IRandomService _rng;
        private readonly IContentIndex _content;
        private readonly ISimLog _log;
        private readonly ICheckpointSink _checkpoints;
        private readonly IdAllocator _idAllocator;
        private readonly CommandHandlerRegistry _registry;
        private readonly List<ISimSystem> _systems = new List<ISimSystem>();

        private ushort _lastRegistered;
        private bool _built;

        internal SimHostBuilder(
            SystemServices services,
            EventBus eventBus,
            IRandomService rng,
            IContentIndex content,
            ISimLog log,
            ICheckpointSink checkpoints,
            IdAllocator idAllocator,
            CommandHandlerRegistry registry)
        {
            Services = services;
            _eventBus = eventBus;
            _rng = rng;
            _content = content;
            _log = log;
            _checkpoints = checkpoints;
            _idAllocator = idAllocator;
            _registry = registry;
        }

        public SystemServices Services { get; }

        public void Register(ISimSystem system)
        {
            if (_built)
            {
                throw new InvalidOperationException("Register cannot be called after Build");
            }
            if (system is null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            ushort value = system.Id.Value;
            if (value == 0 || value == 8 || value > 14)
            {
                throw new ArgumentException("system id must be a registry position, 1-14 excluding 8", nameof(system));
            }
            if (value <= _lastRegistered)
            {
                throw new ArgumentException("systems must be registered in strictly ascending registry order", nameof(system));
            }

            _lastRegistered = value;
            _systems.Add(system);
        }

        public ISimHost Build()
        {
            if (_built)
            {
                throw new InvalidOperationException("Build is callable once");
            }
            foreach (ushort subscriberId in _eventBus.SubscriberIds)
            {
                bool registered = false;
                for (int i = 0; i < _systems.Count; i++)
                {
                    if (_systems[i].Id.Value == subscriberId)
                    {
                        registered = true;
                        break;
                    }
                }
                if (!registered)
                {
                    throw new InvalidOperationException(
                        "Build: a subscriber (system id " + subscriberId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ") has no registered system");
                }
            }

            _registry.EnsureOwnersRegistered(IsSystemRegistered);

            _built = true;
            _eventBus.MarkBuilt();
            _registry.MarkBuilt();

            return new SimHost(_systems.ToArray(), _eventBus, _rng, _content, _log, _checkpoints, _idAllocator, _registry);
        }

        private bool IsSystemRegistered(ushort id)
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].Id.Value == id)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
