using System.Collections.Generic;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// Node behaviour over sim.world's nodes, opaque outside sim.flow. Spec:
    /// 09-interfaces-flow.md §9.11 (Q-032).
    /// </summary>
    public readonly struct FlowGraph
    {
        internal IReadOnlyList<FlowNodeDef> Nodes { get; }

        internal FlowGraph(IReadOnlyList<FlowNodeDef> nodes)
        {
            Nodes = nodes;
        }
    }
}
