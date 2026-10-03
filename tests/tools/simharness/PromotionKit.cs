using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.World;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-014. The doubles of 19 §19.9 (Q-084) for HarnessGates.Promotion's second
    /// run (§19.2d), written from 09 §9.7, 18 and 19 §19.2d, never from an
    /// implementation.
    ///
    /// <para>The world and flow doubles of one composer call share one
    /// <see cref="CallLog"/>, so the order of the calls across both is
    /// asserted. Every entry carries the number of the double's own Tick calls
    /// so far, which is how the tests tell "before the first Step" from "after
    /// the k-th tick".</para>
    /// </summary>
    internal static class PromotionKit
    {
        internal const ulong Seed = 12345;

        /// <summary>§19.9: 1400 ticks record 3 checkpoints, at 0, 600 and 1200 (08 §8.9).</summary>
        internal const uint Ticks = 1400;

        /// <summary>The calls of one composer call's doubles, in order.</summary>
        internal sealed class CallLog
        {
            public readonly List<string> Entries = new List<string>();

            public void Add(string call, ulong ticksSeen)
            {
                Entries.Add(call + "@" + ticksSeen.ToString(CultureInfo.InvariantCulture));
            }
        }

        internal static string Nodes(ulong ticksSeen)
        {
            return "Nodes()@" + ticksSeen.ToString(CultureInfo.InvariantCulture);
        }

        internal static string KindOf(uint node, ulong ticksSeen)
        {
            return "KindOf(" + node.ToString(CultureInfo.InvariantCulture) + ")@" + ticksSeen.ToString(CultureInfo.InvariantCulture);
        }

        internal static string SetPromoted(uint node, bool promoted, ulong ticksSeen)
        {
            return "SetPromoted(" + node.ToString(CultureInfo.InvariantCulture) + "," + (promoted ? "true" : "false") + ")@"
                + ticksSeen.ToString(CultureInfo.InvariantCulture);
        }

        internal static string AgentsAt(uint node, ulong ticksSeen)
        {
            return "AgentsAt(" + node.ToString(CultureInfo.InvariantCulture) + ")@" + ticksSeen.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The whole run-2 log §19.2d prescribes when a gate is found: Nodes() once,
        /// KindOf over <paramref name="kindOfNodes"/>, one SetPromoted(gate, true)
        /// before the first tick, then <paramref name="ticks"/> AgentsAt(gate), the
        /// k-th after k ticks.
        /// </summary>
        internal static List<string> PromotedRunLog(uint[] kindOfNodes, uint gate, uint ticks)
        {
            var log = new List<string> { Nodes(0) };
            foreach (uint n in kindOfNodes)
            {
                log.Add(KindOf(n, 0));
            }

            log.Add(SetPromoted(gate, true, 0));
            for (uint k = 1; k <= ticks; k++)
            {
                log.Add(AgentsAt(gate, k));
            }

            return log;
        }

        /// <summary>
        /// A CountingComposer (19 §19.9) whose every call builds fresh doubles over
        /// a fresh <see cref="CallLog"/>, so run 1's and run 2's are told apart.
        /// </summary>
        internal sealed class Rig
        {
            public readonly List<CallLog> Logs = new List<CallLog>();

            public readonly HarnessTestKit.CountingComposer Composer;

            public Rig(Action<ISimHostBuilder, int, CallLog> register)
            {
                Composer = new HarnessTestKit.CountingComposer((b, call) =>
                {
                    var log = new CallLog();
                    Logs.Add(log);
                    register(b, call, log);
                });
            }
        }

        internal static uint[] Ids(params uint[] ids)
        {
            return ids;
        }

        /// <summary>
        /// §19.9's spy flow: an IFlowSystem with a chosen kind per node. It records
        /// every KindOf, SetPromoted and AgentsAt call, hashes 0, and its Tick only
        /// counts. Every other IFlowSystem member is logged and throws
        /// InvalidOperationException, so a harness that calls one fails the test
        /// even if it swallowed the exception.
        /// </summary>
        internal sealed class SpyFlow : IFlowSystem
        {
            private readonly ushort _position;
            private readonly Dictionary<uint, NodeKind> _kinds;
            private readonly CallLog _log;
            private ulong _ticks;

            public SpyFlow(CallLog log, IReadOnlyDictionary<uint, NodeKind> kinds, ushort position = 4)
            {
                _log = log;
                _kinds = new Dictionary<uint, NodeKind>(kinds.Count);
                foreach (KeyValuePair<uint, NodeKind> kv in kinds)
                {
                    _kinds.Add(kv.Key, kv.Value);
                }

                _position = position;
            }

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.flow";

            public void Tick(in TickContext ctx)
            {
                _ticks++;
            }

            public ulong ComputeStateHash()
            {
                return 0;
            }

            public NodeKind KindOf(NodeId node)
            {
                _log.Add("KindOf(" + node.Value.ToString(CultureInfo.InvariantCulture) + ")", _ticks);
                if (!_kinds.TryGetValue(node.Value, out NodeKind kind))
                {
                    throw new InvalidOperationException("spy flow: KindOf of node " + node.Value + ", which has no chosen kind");
                }

                return kind;
            }

            public void SetPromoted(NodeId node, bool promoted)
            {
                _log.Add("SetPromoted(" + node.Value.ToString(CultureInfo.InvariantCulture) + "," + (promoted ? "true" : "false") + ")", _ticks);
            }

            public IReadOnlyList<AgentView> AgentsAt(NodeId node)
            {
                _log.Add("AgentsAt(" + node.Value.ToString(CultureInfo.InvariantCulture) + ")", _ticks);
                return Array.Empty<AgentView>();
            }

            private InvalidOperationException Forbidden(string member)
            {
                _log.Add("FORBIDDEN " + member, _ticks);
                return new InvalidOperationException("spy flow: the harness called " + member + ", which 19 §19.2d does not allow");
            }

            public int Population(NodeId node)
            {
                throw Forbidden("Population");
            }

            public Fx PredictedWaitMinutes(NodeId node)
            {
                throw Forbidden("PredictedWaitMinutes");
            }

            public int PopulationForFlight(FlightId flight, FlowDirection direction)
            {
                throw Forbidden("PopulationForFlight");
            }

            public IReadOnlyList<CohortId> CohortsAt(NodeId node)
            {
                throw Forbidden("CohortsAt");
            }

            public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
            {
                throw Forbidden("TryGetCohort");
            }

            public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
            {
                throw Forbidden("TryGetOutstanding");
            }

            public bool TryGetLaneState(NodeId node, out LaneState lanes)
            {
                throw Forbidden("TryGetLaneState");
            }

            public CohortId Inject(in CohortKey key, int count, NodeId at)
            {
                throw Forbidden("Inject");
            }

            public int Absorb(NodeId sink, FlightId flight)
            {
                throw Forbidden("Absorb");
            }
        }

        /// <summary>
        /// §19.9's test world: an IWorldSystem at SystemId(1) with a chosen
        /// Nodes(). It records each Nodes() call and hashes 0. Every other
        /// IWorldSystem member is logged and throws InvalidOperationException.
        /// </summary>
        internal sealed class TestWorld : IWorldSystem
        {
            private readonly NodeId[] _nodes;
            private readonly CallLog _log;
            private ulong _ticks;

            public TestWorld(CallLog log, params uint[] nodes)
            {
                _log = log;
                _nodes = new NodeId[nodes.Length];
                for (int i = 0; i < nodes.Length; i++)
                {
                    _nodes[i] = new NodeId(nodes[i]);
                }
            }

            public SystemId Id => new SystemId(1);

            public string Name => "probe.world";

            public void Tick(in TickContext ctx)
            {
                _ticks++;
            }

            public ulong ComputeStateHash()
            {
                return 0;
            }

            public IReadOnlyList<NodeId> Nodes()
            {
                _log.Add("Nodes()", _ticks);
                return _nodes;
            }

            private InvalidOperationException Forbidden(string member)
            {
                _log.Add("FORBIDDEN " + member, _ticks);
                return new InvalidOperationException("test world: the harness called " + member + ", which 19 §19.2d does not allow");
            }

            public uint LengthMetres(NodeId node)
            {
                throw Forbidden("LengthMetres");
            }

            public IReadOnlyList<EdgeId> OutEdges(NodeId node)
            {
                throw Forbidden("OutEdges");
            }

            public NodeId EdgeTo(EdgeId edge)
            {
                throw Forbidden("EdgeTo");
            }

            public bool CanReach(NodeId from, NodeId destination)
            {
                throw Forbidden("CanReach");
            }

            public bool CanReachVia(EdgeId firstEdge, NodeId destination)
            {
                throw Forbidden("CanReachVia");
            }

            public IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
            {
                throw Forbidden("PathVia");
            }
        }

        // ------------------------------------------------------------ the Phase 0 kit, wrapped

        /// <summary>
        /// §19.9's forwarding spy over the kit's world: every member forwards,
        /// and nothing is recorded but the log's Nodes() entry. Id, Name, Tick and
        /// the hash forward, so the composition hashes as the kit does.
        /// </summary>
        internal sealed class ForwardingWorld : IWorldSystem
        {
            private readonly IWorldSystem _inner;

            public ForwardingWorld(IWorldSystem inner)
            {
                _inner = inner;
            }

            public int NodesCalls { get; private set; }

            public SystemId Id => _inner.Id;

            public string Name => _inner.Name;

            public void Tick(in TickContext ctx)
            {
                _inner.Tick(ctx);
            }

            public ulong ComputeStateHash()
            {
                return _inner.ComputeStateHash();
            }

            public IReadOnlyList<NodeId> Nodes()
            {
                NodesCalls++;
                return _inner.Nodes();
            }

            public uint LengthMetres(NodeId node)
            {
                return _inner.LengthMetres(node);
            }

            public IReadOnlyList<EdgeId> OutEdges(NodeId node)
            {
                return _inner.OutEdges(node);
            }

            public NodeId EdgeTo(EdgeId edge)
            {
                return _inner.EdgeTo(edge);
            }

            public bool CanReach(NodeId from, NodeId destination)
            {
                return _inner.CanReach(from, destination);
            }

            public bool CanReachVia(EdgeId firstEdge, NodeId destination)
            {
                return _inner.CanReachVia(firstEdge, destination);
            }

            public IReadOnlyList<NodeId> PathVia(EdgeId firstEdge, NodeId destination)
            {
                return _inner.PathVia(firstEdge, destination);
            }
        }

        /// <summary>
        /// §19.9's forwarding spy over the kit's flow. Every member forwards. It
        /// records KindOf, SetPromoted and AgentsAt in a <see cref="CallLog"/>,
        /// with its own Tick count. Inside each AgentsAt it also reads the wrapped
        /// flow's Population of the same node, a query (09 §9.7), and records the
        /// returned Count beside it.
        /// </summary>
        internal sealed class ForwardingFlow : IFlowSystem
        {
            private readonly IFlowSystem _inner;
            private readonly CallLog _log;
            private ulong _ticks;

            public ForwardingFlow(IFlowSystem inner, CallLog log)
            {
                _inner = inner;
                _log = log;
            }

            /// <summary>Each AgentsAt call's (returned Count, Population of that node), in order.</summary>
            public readonly List<(int Count, int Population)> Draws = new List<(int Count, int Population)>();

            public SystemId Id => _inner.Id;

            public string Name => _inner.Name;

            public void Tick(in TickContext ctx)
            {
                _inner.Tick(ctx);
                _ticks++;
            }

            public ulong ComputeStateHash()
            {
                return _inner.ComputeStateHash();
            }

            public NodeKind KindOf(NodeId node)
            {
                _log.Add("KindOf(" + node.Value.ToString(CultureInfo.InvariantCulture) + ")", _ticks);
                return _inner.KindOf(node);
            }

            public void SetPromoted(NodeId node, bool promoted)
            {
                _log.Add("SetPromoted(" + node.Value.ToString(CultureInfo.InvariantCulture) + "," + (promoted ? "true" : "false") + ")", _ticks);
                _inner.SetPromoted(node, promoted);
            }

            public IReadOnlyList<AgentView> AgentsAt(NodeId node)
            {
                _log.Add("AgentsAt(" + node.Value.ToString(CultureInfo.InvariantCulture) + ")", _ticks);
                IReadOnlyList<AgentView> views = _inner.AgentsAt(node);
                Draws.Add((views.Count, _inner.Population(node)));
                return views;
            }

            public int Population(NodeId node)
            {
                return _inner.Population(node);
            }

            public Fx PredictedWaitMinutes(NodeId node)
            {
                return _inner.PredictedWaitMinutes(node);
            }

            public int PopulationForFlight(FlightId flight, FlowDirection direction)
            {
                return _inner.PopulationForFlight(flight, direction);
            }

            public IReadOnlyList<CohortId> CohortsAt(NodeId node)
            {
                return _inner.CohortsAt(node);
            }

            public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
            {
                return _inner.TryGetCohort(id, out cohort);
            }

            public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
            {
                return _inner.TryGetOutstanding(flight, out outstanding);
            }

            public bool TryGetLaneState(NodeId node, out LaneState lanes)
            {
                return _inner.TryGetLaneState(node, out lanes);
            }

            public CohortId Inject(in CohortKey key, int count, NodeId at)
            {
                return _inner.Inject(key, count, at);
            }

            public int Absorb(NodeId sink, FlightId flight)
            {
                return _inner.Absorb(sink, flight);
            }
        }
    }
}
