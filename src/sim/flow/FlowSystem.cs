using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// The one <see cref="IFlowSystem"/> implementation. Spec: 09-interfaces-flow.md
    /// §9.1-§9.12 (Q-032, Q-033).
    /// </summary>
    internal sealed class FlowSystem : IFlowSystem
    {
        private static readonly Fx Epsilon = Fx.FromRatio(1, 1000);
        private static readonly SystemId FlowSystemId = new SystemId(4);

        private readonly IEventBus _events;
        private readonly IIdAllocator _ids;
        private readonly IContentIndex _content;
        private readonly IWorldSystem _world;

        // Per-node arrays, indexed by ordinal, ascending NodeId (world.Nodes() order).
        private readonly NodeId[] _nodeId;
        private readonly NodeKind[] _kind;
        private readonly int[] _serverCount;
        private readonly int[] _serversOpen;
        private readonly Fx[] _serviceRate;
        private readonly int[] _capacityStanding;
        private readonly Fx[] _thresholdWait;
        private readonly Fx[] _hysteresis;
        private readonly Fx[] _serviceCredit;
        private readonly bool[] _thresholdFlag;
        private readonly bool[] _promoted;
        private readonly List<int>[] _cohortsOnNode;
        private readonly Dictionary<uint, int> _ordinalByNodeValue;
        private readonly List<int> _gateOrdinals = new List<int>();

        // Per-tick snapshot (§9.12 step 1), sized to node count, reused every tick.
        private readonly int[] _snapshotPopulation;
        private readonly Fx[] _snapshotWait;

        // Pooled cohort storage. Generously pre-sized so that ordinary population
        // growth over a run never needs to grow these once warmed up: a resize
        // here is the one way this pool could allocate during a tick (§9.10 "no
        // allocation in the update path").
        private const int InitialSlotCapacity = 4096;
        private CohortSlot[] _slots = new CohortSlot[InitialSlotCapacity];
        private int _slotHighWater;
        private readonly List<int> _freeSlots = new List<int>(InitialSlotCapacity);
        private readonly Dictionary<ulong, int> _slotByCohortId = new Dictionary<ulong, int>(InitialSlotCapacity);

        // Reusable scratch buffers (never reallocated once grown, so a steady-state
        // tick allocates nothing: §9.10 "no allocation in the update path").
        private readonly List<int> _fifoScratch = new List<int>(InitialSlotCapacity);
        private readonly Comparison<int> _fifoComparer;

        // Routing cache (§9.6 "Routing cache", Q-036): the wait-dependent per-tick
        // memo of rule 3, keyed exactly as rule 3 states — "the same node, the same
        // walk speed, and the same value of every cohort field that determines the
        // destination set", which at Phase 0/1 is no field at all (only `Departing`
        // cohorts exist, Q-040, and a Departing cohort's destination set depends
        // only on the releasing node), so `(node, walk speed)` is the whole key.
        // `ComputeRoute`'s own parameter list is exactly `(fromOrdinal, walkSpeed)`,
        // so the memo cannot omit an input the function itself does not take.
        //
        // Rule 5 requires the memo to be "preallocated and reset each tick, never
        // grown", which a Dictionary sized by guesswork cannot promise once the
        // graph or the profile mix grows. Rule 2 lists "the loaded pax profiles'
        // walk_speed_mps" as one of the three sources a *static* table may be
        // built from once, at construction — so the distinct walk speeds are
        // enumerated once here, from content, and every possible (node, walk
        // speed) cell is preallocated as a flat array of exactly
        // nodeCount * distinctWalkSpeedCount slots. A walk speed WalkSpeedOf falls
        // back to that was never resolved from a loaded PaxProfile has no cell and
        // is simply never cached: a permanent miss, not a resize.
        private readonly Dictionary<long, int> _walkSpeedIndex;
        private readonly int _walkSpeedCount;
        private readonly bool[] _routeCacheValid;
        private readonly bool[] _routeCacheFound;
        private readonly EdgeId[] _routeCacheEdge;
        private readonly NodeId[] _routeCacheTarget;

        // Reused by Absorb's missed-passenger sweep to collect every missed slot
        // across nodes before sorting by ascending CohortId (§9.7 "Exact rules"),
        // so the FlowUnblocked publish order does not depend on NodeId iteration
        // order or per-node list order (Absorb is not itself in the hot per-tick
        // path, but this keeps the same no-allocation-once-grown discipline).
        private readonly List<int> _missedScratch = new List<int>(InitialSlotCapacity);
        private readonly Comparison<int> _cohortIdComparer;

        // Per-flight index for TryGetOutstanding (§9.7a, §9.10 "per-flight population
        // indexes"): derived, not hashed, kept in lock-step with every cohort mutation
        // that touches a Departing cohort on an included (non-Gate, non-Sink) node, so
        // the query itself never scans nodes or cohorts (O(the flight's cohorts) only)
        // and allocates nothing. A flight's entry always returns to zero and frees back
        // to the pool once every one of its Departing cohorts has either boarded (§9.7
        // "leave the simulation", so it never lands as a live Sink cohort) or been
        // reported missed and removed — so the number of concurrently live entries is
        // bounded by flights still in transit, not by flights ever seen. Pre-stocking
        // the pool at construction, like the slot pool above, is a warm-up nicety, not
        // a workaround for an unbounded quantity.
        private const int InitialFlightCapacity = 512;
        private readonly Dictionary<ulong, FlightOutstanding> _outstandingByFlight = new Dictionary<ulong, FlightOutstanding>(InitialFlightCapacity);
        private readonly Stack<FlightOutstanding> _outstandingPool = new Stack<FlightOutstanding>(InitialFlightCapacity);

        // Reused buffer for AgentsAt (§9.7 "Allocation": "AgentsAt allocates nothing
        // after warm-up ... its buffer grows only when a node's population exceeds
        // every earlier one"). One buffer suffices because its result is valid only
        // until the next AgentsAt or Tick call.
        private readonly AgentViewBuffer _agentViewBuffer = new AgentViewBuffer();

        private ulong _ticksCompleted;

        /// <summary>Per-flight aggregate for TryGetOutstanding: total count and count by node ordinal.</summary>
        private sealed class FlightOutstanding
        {
            internal int TotalCount;
            internal readonly Dictionary<int, int> ByOrdinal;

            // Sized to the node count at construction: the worst case for one
            // flight's spread of non-Gate ordinals is every node in the graph, so
            // this never needs to grow again once warmed up (§9.10).
            internal FlightOutstanding(int nodeCount)
            {
                ByOrdinal = new Dictionary<int, int>(nodeCount);
            }
        }

        /// <summary>A reusable, index-stable list of <see cref="AgentView"/>, backing <see cref="AgentsAt"/>.</summary>
        private sealed class AgentViewBuffer : IReadOnlyList<AgentView>
        {
            internal AgentView[] Items = Array.Empty<AgentView>();
            internal int Length;

            public AgentView this[int index] => Items[index];

            public int Count => Length;

            public IEnumerator<AgentView> GetEnumerator()
            {
                for (int i = 0; i < Length; i++)
                {
                    yield return Items[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        internal FlowSystem(in SystemServices services, in FlowGraph graph, IWorldSystem world)
        {
            _events = services.Events;
            _ids = services.Ids;
            _content = services.Content;
            _world = world;

            IReadOnlyList<FlowNodeDef> defs = graph.Nodes;
            int n = defs.Count;
            _nodeId = new NodeId[n];
            _kind = new NodeKind[n];
            _serverCount = new int[n];
            _serversOpen = new int[n];
            _serviceRate = new Fx[n];
            _capacityStanding = new int[n];
            _thresholdWait = new Fx[n];
            _hysteresis = new Fx[n];
            _serviceCredit = new Fx[n];
            _thresholdFlag = new bool[n];
            _promoted = new bool[n];
            _cohortsOnNode = new List<int>[n];
            _ordinalByNodeValue = new Dictionary<uint, int>(n);
            _snapshotPopulation = new int[n];
            _snapshotWait = new Fx[n];
            _fifoComparer = CompareFifo;
            _cohortIdComparer = CompareCohortId;

            for (int i = 0; i < n; i++)
            {
                FlowNodeDef def = defs[i];
                _nodeId[i] = def.Id;
                _kind[i] = def.Kind;
                _cohortsOnNode[i] = new List<int>(16);
                _ordinalByNodeValue[def.Id.Value] = i;

                if (def.Kind == NodeKind.Gate)
                {
                    _gateOrdinals.Add(i);
                }

                if (def.Kind == NodeKind.Queue)
                {
                    _serverCount[i] = def.ServerCount;
                    _serversOpen[i] = def.ServersOpen;
                    if (!_content.TryGet(def.QueueProfile, out QueueProfileDefinition profile))
                    {
                        throw new FormatException("sim.flow: node " + def.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " names an unresolved queue_profile '" + def.QueueProfile.Value + "'");
                    }

                    _serviceRate[i] = profile.ServiceRatePerServerPerMinute;
                    _capacityStanding[i] = profile.CapacityStanding;
                    _thresholdWait[i] = profile.ThresholdWaitMinutes;
                    _hysteresis[i] = profile.HysteresisMinutes;
                }
            }

            // Route cache (§9.6 "Routing cache", rule 2, Q-036): the distinct walk
            // speeds are a static, construction-time source, enumerated here once
            // so the per-tick memo below can be sized exactly, with no risk of
            // growing later (rule 5).
            _walkSpeedIndex = new Dictionary<long, int>();
            IReadOnlyList<ContentId> paxProfiles = _content.AllOf(ContentKind.PaxProfile);
            for (int i = 0; i < paxProfiles.Count; i++)
            {
                if (_content.TryGet(paxProfiles[i], out PaxProfileDefinition profileDef))
                {
                    long raw = profileDef.WalkSpeedMps.Raw;
                    if (!_walkSpeedIndex.ContainsKey(raw))
                    {
                        _walkSpeedIndex[raw] = _walkSpeedIndex.Count;
                    }
                }
            }

            _walkSpeedCount = _walkSpeedIndex.Count;
            int cacheSlots = n * _walkSpeedCount;
            _routeCacheValid = new bool[cacheSlots];
            _routeCacheFound = new bool[cacheSlots];
            _routeCacheEdge = new EdgeId[cacheSlots];
            _routeCacheTarget = new NodeId[cacheSlots];

            for (int i = 0; i < InitialFlightCapacity; i++)
            {
                _outstandingPool.Push(new FlightOutstanding(n));
            }

            services.Commands.Register(FlowSystemId, new SetServersOpenHandler(this));

            // Warms each event type's channel now, at construction, rather than
            // lazily on this run's first real publish of that type (which could
            // otherwise land inside a budget measurement window and allocate
            // there instead). The handlers are no-ops: this only adds a silent
            // extra subscriber, never a spurious event.
            services.Events.Subscribe<QueueThresholdExceeded>(FlowSystemId, NoOpHandler);
            services.Events.Subscribe<QueueThresholdCleared>(FlowSystemId, NoOpHandler);
            services.Events.Subscribe<FlowBlocked>(FlowSystemId, NoOpHandler);
            services.Events.Subscribe<FlowUnblocked>(FlowSystemId, NoOpHandler);
            services.Events.Subscribe<PassengersArrivedAtGate>(FlowSystemId, NoOpHandler);
            services.Events.Subscribe<PassengersMissedFlight>(FlowSystemId, NoOpHandler);
        }

        private static void NoOpHandler<T>(in EventEnvelope envelope, in T evt, in TickContext ctx) where T : struct, ISimEvent
        {
        }

        public SystemId Id => FlowSystemId;

        public string Name => "sim.flow";

        // -------------------------------------------------------------- ISimSystem

        public void Tick(in TickContext ctx)
        {
            ulong t = ctx.Tick;
            int n = _nodeId.Length;

            for (int i = 0; i < n; i++)
            {
                _snapshotPopulation[i] = ComputeLivePopulation(i);
                _snapshotWait[i] = _kind[i] == NodeKind.Queue ? ComputeWait(i, _snapshotPopulation[i]) : Fx.Zero;
            }

            // The snapshot above is what §9.6 routing costs read; a route decision
            // computed against it stays valid for the rest of this tick (§9.12).
            // Rule 5's "preallocated and reset each tick": only the validity flags
            // are cleared, never the arrays themselves.
            Array.Clear(_routeCacheValid, 0, _routeCacheValid.Length);

            for (int i = 0; i < n; i++)
            {
                switch (_kind[i])
                {
                    case NodeKind.Source:
                    case NodeKind.Hall:
                        ProcessSourceOrHall(i, t);
                        break;
                    case NodeKind.Corridor:
                        ProcessCorridor(i, t);
                        break;
                    case NodeKind.Queue:
                        ProcessQueue(i, t);
                        break;
                    default:
                        break;
                }
            }

            for (int i = 0; i < n; i++)
            {
                MergeNode(i);
            }

            for (int i = 0; i < n; i++)
            {
                if (_kind[i] == NodeKind.Queue)
                {
                    EvaluateThreshold(i, t);
                }
            }

            _ticksCompleted = t + 1;
        }

        public ulong ComputeStateHash()
        {
            var hasher = new StateHasher();
            int n = _nodeId.Length;
            for (int i = 0; i < n; i++)
            {
                bool queue = _kind[i] == NodeKind.Queue;
                hasher.Feed((ulong)(queue ? _serversOpen[i] : 0));
                hasher.Feed(queue ? _serviceCredit[i] : Fx.Zero);
                hasher.Feed((ulong)ComputeLivePopulation(i));
                hasher.Feed(IsAnyBlocked(i));
                hasher.Feed(_thresholdFlag[i]);
            }

            var live = new List<int>();
            for (int i = 0; i < _slotHighWater; i++)
            {
                if (_slots[i].Alive)
                {
                    live.Add(i);
                }
            }

            live.Sort((a, b) => _slots[a].Id.CompareTo(_slots[b].Id));

            for (int k = 0; k < live.Count; k++)
            {
                CohortSlot s = _slots[live[k]];
                hasher.Feed(s.Id);
                hasher.Feed(s.Key.Flight.Value);
                hasher.Feed((ulong)(int)s.Key.Direction);
                hasher.Feed(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(s.Key.PaxProfile.Value ?? string.Empty)));
                hasher.Feed(s.Key.HasHoldBaggage);
                hasher.Feed(s.Key.RequiresAssistance);
                hasher.Feed(_nodeId[s.NodeOrdinal].Value);
                hasher.Feed((ulong)s.Count);
                hasher.Feed(s.EnteredNodeAt);
                hasher.Feed(s.DueAt);
                hasher.Feed(Fx.Zero);
            }

            return hasher.Result;
        }

        // -------------------------------------------------------------- queries

        public int Population(NodeId node)
        {
            int ordinal = RequireOrdinal(node, nameof(node));
            return ComputeLivePopulation(ordinal);
        }

        public Fx PredictedWaitMinutes(NodeId node)
        {
            int ordinal = RequireOrdinal(node, nameof(node));
            if (_kind[ordinal] != NodeKind.Queue)
            {
                return Fx.Zero;
            }

            return ComputeWait(ordinal, ComputeLivePopulation(ordinal));
        }

        public int PopulationForFlight(FlightId flight, FlowDirection direction)
        {
            int total = 0;
            for (int i = 0; i < _nodeId.Length; i++)
            {
                List<int> list = _cohortsOnNode[i];
                for (int k = 0; k < list.Count; k++)
                {
                    CohortSlot s = _slots[list[k]];
                    if (s.Key.Flight.Equals(flight) && s.Key.Direction == direction)
                    {
                        total += s.Count;
                    }
                }
            }

            return total;
        }

        public IReadOnlyList<CohortId> CohortsAt(NodeId node)
        {
            if (!_ordinalByNodeValue.TryGetValue(node.Value, out int ordinal))
            {
                return Array.Empty<CohortId>();
            }

            List<int> list = _cohortsOnNode[ordinal];
            var result = new CohortId[list.Count];
            for (int k = 0; k < list.Count; k++)
            {
                result[k] = new CohortId(_slots[list[k]].Id);
            }

            return result;
        }

        public bool TryGetCohort(CohortId id, out PassengerCohort cohort)
        {
            if (_slotByCohortId.TryGetValue(id.Value, out int slot) && _slots[slot].Alive)
            {
                CohortSlot s = _slots[slot];
                cohort = new PassengerCohort(new CohortId(s.Id), s.Key, _nodeId[s.NodeOrdinal], s.Count, s.EnteredNodeAt, s.DueAt, Fx.Zero);
                return true;
            }

            cohort = default;
            return false;
        }

        public bool TryGetOutstanding(FlightId flight, out OutstandingPassengers outstanding)
        {
            // O(the flight's cohorts), served from the per-flight index (§9.7a):
            // never scans all nodes or all cohorts.
            if (!_outstandingByFlight.TryGetValue(flight.Value, out FlightOutstanding entry) || entry.TotalCount <= 0)
            {
                outstanding = default;
                return false;
            }

            int bestOrdinal = -1;
            int bestCount = 0;
            foreach (KeyValuePair<int, int> kv in entry.ByOrdinal)
            {
                int ordinal = kv.Key;
                int count = kv.Value;
                bool better = bestOrdinal < 0
                    || count > bestCount
                    || (count == bestCount && _nodeId[ordinal].Value < _nodeId[bestOrdinal].Value);
                if (better)
                {
                    bestOrdinal = ordinal;
                    bestCount = count;
                }
            }

            outstanding = new OutstandingPassengers(flight, entry.TotalCount, _nodeId[bestOrdinal]);
            return true;
        }

        /// <summary>
        /// §9.7a counts a flight's Departing passengers "on any node whose NodeKind
        /// is not Gate" — but a passenger who has reached a Gate or been boarded onto
        /// a Sink is no longer "still in the terminal", so both are excluded the same
        /// way, matching §9.7's "leave the simulation" for a Sink specifically.
        /// </summary>
        private static bool ExcludedFromOutstanding(NodeKind kind) => kind == NodeKind.Gate || kind == NodeKind.Sink;

        /// <summary>
        /// Adds <paramref name="amount"/> Departing passengers of <paramref name="key"/>'s
        /// flight to the outstanding index at <paramref name="ordinal"/>, unless the node
        /// is excluded or the cohort is not Departing (§9.7a only counts those).
        /// </summary>
        private void OutstandingAdd(int ordinal, in CohortKey key, int amount)
        {
            if (amount <= 0 || key.Direction != FlowDirection.Departing || ExcludedFromOutstanding(_kind[ordinal]))
            {
                return;
            }

            if (!_outstandingByFlight.TryGetValue(key.Flight.Value, out FlightOutstanding entry))
            {
                if (_outstandingPool.Count > 0)
                {
                    entry = _outstandingPool.Pop();
                }
                else
                {
                    entry = new FlightOutstanding(_nodeId.Length);
                }

                _outstandingByFlight[key.Flight.Value] = entry;
            }

            entry.TotalCount += amount;
            entry.ByOrdinal.TryGetValue(ordinal, out int existing);
            entry.ByOrdinal[ordinal] = existing + amount;
        }

        /// <summary>The inverse of <see cref="OutstandingAdd"/>.</summary>
        private void OutstandingRemove(int ordinal, in CohortKey key, int amount)
        {
            if (amount <= 0 || key.Direction != FlowDirection.Departing || ExcludedFromOutstanding(_kind[ordinal]))
            {
                return;
            }

            if (!_outstandingByFlight.TryGetValue(key.Flight.Value, out FlightOutstanding entry))
            {
                return;
            }

            entry.TotalCount -= amount;
            int remaining = entry.ByOrdinal[ordinal] - amount;
            if (remaining > 0)
            {
                entry.ByOrdinal[ordinal] = remaining;
            }
            else
            {
                entry.ByOrdinal.Remove(ordinal);
            }

            if (entry.TotalCount <= 0)
            {
                _outstandingByFlight.Remove(key.Flight.Value);
                entry.ByOrdinal.Clear();
                _outstandingPool.Push(entry);
            }
        }

        public bool TryGetLaneState(NodeId node, out LaneState lanes)
        {
            if (_ordinalByNodeValue.TryGetValue(node.Value, out int ordinal) && _kind[ordinal] == NodeKind.Queue)
            {
                lanes = new LaneState(_serverCount[ordinal], _serversOpen[ordinal]);
                return true;
            }

            lanes = default;
            return false;
        }

        // -------------------------------------------------------------- mutation

        public CohortId Inject(in CohortKey key, int count, NodeId at)
        {
            if (!_ordinalByNodeValue.TryGetValue(at.Value, out int ordinal))
            {
                throw new ArgumentException("Inject: unknown node " + at.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), nameof(at));
            }

            if (_kind[ordinal] != NodeKind.Source)
            {
                throw new ArgumentException("Inject: node " + at.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a Source", nameof(at));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "count must be positive");
            }

            if (key.Direction != FlowDirection.Departing)
            {
                throw new ArgumentException("Inject: only Departing cohorts exist at Phase 0/1", nameof(key));
            }

            ulong id = NewCohortId();
            int slot = AllocateSlot();
            _slots[slot] = new CohortSlot
            {
                Alive = true,
                Id = id,
                Key = key,
                NodeOrdinal = ordinal,
                Count = count,
                EnteredNodeAt = _ticksCompleted,
                DueAt = _ticksCompleted,
                Blocked = false,
            };
            _cohortsOnNode[ordinal].Add(slot);
            _slotByCohortId[id] = slot;
            OutstandingAdd(ordinal, key, count);
            return new CohortId(id);
        }

        public int Absorb(NodeId sink, FlightId flight)
        {
            if (!_ordinalByNodeValue.TryGetValue(sink.Value, out int sinkOrdinal))
            {
                throw new ArgumentException("Absorb: unknown node " + sink.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), nameof(sink));
            }

            if (_kind[sinkOrdinal] != NodeKind.Sink)
            {
                throw new ArgumentException("Absorb: node " + sink.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not a Sink", nameof(sink));
            }

            // Reuses IEventBus.Publish's own phase check (08 §8.6) as the "outside a
            // tick" guard (Q-033): Absorb has no TickContext of its own.
            _events.Publish(default(AbsorbPhaseGuard), EventRef.None);

            bool hasOutstanding = TryGetOutstanding(flight, out OutstandingPassengers outstanding);

            // §9.7 "Exact rules": boarded cohorts "leave the simulation" — they are
            // removed outright, never relocated onto sink as a live cohort. sink is
            // still required to name a real Sink node (validated above), but nothing
            // is ever placed on it.
            int boarded = 0;
            for (int g = 0; g < _gateOrdinals.Count; g++)
            {
                int gateOrdinal = _gateOrdinals[g];
                List<int> list = _cohortsOnNode[gateOrdinal];
                for (int idx = list.Count - 1; idx >= 0; idx--)
                {
                    int slot = list[idx];
                    if (_slots[slot].Key.Direction == FlowDirection.Departing && _slots[slot].Key.Flight.Equals(flight))
                    {
                        boarded += _slots[slot].Count;
                        RemoveCohortEntirely(slot, gateOrdinal);
                    }
                }
            }

            if (hasOutstanding)
            {
                // §9.7 "Exact rules", in this order: FlowUnblocked for each missed
                // cohort in an open blocking episode, in ascending CohortId across
                // every node; then one PassengersMissedFlight; then the removal.
                // Collecting first keeps the publish order independent of NodeId
                // iteration order and of each node's own cohort-list order.
                _missedScratch.Clear();
                for (int i = 0; i < _nodeId.Length; i++)
                {
                    if (_kind[i] == NodeKind.Gate)
                    {
                        continue;
                    }

                    List<int> list = _cohortsOnNode[i];
                    for (int idx = 0; idx < list.Count; idx++)
                    {
                        int slot = list[idx];
                        if (_slots[slot].Key.Direction == FlowDirection.Departing && _slots[slot].Key.Flight.Equals(flight))
                        {
                            _missedScratch.Add(slot);
                        }
                    }
                }

                _missedScratch.Sort(_cohortIdComparer);

                for (int k = 0; k < _missedScratch.Count; k++)
                {
                    int slot = _missedScratch[k];
                    if (_slots[slot].Blocked)
                    {
                        int ordinal = _slots[slot].NodeOrdinal;
                        _events.Publish(new FlowUnblocked(new CohortId(_slots[slot].Id), _nodeId[ordinal], _nodeId[_slots[slot].BlockedByOrdinal]), EventRef.None);
                    }
                }

                _events.Publish(new PassengersMissedFlight(flight, outstanding.Count, outstanding.MostHeldAt), EventRef.None);

                for (int k = 0; k < _missedScratch.Count; k++)
                {
                    int slot = _missedScratch[k];
                    int ordinal = _slots[slot].NodeOrdinal;
                    OutstandingRemove(ordinal, _slots[slot].Key, _slots[slot].Count);
                    _cohortsOnNode[ordinal].Remove(slot);
                    _slotByCohortId.Remove(_slots[slot].Id);
                    FreeSlot(slot);
                }
            }

            return boarded;
        }

        public void SetPromoted(NodeId node, bool promoted)
        {
            int ordinal = RequireOrdinal(node, nameof(node));
            _promoted[ordinal] = promoted;
        }

        public NodeKind KindOf(NodeId node) => _kind[RequireOrdinal(node, nameof(node))];

        public IReadOnlyList<AgentView> AgentsAt(NodeId node)
        {
            int ordinal = RequireOrdinal(node, nameof(node));
            if (!_promoted[ordinal])
            {
                _agentViewBuffer.Length = 0;
                return _agentViewBuffer;
            }

            // Exactly one view per passenger (§9.7): for each cohort, in the node's
            // ascending-CohortId order, Index runs 0 .. Count - 1.
            List<int> list = _cohortsOnNode[ordinal];
            int total = 0;
            for (int k = 0; k < list.Count; k++)
            {
                total += _slots[list[k]].Count;
            }

            if (_agentViewBuffer.Items.Length < total)
            {
                Array.Resize(ref _agentViewBuffer.Items, total);
            }

            int w = 0;
            for (int k = 0; k < list.Count; k++)
            {
                CohortSlot s = _slots[list[k]];
                var cohortId = new CohortId(s.Id);
                for (int idx = 0; idx < s.Count; idx++)
                {
                    _agentViewBuffer.Items[w++] = new AgentView(new PassengerRef(cohortId, idx), node, Fx.Zero);
                }
            }

            _agentViewBuffer.Length = total;
            return _agentViewBuffer;
        }

        // -------------------------------------------------------------- internal seams (SetServersOpenHandler)

        internal bool TryOrdinalOf(NodeId node, out int ordinal) => _ordinalByNodeValue.TryGetValue(node.Value, out ordinal);

        internal NodeKind KindOf(int ordinal) => _kind[ordinal];

        internal void ApplySetServersOpen(int ordinal, int count)
        {
            int clamped = Math.Max(0, Math.Min(count, _serverCount[ordinal]));
            _serversOpen[ordinal] = clamped;
        }

        // -------------------------------------------------------------- movement (§9.12)

        private void ProcessSourceOrHall(int ordinal, ulong t)
        {
            List<int> list = _cohortsOnNode[ordinal];
            int idx = 0;
            while (idx < list.Count)
            {
                int slot = list[idx];
                if (_slots[slot].EnteredNodeAt >= t)
                {
                    idx++;
                    continue;
                }

                if (AttemptRelease(slot, ordinal, t, _slots[slot].Count))
                {
                    // The slot was removed from this list; do not advance idx.
                }
                else
                {
                    idx++;
                }
            }
        }

        private void ProcessCorridor(int ordinal, ulong t)
        {
            List<int> list = _cohortsOnNode[ordinal];
            int idx = 0;
            while (idx < list.Count)
            {
                int slot = list[idx];
                if (_slots[slot].DueAt > t)
                {
                    idx++;
                    continue;
                }

                if (AttemptRelease(slot, ordinal, t, _slots[slot].Count))
                {
                }
                else
                {
                    idx++;
                }
            }
        }

        private void ProcessQueue(int ordinal, ulong t)
        {
            Fx rate = _serviceRate[ordinal];
            long serversOpenSeconds = (long)_serversOpen[ordinal] * SimConstants.SIM_SECONDS_PER_TICK;
            Fx capacityThisTick = Fx.Div(Fx.Mul(Fx.FromInt(serversOpenSeconds), rate), Fx.FromInt(60));
            Fx serverTick = Fx.Div(Fx.Mul(Fx.FromInt(SimConstants.SIM_SECONDS_PER_TICK), rate), Fx.FromInt(60));

            _serviceCredit[ordinal] = Fx.Add(_serviceCredit[ordinal], capacityThisTick);
            long served = Fx.Floor(_serviceCredit[ordinal]);
            _serviceCredit[ordinal] = Fx.Sub(_serviceCredit[ordinal], Fx.FromInt(served));

            List<int> list = _cohortsOnNode[ordinal];
            _fifoScratch.Clear();
            for (int k = 0; k < list.Count; k++)
            {
                if (_slots[list[k]].EnteredNodeAt < t)
                {
                    _fifoScratch.Add(list[k]);
                }
            }

            _fifoScratch.Sort(_fifoComparer);

            long moved = 0;
            for (int k = 0; k < _fifoScratch.Count && moved < served; k++)
            {
                int slot = _fifoScratch[k];
                long remainingCredit = served - moved;
                int take = (int)Math.Min(remainingCredit, (long)_slots[slot].Count);
                if (take <= 0)
                {
                    continue;
                }

                if (AttemptRelease(slot, ordinal, t, take))
                {
                    moved += take;
                }
                else
                {
                    break;
                }
            }

            if (moved < served)
            {
                _serviceCredit[ordinal] = Fx.Min(_serviceCredit[ordinal], serverTick);
            }
        }

        private int CompareFifo(int a, int b)
        {
            int c = _slots[a].EnteredNodeAt.CompareTo(_slots[b].EnteredNodeAt);
            return c != 0 ? c : _slots[a].Id.CompareTo(_slots[b].Id);
        }

        private int CompareCohortId(int a, int b) => _slots[a].Id.CompareTo(_slots[b].Id);

        /// <summary>
        /// Routes, checks spillback and moves <paramref name="amount"/> passengers out
        /// of <paramref name="slot"/>, handling the blocking-episode transitions of §9.12.
        /// Returns false, with nothing moved, if refused or unrouteable.
        /// </summary>
        private bool AttemptRelease(int slot, int fromOrdinal, ulong t, int amount)
        {
            CohortKey key = _slots[slot].Key;
            Fx walkSpeed = WalkSpeedOf(key.PaxProfile);
            if (!TryRoute(fromOrdinal, walkSpeed, out EdgeId edge, out NodeId targetNode))
            {
                return false;
            }

            int targetOrdinal = _ordinalByNodeValue[targetNode.Value];
            bool full = _kind[targetOrdinal] == NodeKind.Queue && _snapshotPopulation[targetOrdinal] >= _capacityStanding[targetOrdinal];

            if (full)
            {
                if (!_slots[slot].Blocked)
                {
                    _slots[slot].Blocked = true;
                    _slots[slot].BlockedByOrdinal = targetOrdinal;
                    _events.Publish(new FlowBlocked(new CohortId(_slots[slot].Id), _nodeId[fromOrdinal], targetNode), EventRef.None);
                }
                else if (_slots[slot].BlockedByOrdinal != targetOrdinal)
                {
                    _events.Publish(new FlowUnblocked(new CohortId(_slots[slot].Id), _nodeId[fromOrdinal], _nodeId[_slots[slot].BlockedByOrdinal]), EventRef.None);
                    _slots[slot].BlockedByOrdinal = targetOrdinal;
                    _events.Publish(new FlowBlocked(new CohortId(_slots[slot].Id), _nodeId[fromOrdinal], targetNode), EventRef.None);
                }

                return false;
            }

            if (_slots[slot].Blocked)
            {
                _events.Publish(new FlowUnblocked(new CohortId(_slots[slot].Id), _nodeId[fromOrdinal], _nodeId[_slots[slot].BlockedByOrdinal]), EventRef.None);
                _slots[slot].Blocked = false;
            }

            FlightId flight = key.Flight;
            MoveCohortPortion(slot, fromOrdinal, targetOrdinal, t, amount);

            if (_kind[targetOrdinal] == NodeKind.Gate)
            {
                _events.Publish(new PassengersArrivedAtGate(flight, amount), EventRef.None);
            }

            return true;
        }

        private void MoveCohortPortion(int slot, int fromOrdinal, int toOrdinal, ulong t, int amount)
        {
            CohortKey key = _slots[slot].Key;
            ulong dueAt = _kind[toOrdinal] == NodeKind.Corridor
                ? t + (ulong)TraversalTicks(_nodeId[toOrdinal], WalkSpeedOf(key.PaxProfile))
                : t;

            int newSlot = AllocateSlot();
            ulong newId = NewCohortId();
            _slots[newSlot] = new CohortSlot
            {
                Alive = true,
                Id = newId,
                Key = key,
                NodeOrdinal = toOrdinal,
                Count = amount,
                EnteredNodeAt = t,
                DueAt = dueAt,
                Blocked = false,
            };
            _cohortsOnNode[toOrdinal].Add(newSlot);
            _slotByCohortId[newId] = newSlot;
            OutstandingAdd(toOrdinal, key, amount);

            _slots[slot].Count -= amount;
            OutstandingRemove(fromOrdinal, key, amount);
            if (_slots[slot].Count == 0)
            {
                _cohortsOnNode[fromOrdinal].Remove(slot);
                _slotByCohortId.Remove(_slots[slot].Id);
                FreeSlot(slot);
            }
        }

        private void RemoveCohortEntirely(int slot, int ordinal)
        {
            if (_slots[slot].Blocked)
            {
                _events.Publish(new FlowUnblocked(new CohortId(_slots[slot].Id), _nodeId[ordinal], _nodeId[_slots[slot].BlockedByOrdinal]), EventRef.None);
            }

            OutstandingRemove(ordinal, _slots[slot].Key, _slots[slot].Count);
            _cohortsOnNode[ordinal].Remove(slot);
            _slotByCohortId.Remove(_slots[slot].Id);
            FreeSlot(slot);
        }

        private void MergeNode(int ordinal)
        {
            List<int> list = _cohortsOnNode[ordinal];
            bool corridor = _kind[ordinal] == NodeKind.Corridor;
            for (int i = 0; i < list.Count; i++)
            {
                int slotA = list[i];
                if (_slots[slotA].Blocked)
                {
                    continue;
                }

                for (int j = i + 1; j < list.Count;)
                {
                    int slotB = list[j];
                    bool sameKey = !_slots[slotB].Blocked && _slots[slotA].Key.Equals(_slots[slotB].Key);

                    // On a Corridor, two cohorts merge only if their DueAt also matches,
                    // so a merge never moves a release tick (Q-033); a Corridor can
                    // therefore hold up to traversalTicks live cohorts of one key.
                    // Elsewhere DueAt already equals EnteredNodeAt on both sides, so this
                    // is never a real restriction there.
                    bool sameDue = !corridor || _slots[slotA].DueAt == _slots[slotB].DueAt;

                    if (sameKey && sameDue)
                    {
                        _slots[slotA].Count += _slots[slotB].Count;
                        if (_slots[slotB].EnteredNodeAt < _slots[slotA].EnteredNodeAt)
                        {
                            _slots[slotA].EnteredNodeAt = _slots[slotB].EnteredNodeAt;
                            if (!corridor)
                            {
                                _slots[slotA].DueAt = _slots[slotA].EnteredNodeAt;
                            }
                        }

                        list.RemoveAt(j);
                        _slotByCohortId.Remove(_slots[slotB].Id);
                        FreeSlot(slotB);
                    }
                    else
                    {
                        j++;
                    }
                }
            }
        }

        private void EvaluateThreshold(int ordinal, ulong t)
        {
            int population = ComputeLivePopulation(ordinal);
            Fx wait = ComputeWait(ordinal, population);
            Fx threshold = _thresholdWait[ordinal];
            if (!_thresholdFlag[ordinal] && wait > threshold)
            {
                _thresholdFlag[ordinal] = true;
                _events.Publish(new QueueThresholdExceeded(_nodeId[ordinal], wait, _serversOpen[ordinal], _serverCount[ordinal]), EventRef.None);
            }
            else if (_thresholdFlag[ordinal] && wait < Fx.Sub(threshold, _hysteresis[ordinal]))
            {
                _thresholdFlag[ordinal] = false;
                _events.Publish(new QueueThresholdCleared(_nodeId[ordinal], wait, _serversOpen[ordinal], _serverCount[ordinal]), EventRef.None);
            }
        }

        // -------------------------------------------------------------- routing (§9.6, §9.12)

        private bool TryRoute(int fromOrdinal, Fx walkSpeed, out EdgeId bestEdge, out NodeId bestTarget)
        {
            if (_walkSpeedCount > 0 && _walkSpeedIndex.TryGetValue(walkSpeed.Raw, out int walkSpeedOrdinal))
            {
                int cacheIndex = (fromOrdinal * _walkSpeedCount) + walkSpeedOrdinal;
                if (_routeCacheValid[cacheIndex])
                {
                    bestEdge = _routeCacheEdge[cacheIndex];
                    bestTarget = _routeCacheTarget[cacheIndex];
                    return _routeCacheFound[cacheIndex];
                }

                bool computed = ComputeRoute(fromOrdinal, walkSpeed, out bestEdge, out bestTarget);
                _routeCacheValid[cacheIndex] = true;
                _routeCacheFound[cacheIndex] = computed;
                _routeCacheEdge[cacheIndex] = bestEdge;
                _routeCacheTarget[cacheIndex] = bestTarget;
                return computed;
            }

            // A walk speed that was never resolved from a loaded PaxProfile (§9.6
            // rule 2's static source) has no cell in the memo: compute directly,
            // never caching it, rather than growing the memo to fit it (rule 5).
            return ComputeRoute(fromOrdinal, walkSpeed, out bestEdge, out bestTarget);
        }

        private bool ComputeRoute(int fromOrdinal, Fx walkSpeed, out EdgeId bestEdge, out NodeId bestTarget)
        {
            NodeId from = _nodeId[fromOrdinal];
            IReadOnlyList<EdgeId> outs = _world.OutEdges(from);
            bool found = false;
            long bestCost = 0;
            uint bestGateValue = 0;
            uint bestEdgeValue = 0;
            EdgeId bestEdgeLocal = default;
            NodeId bestTargetLocal = default;

            for (int gi = 0; gi < _gateOrdinals.Count; gi++)
            {
                NodeId gate = _nodeId[_gateOrdinals[gi]];
                for (int ei = 0; ei < outs.Count; ei++)
                {
                    EdgeId e = outs[ei];
                    if (!_world.CanReachVia(e, gate))
                    {
                        continue;
                    }

                    long cost = ComputeRouteCost(e, gate, walkSpeed);
                    bool better = !found
                        || cost < bestCost
                        || (cost == bestCost && gate.Value < bestGateValue)
                        || (cost == bestCost && gate.Value == bestGateValue && e.Value < bestEdgeValue);
                    if (better)
                    {
                        found = true;
                        bestCost = cost;
                        bestGateValue = gate.Value;
                        bestEdgeValue = e.Value;
                        bestEdgeLocal = e;
                        bestTargetLocal = _world.EdgeTo(e);
                    }
                }
            }

            bestEdge = bestEdgeLocal;
            bestTarget = bestTargetLocal;
            return found;
        }

        private long ComputeRouteCost(EdgeId edge, NodeId gate, Fx walkSpeed)
        {
            IReadOnlyList<NodeId> path = _world.PathVia(edge, gate);
            Fx total = Fx.Zero;
            for (int i = 0; i < path.Count; i++)
            {
                NodeId node = path[i];
                total = Fx.Add(total, Fx.FromInt(TraversalTicks(node, walkSpeed)));
                if (_ordinalByNodeValue.TryGetValue(node.Value, out int ordinal) && _kind[ordinal] == NodeKind.Queue)
                {
                    total = Fx.Add(total, Fx.Mul(_snapshotWait[ordinal], Fx.FromInt((long)SimConstants.TICKS_PER_SIM_MINUTE)));
                }
            }

            return total.Raw;
        }

        private long TraversalTicks(NodeId node, Fx walkSpeed)
        {
            uint length = _world.LengthMetres(node);
            Fx denom = Fx.Mul(walkSpeed, Fx.FromInt(SimConstants.SIM_SECONDS_PER_TICK));
            long ceil = Fx.Ceil(Fx.Div(Fx.FromInt(length), denom));
            return Math.Max(1L, ceil);
        }

        private Fx WalkSpeedOf(ContentId paxProfile)
        {
            if (_content.TryGet(paxProfile, out PaxProfileDefinition def))
            {
                return def.WalkSpeedMps;
            }

            return Fx.One;
        }

        // -------------------------------------------------------------- helpers

        private int ComputeLivePopulation(int ordinal)
        {
            List<int> list = _cohortsOnNode[ordinal];
            int sum = 0;
            for (int k = 0; k < list.Count; k++)
            {
                sum += _slots[list[k]].Count;
            }

            return sum;
        }

        private bool IsAnyBlocked(int ordinal)
        {
            List<int> list = _cohortsOnNode[ordinal];
            for (int k = 0; k < list.Count; k++)
            {
                if (_slots[list[k]].Blocked)
                {
                    return true;
                }
            }

            return false;
        }

        private Fx ComputeWait(int ordinal, int population)
        {
            Fx capacityPerMinute = Fx.Mul(Fx.FromInt(_serversOpen[ordinal]), _serviceRate[ordinal]);
            return Fx.Div(Fx.FromInt(population), Fx.Max(capacityPerMinute, Epsilon));
        }

        private int RequireOrdinal(NodeId node, string paramName)
        {
            if (!_ordinalByNodeValue.TryGetValue(node.Value, out int ordinal))
            {
                throw new ArgumentException("unknown node " + node.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), paramName);
            }

            return ordinal;
        }

        private ulong NewCohortId() => _ids.Next(FlowSystemId).Value;

        private int AllocateSlot()
        {
            if (_freeSlots.Count > 0)
            {
                int i = _freeSlots[_freeSlots.Count - 1];
                _freeSlots.RemoveAt(_freeSlots.Count - 1);
                return i;
            }

            if (_slotHighWater == _slots.Length)
            {
                Array.Resize(ref _slots, _slots.Length * 2);
            }

            return _slotHighWater++;
        }

        private void FreeSlot(int index)
        {
            _slots[index].Alive = false;
            _freeSlots.Add(index);
        }
    }
}
