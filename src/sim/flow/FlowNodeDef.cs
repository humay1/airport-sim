using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow
{
    /// <summary>One node's flow behaviour, as parsed from the flow graph file (§9.11).</summary>
    internal readonly struct FlowNodeDef
    {
        internal NodeId Id { get; }

        internal NodeKind Kind { get; }

        /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="NodeKind.Queue"/>.</summary>
        internal int ServerCount { get; }

        /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="NodeKind.Queue"/>.</summary>
        internal int ServersOpen { get; }

        /// <summary>Meaningful only when <see cref="Kind"/> is <see cref="NodeKind.Queue"/>.</summary>
        internal ContentId QueueProfile { get; }

        internal FlowNodeDef(NodeId id, NodeKind kind, int serverCount, int serversOpen, ContentId queueProfile)
        {
            Id = id;
            Kind = kind;
            ServerCount = serverCount;
            ServersOpen = serversOpen;
            QueueProfile = queueProfile;
        }
    }
}
