using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow.Tests
{
    // Test doubles for the T-007 suite. 08 §8.11a (Q-014): sim.core publishes no
    // null ICheckpointSink or ISimLog, so tests write their own. Content is
    // built directly through ContentIndexFactory (08 §8.11: "Tests may skip the
    // loader"), with test-local profile values, never data/ balance values.

    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class RecordingSink : ICheckpointSink
    {
        public readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();

        public void Record(in Checkpoint cp)
        {
            Checkpoints.Add(cp);
        }
    }

    /// <summary>
    /// SplitMix64 as pinned by 08 §8.8, the input generator for property-style
    /// tests (07 L4). Always seeded with an integer literal inside the test.
    /// </summary>
    internal sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Integer in [lo, hi]; test inputs only.</summary>
        public int Range(int lo, int hi)
        {
            return lo + (int)(Next() % (ulong)(hi - lo + 1));
        }
    }

    /// <summary>
    /// A hand-built IWorldSystem. Routes follow 18 §18.3 by exhaustive search over
    /// simple paths (graphs here are tiny and every length is positive where a
    /// cycle exists). Every query is counted, and PathVia can be overridden, so a
    /// test can prove sim.flow takes its routes from the world and computes none
    /// itself (09 §9.6).
    /// </summary>
    internal sealed class FakeWorld : IWorldSystem
    {
        private readonly List<NodeId> _nodes = new List<NodeId>();
        private readonly Dictionary<uint, uint> _length = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, (uint From, uint To)> _edges = new Dictionary<uint, (uint From, uint To)>();
        private readonly Dictionary<uint, List<EdgeId>> _out = new Dictionary<uint, List<EdgeId>>();
        private readonly Dictionary<(uint, uint), NodeId[]> _pathOverride = new Dictionary<(uint, uint), NodeId[]>();

        public int PathViaCalls;
        public int CanReachCalls;
        public int CanReachViaCalls;

        public FakeWorld Node(uint id, uint lengthMetres)
        {
            _nodes.Add(new NodeId(id));
            _nodes.Sort((a, b) => a.Value.CompareTo(b.Value));
            _length[id] = lengthMetres;
            _out[id] = new List<EdgeId>();
            return this;
        }

        public FakeWorld Edge(uint id, uint from, uint to)
        {
            _edges[id] = (from, to);
            _out[from].Add(new EdgeId(id));
            _out[from].Sort((a, b) => a.Value.CompareTo(b.Value));
            return this;
        }

        /// <summary>Makes PathVia(edge, destination) answer this node list instead.</summary>
        public FakeWorld OverridePath(uint edge, uint destination, params uint[] nodes)
        {
            var list = new NodeId[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                list[i] = new NodeId(nodes[i]);
            }

            _pathOverride[(edge, destination)] = list;
            return this;
        }

        public SystemId Id => new SystemId(1);

        public string Name => "sim.world";

        public void Tick(in TickContext ctx)
        {
        }

        public ulong ComputeStateHash()
        {
            return 0x57021DUL;
        }

        public IReadOnlyList<NodeId> Nodes()
        {
            return _nodes;
        }

        public uint LengthMetres(NodeId node)
        {
            return _length.TryGetValue(node.Value, out uint l) ? l : throw new ArgumentException("unknown node " + node.Value);
        }

        public IReadOnlyList<EdgeId> OutEdges(NodeId node)
        {
            return _out.TryGetValue(node.Value, out List<EdgeId>? l) ? l : throw new ArgumentException("unknown node " + node.Value);
        }

        public NodeId EdgeTo(EdgeId edge)
        {
            return _edges.TryGetValue(edge.Value, out var e) ? new NodeId(e.To) : throw new ArgumentException("unknown edge " + edge.Value);
        }

        public bool CanReach(NodeId from, NodeId destination)
        {
            CanReachCalls++;
            LengthMetres(from);
            LengthMetres(destination);
            if (from == destination)
            {
                // 18 §18.3 (Q-031): the route from n to n is the empty path.
                return true;
            }

            IReadOnlyList<EdgeId> outs = _out[from.Value];
            for (int i = 0; i < outs.Count; i++)
            {
                if (Best(outs[i].Value, destination.Value) != null)
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanReachVia(EdgeId firstEdge, NodeId destination)
        {
            CanReachViaCalls++;
            EdgeTo(firstEdge);
            LengthMetres(destination);
            return Path(firstEdge.Value, destination.Value).Length > 0;
        }

        public IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
        {
            PathViaCalls++;
            EdgeTo(firstEdge);
            LengthMetres(destination);
            return Path(firstEdge.Value, destination.Value);
        }

        private NodeId[] Path(uint edge, uint destination)
        {
            if (_pathOverride.TryGetValue((edge, destination), out NodeId[]? p))
            {
                return p;
            }

            return Best(edge, destination) ?? Array.Empty<NodeId>();
        }

        private NodeId[]? Best(uint firstEdge, uint destination)
        {
            uint start = _edges[firstEdge].To;
            List<uint>? bestEdges = null;
            ulong bestCost = ulong.MaxValue;
            var edges = new List<uint> { firstEdge };
            var onPath = new HashSet<uint> { start };

            void Walk(uint at, ulong cost)
            {
                if (at == destination)
                {
                    if (bestEdges == null || cost < bestCost || (cost == bestCost && LexLess(edges, bestEdges)))
                    {
                        bestEdges = new List<uint>(edges);
                        bestCost = cost;
                    }

                    return;
                }

                foreach (EdgeId e in _out[at])
                {
                    uint to = _edges[e.Value].To;
                    if (onPath.Add(to))
                    {
                        edges.Add(e.Value);
                        Walk(to, cost + _length[to]);
                        edges.RemoveAt(edges.Count - 1);
                        onPath.Remove(to);
                    }
                }
            }

            Walk(start, _length[start]);
            if (bestEdges == null)
            {
                return null;
            }

            var nodes = new NodeId[bestEdges.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = new NodeId(_edges[bestEdges[i]].To);
            }

            return nodes;
        }

        private static bool LexLess(List<uint> a, List<uint> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i];
                }
            }

            return a.Count < b.Count;
        }
    }

    /// <summary>Records every flow event, in dispatch order, as text lines and structs.</summary>
    internal sealed class FlowEvents
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<(ulong Tick, QueueThresholdExceeded E)> Exceeded = new List<(ulong, QueueThresholdExceeded)>();
        public readonly List<(ulong Tick, QueueThresholdCleared E)> Cleared = new List<(ulong, QueueThresholdCleared)>();
        public readonly List<(ulong Tick, FlowBlocked E)> Blocked = new List<(ulong, FlowBlocked)>();
        public readonly List<(ulong Tick, FlowUnblocked E)> Unblocked = new List<(ulong, FlowUnblocked)>();
        public readonly List<(ulong Tick, PassengersArrivedAtGate E)> Arrived = new List<(ulong, PassengersArrivedAtGate)>();
        public readonly List<(ulong Tick, PassengersMissedFlight E)> Missed = new List<(ulong, PassengersMissedFlight)>();

        /// <summary>Subscribes as a probe at registry position 7 (sim.delay's, absent here).</summary>
        public FlowEvents(IEventBus bus, ushort subscriber = 7)
        {
            var id = new SystemId(subscriber);
            bus.Subscribe<QueueThresholdExceeded>(id, (in EventEnvelope env, in QueueThresholdExceeded e, in TickContext ctx) =>
            {
                Exceeded.Add((env.Tick, e));
                Log.Add(env.Tick + " exceeded n" + e.Node.Value + " w" + e.WaitMinutes.Raw + " " + e.ServersOpen + "/" + e.ServerCount);
            });
            bus.Subscribe<QueueThresholdCleared>(id, (in EventEnvelope env, in QueueThresholdCleared e, in TickContext ctx) =>
            {
                Cleared.Add((env.Tick, e));
                Log.Add(env.Tick + " cleared n" + e.Node.Value + " w" + e.WaitMinutes.Raw + " " + e.ServersOpen + "/" + e.ServerCount);
            });
            bus.Subscribe<FlowBlocked>(id, (in EventEnvelope env, in FlowBlocked e, in TickContext ctx) =>
            {
                Blocked.Add((env.Tick, e));
                Log.Add(env.Tick + " blocked c" + e.Cohort.Value + " n" + e.Held.Value + " by n" + e.BlockedBy.Value);
            });
            bus.Subscribe<FlowUnblocked>(id, (in EventEnvelope env, in FlowUnblocked e, in TickContext ctx) =>
            {
                Unblocked.Add((env.Tick, e));
                Log.Add(env.Tick + " unblocked c" + e.Cohort.Value + " n" + e.Held.Value + " by n" + e.BlockedBy.Value);
            });
            bus.Subscribe<PassengersArrivedAtGate>(id, (in EventEnvelope env, in PassengersArrivedAtGate e, in TickContext ctx) =>
            {
                Arrived.Add((env.Tick, e));
                Log.Add(env.Tick + " arrived f" + e.Flight.Value + " x" + e.Count);
            });
            bus.Subscribe<PassengersMissedFlight>(id, (in EventEnvelope env, in PassengersMissedFlight e, in TickContext ctx) =>
            {
                Missed.Add((env.Tick, e));
                Log.Add(env.Tick + " missed f" + e.Flight.Value + " x" + e.Count + " at n" + e.LastBlockedAt.Value);
            });
        }
    }

    internal static class FlowKit
    {
        public static readonly ContentId Walker = new ContentId("pax_test_walker");
        public static readonly ContentId Lane = new ContentId("queue_test_lane");

        /// <summary>A pax profile walking at the given metres per second (test value).</summary>
        public static PaxProfileDefinition Pax(ContentId id, Fx walkSpeedMps)
        {
            return new PaxProfileDefinition(id, walkSpeedMps, new[] { new ShowUpBucket(120, 1000) });
        }

        /// <summary>A queue profile with test-local values (never data/ balance values).</summary>
        public static QueueProfileDefinition Queue(ContentId id, Fx ratePerServerPerMinute, int capacityStanding, Fx thresholdMinutes, Fx hysteresisMinutes)
        {
            return new QueueProfileDefinition(id, ratePerServerPerMinute, capacityStanding, thresholdMinutes, hysteresisMinutes, DelayCategory.SecurityQueue);
        }

        public static IContentIndex Content(params IContentDefinition[] definitions)
        {
            return ContentIndexFactory.Create(definitions);
        }

        public static ISimHostBuilder Builder(IContentIndex content, ulong seed = 1, ICheckpointSink? sink = null)
        {
            return SimHostFactory.CreateBuilder(new SimHostConfig(seed, content, sink ?? new RecordingSink(), new NullLog()));
        }

        public static CohortKey Key(ulong flight, ContentId? pax = null, FlowDirection direction = FlowDirection.Departing, bool bags = false, bool assist = false)
        {
            return new CohortKey(new FlightId(flight), direction, pax ?? Walker, bags, assist);
        }

        public static byte[] SetServersOpenPayload(uint node, int count)
        {
            // 08 §8.7: NodeId.Value : uint32, count : int32, little-endian, 8 bytes.
            var b = new byte[8];
            BitConverter.TryWriteBytes(new Span<byte>(b, 0, 4), node);
            BitConverter.TryWriteBytes(new Span<byte>(b, 4, 4), count);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(b, 0, 4);
                Array.Reverse(b, 4, 4);
            }

            return b;
        }

        public static int TotalPopulation(IFlowSystem flow, IWorldSystem world)
        {
            int sum = 0;
            IReadOnlyList<NodeId> nodes = world.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                sum += flow.Population(nodes[i]);
            }

            return sum;
        }

        /// <summary>Sum of Count over every cohort on every node, read through TryGetCohort.</summary>
        public static int CohortHeadCount(IFlowSystem flow, IWorldSystem world)
        {
            int sum = 0;
            IReadOnlyList<NodeId> nodes = world.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                IReadOnlyList<CohortId> ids = flow.CohortsAt(nodes[i]);
                for (int k = 0; k < ids.Count; k++)
                {
                    if (!flow.TryGetCohort(ids[k], out PassengerCohort c))
                    {
                        throw new InvalidOperationException("CohortsAt listed an unknown cohort " + ids[k].Value);
                    }

                    sum += c.Count;
                }
            }

            return sum;
        }
    }
}
