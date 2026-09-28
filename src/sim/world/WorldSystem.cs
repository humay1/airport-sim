using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// The fixed landside walk graph and its precomputed routes. Spec:
    /// 18-interfaces-world.md §18.3, §18.4. All-pairs shortest distances and
    /// materialised shortest-path node lists are computed once here, at
    /// construction (off the tick path); every query afterwards is a direct
    /// lookup into those tables, allocating nothing.
    /// </summary>
    internal sealed class WorldSystem : IWorldSystem
    {
        private const ulong Infinity = ulong.MaxValue;

        private readonly WalkGraph _graph;
        private readonly NodeId[] _nodeIds;
        private readonly Dictionary<uint, int> _nodeIndex;
        private readonly Dictionary<uint, int> _edgeIndex;
        private readonly uint[] _lengthByNode;
        private readonly NodeId[] _edgeTarget;
        private readonly EdgeId[][] _outEdgesByNode;
        private readonly ulong[,] _dist;

        /// <summary>Flattened n×n table: <c>_path[i * _nodeIds.Length + j]</c> is the materialised path from i to j.</summary>
        private readonly NodeId[][] _path;
        private readonly int _n;

        internal WorldSystem(in WalkGraph graph)
        {
            _graph = graph;
            int n = graph.Nodes.Count;
            int m = graph.Edges.Count;

            _nodeIds = new NodeId[n];
            _nodeIndex = new Dictionary<uint, int>(n);
            for (int i = 0; i < n; i++)
            {
                _nodeIds[i] = graph.Nodes[i].Id;
                _nodeIndex[graph.Nodes[i].Id.Value] = i;
            }

            _lengthByNode = new uint[n];
            for (int i = 0; i < n; i++)
            {
                _lengthByNode[i] = graph.Nodes[i].LengthMetres;
            }

            _edgeIndex = new Dictionary<uint, int>(m);
            _edgeTarget = new NodeId[m];
            var edgeSource = new int[m];
            var edgeTargetIndex = new int[m];
            for (int i = 0; i < m; i++)
            {
                WalkEdgeDef e = graph.Edges[i];
                _edgeIndex[e.Id.Value] = i;
                _edgeTarget[i] = e.To;
                edgeSource[i] = _nodeIndex[e.From.Value];
                edgeTargetIndex[i] = _nodeIndex[e.To.Value];
            }

            var outLists = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                outLists[i] = new List<int>();
            }

            for (int i = 0; i < m; i++)
            {
                outLists[edgeSource[i]].Add(i);
            }

            _outEdgesByNode = new EdgeId[n][];
            for (int i = 0; i < n; i++)
            {
                var arr = new EdgeId[outLists[i].Count];
                for (int k = 0; k < arr.Length; k++)
                {
                    arr[k] = graph.Edges[outLists[i][k]].Id;
                }

                _outEdgesByNode[i] = arr;
            }

            _n = n;
            _dist = ComputeAllPairsDistance(n, m, _lengthByNode, edgeSource, edgeTargetIndex);
            _path = MaterialisePaths(n, _dist, _lengthByNode, _outEdgesByNode, edgeTargetIndex, _edgeIndex, _nodeIds);
        }

        private static ulong[,] ComputeAllPairsDistance(int n, int m, uint[] lengthByNode, int[] edgeSource, int[] edgeTargetIndex)
        {
            var dist = new ulong[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    dist[i, j] = i == j ? 0UL : Infinity;
                }
            }

            for (int e = 0; e < m; e++)
            {
                int from = edgeSource[e];
                int to = edgeTargetIndex[e];
                ulong w = lengthByNode[to];
                if (w < dist[from, to])
                {
                    dist[from, to] = w;
                }
            }

            for (int k = 0; k < n; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (dist[i, k] == Infinity)
                    {
                        continue;
                    }

                    for (int j = 0; j < n; j++)
                    {
                        if (dist[k, j] == Infinity)
                        {
                            continue;
                        }

                        ulong via = dist[i, k] + dist[k, j];
                        if (via < dist[i, j])
                        {
                            dist[i, j] = via;
                        }
                    }
                }
            }

            return dist;
        }

        /// <summary>
        /// For every ordered pair (i, j), the shortest walk starting at and
        /// including i, ending at j inclusive; empty when unreachable, a
        /// singleton [i] when i == j (the empty route from n to n, Q-031).
        /// This is exactly PathVia's own result once i is resolved as
        /// EdgeTo(firstEdge), so no further work happens at query time.
        /// <para>
        /// Only <b>simple</b> paths are candidates (Q-031): no node repeats.
        /// Costs are non-negative, so the true minimum cost is still exactly
        /// <paramref name="dist"/> (removing a repeated-node loop from any walk
        /// only lowers or preserves its cost). Reconstruction is a pruned
        /// backtracking search: at each node, try the smallest outgoing edge
        /// whose target is unvisited and still lies on a cost-optimal
        /// continuation (the Bellman condition against <paramref name="dist"/>);
        /// backtrack only when a visited-set conflict (a zero-length cycle)
        /// makes that particular choice a dead end. Trying edges in ascending
        /// order and returning on first success yields the lexicographically
        /// smallest valid sequence (a standard greedy-choice argument).
        /// </para>
        /// Flattened to a 1D array of n*n entries (<c>i * n + j</c>).
        /// </summary>
        private static NodeId[][] MaterialisePaths(
            int n, ulong[,] dist, uint[] lengthByNode, EdgeId[][] outEdgesByNode, int[] edgeTargetIndex,
            Dictionary<uint, int> edgeIndex, NodeId[] nodeIds)
        {
            NodeId[] empty = Array.Empty<NodeId>();
            var result = new NodeId[n * n][];
            var visited = new bool[n];
            var chain = new List<int>();

            bool Search(int current, int destIndex)
            {
                ulong needed = dist[current, destIndex];
                EdgeId[] outs = outEdgesByNode[current];
                for (int k = 0; k < outs.Length; k++)
                {
                    int target = edgeTargetIndex[edgeIndex[outs[k].Value]];
                    if (visited[target] || dist[target, destIndex] == Infinity)
                    {
                        continue;
                    }

                    if (lengthByNode[target] + dist[target, destIndex] != needed)
                    {
                        continue;
                    }

                    visited[target] = true;
                    chain.Add(target);
                    if (target == destIndex || Search(target, destIndex))
                    {
                        return true;
                    }

                    chain.RemoveAt(chain.Count - 1);
                    visited[target] = false;
                }

                return false;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (dist[i, j] == Infinity)
                    {
                        result[(i * n) + j] = empty;
                        continue;
                    }

                    if (i == j)
                    {
                        result[(i * n) + j] = new[] { nodeIds[i] };
                        continue;
                    }

                    Array.Clear(visited, 0, n);
                    visited[i] = true;
                    chain.Clear();
                    Search(i, j);

                    // PathVia's own leading element is i itself (the node the
                    // resolved firstEdge enters); prepend it here so the query
                    // path is a single stored lookup, no concatenation.
                    var arr = new NodeId[chain.Count + 1];
                    arr[0] = nodeIds[i];
                    for (int p = 0; p < chain.Count; p++)
                    {
                        arr[p + 1] = nodeIds[chain[p]];
                    }

                    result[(i * n) + j] = arr;
                }
            }

            return result;
        }

        public SystemId Id => new SystemId(1);

        public string Name => "sim.world";

        public void Tick(in TickContext ctx)
        {
            // 18 §18.4: no runtime state, no RNG, no events. Deliberately empty.
        }

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            h.Feed(_graph.FixtureHash);
            return h.Result;
        }

        public IReadOnlyList<NodeId> Nodes()
        {
            return _nodeIds;
        }

        public uint LengthMetres(NodeId node)
        {
            return _lengthByNode[NodeIndex(node)];
        }

        public IReadOnlyList<EdgeId> OutEdges(NodeId node)
        {
            return _outEdgesByNode[NodeIndex(node)];
        }

        public NodeId EdgeTo(EdgeId edge)
        {
            return _edgeTarget[EdgeIndex(edge)];
        }

        public bool CanReach(NodeId from, NodeId destination)
        {
            int i = NodeIndex(from);
            int j = NodeIndex(destination);
            return _dist[i, j] != Infinity;
        }

        public bool CanReachVia(EdgeId firstEdge, NodeId destination)
        {
            int e = EdgeIndex(firstEdge);
            int j = NodeIndex(destination);
            int v = NodeIndex(_edgeTarget[e]);
            return _dist[v, j] != Infinity;
        }

        public IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
        {
            int e = EdgeIndex(firstEdge);
            int j = NodeIndex(destination);
            int v = NodeIndex(_edgeTarget[e]);
            return _path[(v * _n) + j];
        }

        private int NodeIndex(NodeId node)
        {
            if (_nodeIndex.TryGetValue(node.Value, out int i))
            {
                return i;
            }

            throw new ArgumentException("unknown node id " + node.Value, nameof(node));
        }

        private int EdgeIndex(EdgeId edge)
        {
            if (_edgeIndex.TryGetValue(edge.Value, out int i))
            {
                return i;
            }

            throw new ArgumentException("unknown edge id " + edge.Value, nameof(edge));
        }
    }
}
