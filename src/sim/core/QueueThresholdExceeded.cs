namespace AirportSim.Sim.Core
{
    /// <summary>A queue's wait crossed its threshold. Spec: 10-events.md §10.9.</summary>
    public readonly struct QueueThresholdExceeded : ISimEvent
    {
        /// <summary>The queue node.</summary>
        public NodeId Node { get; }

        /// <summary>The current wait, in sim-minutes.</summary>
        public Fx WaitMinutes { get; }

        /// <summary>Servers currently open.</summary>
        public int ServersOpen { get; }

        /// <summary>Total server count.</summary>
        public int ServerCount { get; }

        /// <summary>Constructs the event from its members, in declared order.</summary>
        public QueueThresholdExceeded(NodeId node, Fx waitMinutes, int serversOpen, int serverCount)
        {
            Node = node;
            WaitMinutes = waitMinutes;
            ServersOpen = serversOpen;
            ServerCount = serverCount;
        }
    }
}
