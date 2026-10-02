using System.Collections.Generic;

namespace AirportSim.Sim.Airside
{
    /// <summary>Routing, computed once at construction. Spec: 12-interfaces-airside.md §12.4 "Routing".</summary>
    internal sealed partial class AirsideSystem
    {
        private const long Infinity = long.MaxValue / 4;

        // Arrival routes, indexed runway * stands + stand: threshold to stand.
        private int[] _arrBase = null!;
        private int[] _arrLen = null!;
        private ulong[] _arrTicks = null!;

        // Departure routes, indexed stand * runways + runway: stand to threshold.
        private int[] _depBase = null!;
        private int[] _depLen = null!;
        private ulong[] _depTicks = null!;

        // Flat route storage: the edge index taken, and the node index reached.
        private int[] _routeEdge = null!;
        private int[] _routeNode = null!;

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
            var outArcs = new List<int>[nodes];
            var inArcs = new List<int>[nodes];
            for (int i = 0; i < nodes; i++)
            {
                outArcs[i] = new List<int>();
                inArcs[i] = new List<int>();
            }

            for (int a = 0; a < arcs; a++)
            {
                outArcs[arcFrom[a]].Add(a);
                inArcs[arcTo[a]].Add(a);
            }

            // Distance to each target node, from every node (reverse Dijkstra).
            var distTo = new long[nodes][];
            for (int s = 0; s < stands; s++)
            {
                EnsureDist(distTo, _standNode[s], nodes, inArcs, arcFrom, arcEdge);
            }

            for (int r = 0; r < runways; r++)
            {
                EnsureDist(distTo, _rwyNode[r], nodes, inArcs, arcFrom, arcEdge);
            }

            _arrBase = new int[runways * stands];
            _arrLen = new int[runways * stands];
            _arrTicks = new ulong[runways * stands];
            _depBase = new int[stands * runways];
            _depLen = new int[stands * runways];
            _depTicks = new ulong[stands * runways];
            var flatEdge = new List<int>();
            var flatNode = new List<int>();

            for (int r = 0; r < runways; r++)
            {
                for (int s = 0; s < stands; s++)
                {
                    int ai = (r * stands) + s;
                    _arrBase[ai] = flatEdge.Count;
                    Walk(_rwyNode[r], _standNode[s], distTo[_standNode[s]], outArcs, arcTo, arcEdge, flatEdge, flatNode);
                    _arrLen[ai] = flatEdge.Count - _arrBase[ai];
                    _arrTicks[ai] = (ulong)distTo[_standNode[s]][_rwyNode[r]];

                    int di = (s * runways) + r;
                    _depBase[di] = flatEdge.Count;
                    Walk(_standNode[s], _rwyNode[r], distTo[_rwyNode[r]], outArcs, arcTo, arcEdge, flatEdge, flatNode);
                    _depLen[di] = flatEdge.Count - _depBase[di];
                    _depTicks[di] = (ulong)distTo[_rwyNode[r]][_standNode[s]];
                }
            }

            _routeEdge = flatEdge.ToArray();
            _routeNode = flatNode.ToArray();
        }

        private void EnsureDist(long[][] distTo, int target, int nodes, List<int>[] inArcs, List<int> arcFrom, List<int> arcEdge)
        {
            if (distTo[target] != null)
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

            distTo[target] = dist;
        }

        /// <summary>
        /// Follows least-cost arcs from origin to target; at each node takes the lowest edge id
        /// among the arcs that stay on some least-cost route, which is the tie-break of §12.4.
        /// </summary>
        private void Walk(int origin, int target, long[] dist, List<int>[] outArcs, List<int> arcTo, List<int> arcEdge, List<int> flatEdge, List<int> flatNode)
        {
            int u = origin;
            while (u != target)
            {
                int best = -1;
                foreach (int a in outArcs[u])
                {
                    int v = arcTo[a];
                    if (dist[v] < Infinity && (long)_edgeTicks[arcEdge[a]] + dist[v] == dist[u] && (best < 0 || arcEdge[a] < arcEdge[best]))
                    {
                        best = a;
                    }
                }

                flatEdge.Add(arcEdge[best]);
                flatNode.Add(arcTo[best]);
                u = arcTo[best];
            }
        }
    }
}
