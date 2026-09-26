using System;

namespace AirportSim.Sim.Core
{
    /// <summary>Spec: 08-interfaces-core.md §8.11a.</summary>
    public static class SimHostFactory
    {
        /// <summary>
        /// Creates a fresh builder from a validated config. Throws
        /// <see cref="ArgumentNullException"/> if any reference member of config is null.
        /// </summary>
        public static ISimHostBuilder CreateBuilder(in SimHostConfig config)
        {
            if (config.Content is null)
            {
                throw new ArgumentNullException(nameof(config), "SimHostConfig.Content must not be null");
            }
            if (config.Checkpoints is null)
            {
                throw new ArgumentNullException(nameof(config), "SimHostConfig.Checkpoints must not be null");
            }
            if (config.Log is null)
            {
                throw new ArgumentNullException(nameof(config), "SimHostConfig.Log must not be null");
            }

            var eventBus = new EventBus();
            var idAllocator = new IdAllocator();
            var commands = new CommandHandlerRegistryPlaceholder();
            IRandomService rng = RandomServiceFactory.Create(config.MasterSeed);

            var services = new SystemServices(eventBus, idAllocator, config.Content, commands);

            return new SimHostBuilder(services, eventBus, rng, config.Content, config.Log, config.Checkpoints, idAllocator);
        }
    }
}
