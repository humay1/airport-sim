namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// A queue node's lane state, for presentation. Spec: 09-interfaces-flow.md §9.7b (Q-010).
    /// </summary>
    public readonly struct LaneState
    {
        /// <summary>Lanes/desks physically built.</summary>
        public int ServerCount { get; }

        /// <summary>Lanes/desks currently open.</summary>
        public int ServersOpen { get; }

        /// <summary>Constructs the state from its two fields.</summary>
        public LaneState(int serverCount, int serversOpen)
        {
            ServerCount = serverCount;
            ServersOpen = serversOpen;
        }
    }
}
