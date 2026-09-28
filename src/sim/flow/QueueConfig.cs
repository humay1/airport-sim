using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>A queue node's throughput model. Spec: 09-interfaces-flow.md §9.4.</summary>
    public readonly struct QueueConfig
    {
        /// <summary>Lanes/desks physically built.</summary>
        public int ServerCount { get; }

        /// <summary>Lanes/desks currently open; &lt;= <see cref="ServerCount"/>.</summary>
        public int ServersOpen { get; }

        /// <summary>Passengers per sim-minute, per open server, from content.</summary>
        public Fx ServiceRatePerServer { get; }

        /// <summary>The spillback threshold (§9.5).</summary>
        public int CapacityStanding { get; }

        /// <summary>Constructs the config from its four fields, in declared order.</summary>
        public QueueConfig(int serverCount, int serversOpen, Fx serviceRatePerServer, int capacityStanding)
        {
            ServerCount = serverCount;
            ServersOpen = serversOpen;
            ServiceRatePerServer = serviceRatePerServer;
            CapacityStanding = capacityStanding;
        }
    }
}
