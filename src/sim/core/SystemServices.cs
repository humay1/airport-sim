namespace AirportSim.Sim.Core
{
    /// <summary>What a module factory may receive from core. Spec: 08-interfaces-core.md §8.11a.</summary>
    public readonly struct SystemServices
    {
        /// <summary>Subscribe during construction only.</summary>
        public IEventBus Events { get; }

        /// <summary>Entity id allocation.</summary>
        public IIdAllocator Ids { get; }

        /// <summary>Read-only; load-time validation.</summary>
        public IContentIndex Content { get; }

        /// <summary>Register during construction only.</summary>
        public ICommandHandlerRegistry Commands { get; }

        /// <summary>Constructs the services bundle from its four members.</summary>
        public SystemServices(IEventBus events, IIdAllocator ids, IContentIndex content, ICommandHandlerRegistry commands)
        {
            Events = events;
            Ids = ids;
            Content = content;
            Commands = commands;
        }
    }
}
