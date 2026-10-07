using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Airside
{
    /// <summary>Validates airside layouts. Spec: 12-interfaces-airside.md §12.4 "Load-time validation" (Q-046).</summary>
    internal sealed class LayoutLoader : IAirsideLayoutLoader
    {
        private const string Prefix = "sim.airside: ";

        public AirsideLayout Parse(ReadOnlySpan<byte> file, string sourceName)
        {
            AirsideLayout raw = LayoutParser.Parse(file, sourceName);
            try
            {
                return Load(raw);
            }
            catch (FormatException ex)
            {
                throw new FormatException(sourceName + ": " + ex.Message);
            }
        }

        public AirsideLayout Load(AirsideLayout raw)
        {
            if (raw.Runways is null || raw.Nodes is null || raw.Edges is null || raw.Stands is null)
            {
                throw new ArgumentException(Prefix + "a layout list is null", nameof(raw));
            }

            RunwayDef[] runways = Copy(raw.Runways);
            TaxiNodeDef[] nodes = Copy(raw.Nodes);
            TaxiEdgeDef[] edges = Copy(raw.Edges);
            StandDef[] stands = Copy(raw.Stands);

            CheckRanges(runways, nodes, edges, stands);

            if (runways.Length == 0)
            {
                throw new FormatException(Prefix + "runways: at least one runway is required");
            }

            if (stands.Length == 0)
            {
                throw new FormatException(Prefix + "stands: at least one stand is required");
            }

            Array.Sort(runways, (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            Array.Sort(nodes, (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            Array.Sort(edges, (a, b) => a.Id.Value.CompareTo(b.Id.Value));
            Array.Sort(stands, (a, b) => a.Id.Value.CompareTo(b.Id.Value));

            CheckUnique("runways", runways.Length, i => runways[i].Id.Value);
            CheckUnique("nodes", nodes.Length, i => nodes[i].Id.Value);
            CheckUnique("edges", edges.Length, i => edges[i].Id.Value);
            CheckUnique("stands", stands.Length, i => stands[i].Id.Value);

            CheckReferences(runways, nodes, edges, stands);
            CheckKinds(runways, nodes, stands);
            CheckConnected(runways, nodes, edges);
            CheckExitsReachStands(runways, nodes, edges);

            return new AirsideLayout(runways, nodes, edges, stands);
        }

        private static T[] Copy<T>(IReadOnlyList<T> list)
        {
            var a = new T[list.Count];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = list[i];
            }

            return a;
        }

        private static string N(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private static FormatException Range(string list, long id, string field)
        {
            return new FormatException(Prefix + list + ": object " + N(id) + " has field " + field + " out of range");
        }

        /// <summary>Check 1: the offending object with the lowest id, and its first failing field.</summary>
        private static void CheckRanges(RunwayDef[] runways, TaxiNodeDef[] nodes, TaxiEdgeDef[] edges, StandDef[] stands)
        {
            string? field = null;
            long best = long.MaxValue;
            foreach (RunwayDef r in runways)
            {
                string? f = r.Id.Value < 1 ? "id"
                    : r.ActiveDirectionDeg < 0 || r.ActiveDirectionDeg > 359 ? "active_direction_deg"
                    : r.DeclaredCapacityPerHour < 1 ? "declared_capacity_per_hour"
                    : r.OccupancyTicks < 1 ? "occupancy_ticks" : null;
                if (f != null && r.Id.Value < best)
                {
                    best = r.Id.Value;
                    field = f;
                }
            }

            if (field != null)
            {
                throw Range("runways", best, field);
            }

            foreach (TaxiNodeDef n in nodes)
            {
                if (n.Id.Value < 1 && n.Id.Value < best)
                {
                    best = n.Id.Value;
                    field = "id";
                }
            }

            if (field != null)
            {
                throw Range("nodes", best, field);
            }

            foreach (TaxiEdgeDef e in edges)
            {
                string? f = e.Id.Value < 1 ? "id" : e.TraversalTicks < 1 ? "traversal_ticks" : null;
                if (f != null && e.Id.Value < best)
                {
                    best = e.Id.Value;
                    field = f;
                }
            }

            if (field != null)
            {
                throw Range("edges", best, field);
            }

            foreach (StandDef s in stands)
            {
                string? f = s.Id.Value < 1 ? "id" : s.DepartureSinkNode.Value < 1 ? "departure_sink_node" : null;
                if (f != null && s.Id.Value < best)
                {
                    best = s.Id.Value;
                    field = f;
                }
            }

            if (field != null)
            {
                throw Range("stands", best, field);
            }
        }

        private static void CheckUnique(string list, int count, Func<int, ushort> id)
        {
            for (int i = 1; i < count; i++)
            {
                if (id(i) == id(i - 1))
                {
                    throw new FormatException(Prefix + list + ": duplicate id " + N(id(i)));
                }
            }
        }

        private static bool Has(TaxiNodeDef[] nodes, ushort id)
        {
            int lo = 0;
            int hi = nodes.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (nodes[mid].Id.Value == id)
                {
                    return true;
                }

                if (nodes[mid].Id.Value < id)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return false;
        }

        private static FormatException Ref(string list, long id, string field, long node)
        {
            return new FormatException(Prefix + list + ": object " + N(id) + " field " + field + " names undeclared node " + N(node));
        }

        private static void CheckReferences(RunwayDef[] runways, TaxiNodeDef[] nodes, TaxiEdgeDef[] edges, StandDef[] stands)
        {
            foreach (RunwayDef r in runways)
            {
                if (!Has(nodes, r.ThresholdNode.Value))
                {
                    throw Ref("runways", r.Id.Value, "threshold_node", r.ThresholdNode.Value);
                }

                if (!Has(nodes, r.ExitNode.Value))
                {
                    throw Ref("runways", r.Id.Value, "exit_node", r.ExitNode.Value);
                }
            }

            foreach (TaxiEdgeDef e in edges)
            {
                if (!Has(nodes, e.From.Value))
                {
                    throw Ref("edges", e.Id.Value, "from", e.From.Value);
                }

                if (!Has(nodes, e.To.Value))
                {
                    throw Ref("edges", e.Id.Value, "to", e.To.Value);
                }
            }

            foreach (StandDef s in stands)
            {
                if (!Has(nodes, s.Node.Value))
                {
                    throw Ref("stands", s.Id.Value, "node", s.Node.Value);
                }
            }
        }

        private static TaxiNodeKind KindOf(TaxiNodeDef[] nodes, ushort id)
        {
            foreach (TaxiNodeDef n in nodes)
            {
                if (n.Id.Value == id)
                {
                    return n.Kind;
                }
            }

            return TaxiNodeKind.Junction;
        }

        private static void CheckKinds(RunwayDef[] runways, TaxiNodeDef[] nodes, StandDef[] stands)
        {
            foreach (RunwayDef r in runways)
            {
                if (KindOf(nodes, r.ThresholdNode.Value) != TaxiNodeKind.RunwayThreshold)
                {
                    throw new FormatException(Prefix + "runways: object " + N(r.Id.Value) + " field threshold_node names node " + N(r.ThresholdNode.Value) + " which is not a runway threshold");
                }

                if (r.ExitNode.Value != r.ThresholdNode.Value && KindOf(nodes, r.ExitNode.Value) != TaxiNodeKind.Junction)
                {
                    throw new FormatException(Prefix + "runways: object " + N(r.Id.Value) + " field exit_node names node " + N(r.ExitNode.Value) + " which is neither its threshold nor a junction");
                }
            }

            foreach (StandDef s in stands)
            {
                if (KindOf(nodes, s.Node.Value) != TaxiNodeKind.StandPosition)
                {
                    throw new FormatException(Prefix + "stands: object " + N(s.Id.Value) + " field node names node " + N(s.Node.Value) + " which is not a stand position");
                }
            }
        }

        private static void CheckConnected(RunwayDef[] runways, TaxiNodeDef[] nodes, TaxiEdgeDef[] edges)
        {
            int n = nodes.Length;
            var index = new Dictionary<ushort, int>();
            for (int i = 0; i < n; i++)
            {
                index.Add(nodes[i].Id.Value, i);
            }

            int r0 = -1;
            for (int i = 0; i < n; i++)
            {
                if (nodes[i].Kind == TaxiNodeKind.RunwayThreshold)
                {
                    r0 = i;
                    break;
                }
            }

            bool[] forward = Reach(n, edges, index, r0, false);
            bool[] backward = Reach(n, edges, index, r0, true);
            for (int i = 0; i < n; i++)
            {
                if (nodes[i].Kind != TaxiNodeKind.Junction && (!forward[i] || !backward[i]))
                {
                    throw new FormatException(Prefix + "nodes: node " + N(nodes[i].Id.Value) + " is not reachable from and to the lowest runway threshold");
                }
            }
        }

        /// <summary>Check 7 (Q-132): every stand node is reachable from each runway's distinct exit node.</summary>
        private static void CheckExitsReachStands(RunwayDef[] runways, TaxiNodeDef[] nodes, TaxiEdgeDef[] edges)
        {
            int n = nodes.Length;
            var index = new Dictionary<ushort, int>();
            for (int i = 0; i < n; i++)
            {
                index.Add(nodes[i].Id.Value, i);
            }

            foreach (RunwayDef r in runways)
            {
                if (r.ExitNode.Value == r.ThresholdNode.Value)
                {
                    continue;
                }

                bool[] seen = Reach(n, edges, index, index[r.ExitNode.Value], false);
                for (int i = 0; i < n; i++)
                {
                    if (nodes[i].Kind == TaxiNodeKind.StandPosition && !seen[i])
                    {
                        throw new FormatException(Prefix + "runways: object " + N(r.Id.Value) + " field exit_node names node " + N(r.ExitNode.Value) + " from which node " + N(nodes[i].Id.Value) + " is not reachable");
                    }
                }
            }
        }

        private static bool[] Reach(int n, TaxiEdgeDef[] edges, Dictionary<ushort, int> index, int start, bool reverse)
        {
            var seen = new bool[n];
            var stack = new Stack<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (TaxiEdgeDef e in edges)
                {
                    int a = index[e.From.Value];
                    int b = index[e.To.Value];
                    for (int dir = 0; dir < 2; dir++)
                    {
                        if (dir == 1 && !e.Bidirectional)
                        {
                            break;
                        }

                        int from = dir == 0 ? a : b;
                        int to = dir == 0 ? b : a;
                        if (reverse)
                        {
                            int t = from;
                            from = to;
                            to = t;
                        }

                        if (from == u && !seen[to])
                        {
                            seen[to] = true;
                            stack.Push(to);
                        }
                    }
                }
            }

            return seen;
        }
    }
}
