using System.Collections.Generic;

namespace AirportSim.Sim.Airside
{
    /// <summary>
    /// Routing, computed once at construction. Spec: 12-interfaces-airside.md §12.4 "Routing".
    /// The tables are fixed at load. A flight's position on its route is never stored: the next
    /// edge is found from the node it is at and its target, which is the same lowest-edge-id
    /// least-cost walk (a suffix of a least route, in this order, is the least route from there).
    /// </summary>
    internal sealed partial class AirsideSystem
    {
        private const long Infinity = long.MaxValue / 4;

        // Route cost in ticks, arrivals indexed runway * stands + stand (threshold to stand),
        // departures indexed stand * runways + runway (stand to threshold).
        private ulong[] _arrTicks = null!;
        private ulong[] _depTicks = null!;

        // Distance to a target node from every node, by target node index; null for non-targets.
        private long[]?[] _distTo = null!;

        // Outgoing arcs per node, flat: arcs of node n are [_outStart[n], _outStart[n + 1]).
        private int[] _outStart = null!;
        private int[] _outTo = null!;
        private int[] _outEdge = null!;

        private void BuildRoutes()
        {
            int nodes = _nodeId.Length;
            int edges = _edgeId.Length;
            int stands = _standId.Length;
            int runways = _rwyId.Length;

            // Directed arcs: an edge, and its reverse if bidirectional.
            var arcFrom = new List<int>();
            var arcTo = new List<int>();
            var arcEdge = new List<int>();
            for (int e = 0; e < edges; e++)
            {
                arcFrom.Add(_edgeFrom[e]);
                arcTo.Add(_edgeTo[e]);
                arcEdge.Add(e);
                if (_edgeBidir[e])
                {
                    arcFrom.Add(_edgeTo[e]);
                    arcTo.Add(_edgeFrom[e]);
                    arcEdge.Add(e);
                }
            }

            int arcs = arcFrom.Count;
            _outStart = new int[nodes + 1];
            for (int a = 0; a < arcs; a++)
            {
                _outStart[arcFrom[a] + 1]++;
            }

            for (int i = 0; i < nodes; i++)
            {
                _outStart[i + 1] += _outStart[i];
            }

            _outTo = new int[arcs];
            _outEdge = new int[arcs];
            var fill = new int[nodes];
            for (int a = 0; a < arcs; a++)
            {
                int at = _outStart[arcFrom[a]] + fill[arcFrom[a]]++;
                _outTo[at] = arcTo[a];
                _outEdge[at] = arcEdge[a];
            }

            var inArcs = new List<int>[nodes];
            for (int i = 0; i < nodes; i++)
            {
                inArcs[i] = new List<int>();
            }

            for (int a = 0; a < arcs; a++)
            {
                inArcs[arcTo[a]].Add(a);
            }

            _distTo = new long[]?[nodes];
            for (int s = 0; s < stands; s++)
            {
                EnsureDist(_standNode[s], nodes, inArcs, arcFrom, arcEdge);
            }

            for (int r = 0; r < runways; r++)
            {
                EnsureDist(_rwyNode[r], nodes, inArcs, arcFrom, arcEdge);
            }

            _arrTicks = new ulong[runways * stands];
            _depTicks = new ulong[stands * runways];
            for (int r = 0; r < runways; r++)
            {
                for (int s = 0; s < stands; s++)
                {
                    _arrTicks[(r * stands) + s] = (ulong)_distTo[_standNode[s]]![_rwyNode[r]];
                    _depTicks[(s * runways) + r] = (ulong)_distTo[_rwyNode[r]]![_standNode[s]];
                }
            }
        }

        private void EnsureDist(int target, int nodes, List<int>[] inArcs, List<int> arcFrom, List<int> arcEdge)
        {
            if (_distTo[target] != null)
            {
                return;
            }

            var dist = new long[nodes];
            var done = new bool[nodes];
            for (int i = 0; i < nodes; i++)
            {
                dist[i] = Infinity;
            }

            dist[target] = 0;
            while (true)
            {
                int u = -1;
                for (int i = 0; i < nodes; i++)
                {
                    if (!done[i] && dist[i] < Infinity && (u < 0 || dist[i] < dist[u]))
                    {
                        u = i;
                    }
                }

                if (u < 0)
                {
                    break;
                }

                done[u] = true;
                foreach (int a in inArcs[u])
                {
                    int v = arcFrom[a];
                    long nd = dist[u] + (long)_edgeTicks[arcEdge[a]];
                    if (nd < dist[v])
                    {
                        dist[v] = nd;
                    }
                }
            }

            _distTo[target] = dist;
        }

        /// <summary>
        /// The next edge from <paramref name="node"/> towards <paramref name="target"/>: the lowest
        /// edge id among the arcs that stay on a least-cost route (the tie-break of §12.4).
        /// </summary>
        private int NextEdge(int node, int target)
        {
            long[] dist = _distTo[target]!;
            int best = -1;
            for (int a = _outStart[node]; a < _outStart[node + 1]; a++)
            {
                int v = _outTo[a];
                if (dist[v] < Infinity && (long)_edgeTicks[_outEdge[a]] + dist[v] == dist[node] && (best < 0 || _outEdge[a] < best))
                {
                    best = _outEdge[a];
                }
            }

            return best;
        }
    }
}
