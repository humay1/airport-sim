using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Airside
{
    /// <summary>
    /// The airside module: one runway model, a single-lane taxiway graph, stands and the
    /// boarding hold. Spec: 12-interfaces-airside.md. All hot-path state is preallocated.
    /// </summary>
    internal sealed partial class AirsideSystem : IAirsideSystem
    {
        // 12 §12.2.
        private const ulong CruiseLeadTicks = 1200UL;
        private const int StandWaitCapacity = 1024;
        private const int PendingCapacity = 2048;
        private const int InitialSlots = 4096;
        private const ulong Unscheduled = ulong.MaxValue;

        private readonly IScheduleSystem _schedule;
        private readonly IFlowSystem? _flow;
        private readonly bool _turnaround;
        private readonly ulong _delayTicks;
        private readonly ulong _holdTicks;
        private readonly AirsideLayout _layout;
        private readonly SystemId _id = new SystemId(3);

        // Layout, by dense index in ascending id order.
        private readonly ushort[] _nodeId;
        private readonly int[] _nodeIndexById;
        private readonly ushort[] _edgeId;
        private readonly int[] _edgeFrom;
        private readonly int[] _edgeTo;
        private readonly ulong[] _edgeTicks;
        private readonly bool[] _edgeBidir;
        private readonly ushort[] _standId;
        private readonly int[] _standNode;
        private readonly int[] _standMaxOrd;
        private readonly NodeId[] _standSink;
        private readonly int[] _standIndexById;
        private readonly ushort[] _rwyId;
        private readonly int[] _rwyNode;
        private readonly ulong[] _rwyOccTicks;
        private readonly ulong[] _rwySep;
        private readonly Dictionary<ContentId, int> _aircraftOrd = new Dictionary<ContentId, int>();

        // Runway state.
        private readonly ulong[] _rwyNextSlot;
        private readonly Slot?[] _rwyOccupant;
        private readonly Slot?[] _rwyQHead;
        private readonly Slot?[] _rwyQTail;
        private readonly int[] _rwyQLen;

        // Edge state.
        private readonly Slot?[] _edgeOcc;
        private readonly Slot?[] _edgeQHead;
        private readonly Slot?[] _edgeQTail;
        private readonly int[] _edgeQLen;
        private readonly ulong[] _edgeLeftTick;
        private readonly ulong[] _edgeLeftFlight;
        private readonly ulong[] _edgeStamp;
        private readonly int[] _heldEdges;
        private int _heldEdgeCount;
        private readonly bool[] _edgeIsHeld;

        // Stand state.
        private readonly bool[] _standHas;
        private readonly ulong[] _standOccupant;
        private readonly ulong[] _standFreedTick;
        private readonly EventRef[] _standVacatedBy;
        private readonly WaitEntry[] _wait = new WaitEntry[StandWaitCapacity];
        private int _waitCount;

        // Tracked aircraft, ascending FlightId.
        private Slot[] _order = new Slot[InitialSlots];
        private Slot[] _scr = new Slot[InitialSlots];
        private Slot[] _askers = new Slot[InitialSlots];
        private Slot[] _standReqs = new Slot[InitialSlots];
        private int _n;
        private int _askerCount;
        private int _standReqCount;
        private readonly Stack<Slot> _pool = new Stack<Slot>(InitialSlots);

        // The pending list, ascending FlightId (12 §12.11).
        private readonly PendingEntry[] _pending = new PendingEntry[PendingCapacity];
        private int _pendingCount;
        private readonly PendingEntry[] _depDue = new PendingEntry[PendingCapacity];

        private ulong _now;

        internal AirsideSystem(
            in SystemServices services,
            in AirsideLayout layout,
            in AirsideRules rules,
            IScheduleSystem schedule,
            IFlowSystem? flow,
            bool turnaroundRegistered)
        {
            if (schedule is null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            _schedule = schedule;
            _flow = flow;
            _turnaround = turnaroundRegistered;
            _delayTicks = (ulong)rules.DoorsOpenDelayMinutes * SimConstants.TICKS_PER_SIM_MINUTE;
            _holdTicks = (ulong)rules.BoardingHoldMaxMinutes * SimConstants.TICKS_PER_SIM_MINUTE;
            _layout = layout;

            int nodes = layout.Nodes.Count;
            int edges = layout.Edges.Count;
            int stands = layout.Stands.Count;
            int runways = layout.Runways.Count;

            _nodeId = new ushort[nodes];
            int maxNode = 0;
            for (int i = 0; i < nodes; i++)
            {
                _nodeId[i] = layout.Nodes[i].Id.Value;
                maxNode = Math.Max(maxNode, _nodeId[i]);
            }

            _nodeIndexById = Filled(maxNode + 1);
            for (int i = 0; i < nodes; i++)
            {
                _nodeIndexById[_nodeId[i]] = i;
            }

            _edgeId = new ushort[edges];
            _edgeFrom = new int[edges];
            _edgeTo = new int[edges];
            _edgeTicks = new ulong[edges];
            _edgeBidir = new bool[edges];
            for (int i = 0; i < edges; i++)
            {
                TaxiEdgeDef e = layout.Edges[i];
                _edgeId[i] = e.Id.Value;
                _edgeFrom[i] = _nodeIndexById[e.From.Value];
                _edgeTo[i] = _nodeIndexById[e.To.Value];
                _edgeTicks[i] = e.TraversalTicks;
                _edgeBidir[i] = e.Bidirectional;
            }

            _standId = new ushort[stands];
            _standNode = new int[stands];
            _standMaxOrd = new int[stands];
            _standSink = new NodeId[stands];
            int maxStand = 0;
            for (int i = 0; i < stands; i++)
            {
                StandDef s = layout.Stands[i];
                if (!services.Content.TryGet(s.MaxAircraftSizeCategory, out SizeCategoryDefinition size))
                {
                    throw new FormatException(
                        "sim.airside: stand " + s.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " names size category '" + s.MaxAircraftSizeCategory.Value + "' which is not in the content");
                }

                _standId[i] = s.Id.Value;
                _standNode[i] = _nodeIndexById[s.Node.Value];
                _standMaxOrd[i] = size.Ordinal;
                _standSink[i] = s.DepartureSinkNode;
                maxStand = Math.Max(maxStand, _standId[i]);
            }

            _standIndexById = Filled(maxStand + 1);
            for (int i = 0; i < stands; i++)
            {
                _standIndexById[_standId[i]] = i;
            }

            _rwyId = new ushort[runways];
            _rwyNode = new int[runways];
            _rwyOccTicks = new ulong[runways];
            _rwySep = new ulong[runways];
            for (int i = 0; i < runways; i++)
            {
                RunwayDef r = layout.Runways[i];
                _rwyId[i] = r.Id.Value;
                _rwyNode[i] = _nodeIndexById[r.ThresholdNode.Value];
                _rwyOccTicks[i] = r.OccupancyTicks;
                ulong cap = (ulong)r.DeclaredCapacityPerHour;
                _rwySep[i] = (SimConstants.TICKS_PER_SIM_HOUR + cap - 1UL) / cap;
            }

            IReadOnlyList<ContentId> aircraft = services.Content.AllOf(ContentKind.Aircraft);
            for (int i = 0; i < aircraft.Count; i++)
            {
                if (services.Content.TryGet(aircraft[i], out AircraftDefinition def)
                    && services.Content.TryGet(def.SizeCategory, out SizeCategoryDefinition sc))
                {
                    _aircraftOrd[aircraft[i]] = sc.Ordinal;
                }
            }

            _rwyNextSlot = new ulong[runways];
            _rwyOccupant = new Slot?[runways];
            _rwyQHead = new Slot?[runways];
            _rwyQTail = new Slot?[runways];
            _rwyQLen = new int[runways];

            _edgeOcc = new Slot?[edges];
            _edgeQHead = new Slot?[edges];
            _edgeQTail = new Slot?[edges];
            _edgeQLen = new int[edges];
            _edgeLeftTick = new ulong[edges];
            _edgeLeftFlight = new ulong[edges];
            _edgeStamp = new ulong[edges];
            _heldEdges = new int[edges];
            _edgeIsHeld = new bool[edges];
            for (int i = 0; i < edges; i++)
            {
                _edgeLeftTick[i] = ulong.MaxValue;
                _edgeStamp[i] = ulong.MaxValue;
            }

            _standHas = new bool[stands];
            _standOccupant = new ulong[stands];
            _standFreedTick = new ulong[stands];
            _standVacatedBy = new EventRef[stands];
            for (int i = 0; i < stands; i++)
            {
                _standFreedTick[i] = ulong.MaxValue;
                _standVacatedBy[i] = EventRef.None;
            }

            for (int i = 0; i < InitialSlots; i++)
            {
                _pool.Push(new Slot());
            }

            _cand = new int[edges];
            BuildRoutes();
            ReadDayZero();

            services.Events.Subscribe<FlightPlanPublished>(_id, OnPlanPublished);
            if (turnaroundRegistered)
            {
                services.Events.Subscribe<FlightMilestoneReached>(_id, OnMilestone);
            }

            services.Commands.Register(_id, new ReassignHandler(this));
        }

        public SystemId Id => _id;

        public string Name => "sim.airside";

        public AirsideLayout Layout() => _layout;

        public bool TryGetTrack(FlightId flight, out AircraftTrack track)
        {
            Slot? s = Find(flight.Value);
            if (s is null)
            {
                track = default;
                return false;
            }

            track = MakeTrack(s);
            return true;
        }

        public bool TryGetStand(StandId id, out StandState stand)
        {
            int i = StandIndex(id.Value);
            if (i < 0)
            {
                stand = default;
                return false;
            }

            stand = new StandState(id, _standHas[i] ? new FlightId(_standOccupant[i]) : (FlightId?)null, _standVacatedBy[i]);
            return true;
        }

        public IReadOnlyList<StandId> FreeStands()
        {
            var list = new List<StandId>();
            for (int i = 0; i < _standId.Length; i++)
            {
                if (!_standHas[i])
                {
                    list.Add(new StandId(_standId[i]));
                }
            }

            return list;
        }

        public int RunwayQueueLength(RunwayId runway)
        {
            for (int i = 0; i < _rwyId.Length; i++)
            {
                if (_rwyId[i] == runway.Value)
                {
                    return _rwyQLen[i];
                }
            }

            return 0;
        }

        public IReadOnlyList<FlightId> TrackedFlights()
        {
            var list = new List<FlightId>(_n);
            for (int i = 0; i < _n; i++)
            {
                list.Add(new FlightId(_order[i].Flight));
            }

            return list;
        }

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            for (int r = 0; r < _rwyId.Length; r++)
            {
                h.Feed(_rwyNextSlot[r]);
                FeedSlotRef(ref h, _rwyOccupant[r]);
                FeedQueue(ref h, _rwyQHead[r], _rwyQLen[r]);
            }

            for (int e = 0; e < _edgeId.Length; e++)
            {
                FeedSlotRef(ref h, _edgeOcc[e]);
                FeedQueue(ref h, _edgeQHead[e], _edgeQLen[e]);
            }

            for (int s = 0; s < _standId.Length; s++)
            {
                h.Feed(_standHas[s]);
                h.Feed(_standHas[s] ? _standOccupant[s] : 0UL);
                FeedRef(ref h, _standVacatedBy[s]);
            }

            h.Feed((ulong)_waitCount);
            for (int i = 0; i < _waitCount; i++)
            {
                h.Feed(_wait[i].Flight);
            }

            h.Feed((ulong)_n);
            for (int i = 0; i < _n; i++)
            {
                AircraftTrack t = MakeTrack(_order[i]);
                h.Feed(t.Flight.Value);
                h.Feed((ulong)t.Kind);
                h.Feed((ulong)t.Phase);
                h.Feed(t.AtNode.HasValue);
                h.Feed(t.AtNode.HasValue ? (ulong)t.AtNode.Value.Value : 0UL);
                h.Feed(t.OnEdge.HasValue);
                h.Feed(t.OnEdge.HasValue ? (ulong)t.OnEdge.Value.Value : 0UL);
                h.Feed(t.EdgeProgress);
                h.Feed(t.Stand.HasValue);
                h.Feed(t.Stand.HasValue ? (ulong)t.Stand.Value.Value : 0UL);
                h.Feed(t.Runway.HasValue);
                h.Feed(t.Runway.HasValue ? (ulong)t.Runway.Value.Value : 0UL);
                h.Feed(t.PhaseEnteredAt);
                h.Feed(t.DueAt);
                h.Feed(t.PassengerHoldSince);
                FeedRef(ref h, t.RecordedCause);
                FeedRef(ref h, t.OpenHold);
            }

            h.Feed((ulong)_pendingCount);
            for (int i = 0; i < _pendingCount; i++)
            {
                h.Feed(_pending[i].Flight);
            }

            return h.Result;
        }

        private static void FeedRef(ref StateHasher h, in EventRef r)
        {
            h.Feed(r.HasValue);
            h.Feed(r.HasValue ? r.Id.Tick : 0UL);
            h.Feed(r.HasValue ? (ulong)r.Id.Sequence : 0UL);
        }

        private static void FeedSlotRef(ref StateHasher h, Slot? s)
        {
            h.Feed(s != null);
            h.Feed(s != null ? s.Flight : 0UL);
        }

        private static void FeedQueue(ref StateHasher h, Slot? head, int len)
        {
            h.Feed((ulong)len);
            for (Slot? s = head; s != null; s = s.QNext)
            {
                h.Feed(s.Flight);
            }
        }

        private static int[] Filled(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = -1;
            }

            return a;
        }

        private int StandIndex(ushort id)
        {
            return id < _standIndexById.Length ? _standIndexById[id] : -1;
        }

        private AircraftTrack MakeTrack(Slot s)
        {
            Fx progress = Fx.Zero;
            if (s.OnEdge >= 0)
            {
                ulong ticks = _edgeTicks[s.OnEdge];
                ulong elapsed = _now >= s.PhaseEnteredAt ? _now - s.PhaseEnteredAt : 0UL;
                if (elapsed > ticks)
                {
                    elapsed = ticks;
                }

                progress = Fx.FromRatio((long)elapsed, (long)ticks);
            }

            return new AircraftTrack(
                new FlightId(s.Flight),
                s.Kind,
                s.Phase,
                s.AtNode >= 0 ? new TaxiNodeId(_nodeId[s.AtNode]) : (TaxiNodeId?)null,
                s.OnEdge >= 0 ? new TaxiEdgeId(_edgeId[s.OnEdge]) : (TaxiEdgeId?)null,
                progress,
                s.Stand >= 0 ? new StandId(_standId[s.Stand]) : (StandId?)null,
                s.Rwy >= 0 ? new RunwayId(_rwyId[s.Rwy]) : (RunwayId?)null,
                s.PhaseEnteredAt,
                s.DueAt,
                s.HoldSince,
                s.Recorded,
                s.OpenHold);
        }

        // ------------------------------------------------------------ tracked list

        private int LowerBound(ulong flight)
        {
            int lo = 0;
            int hi = _n;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_order[mid].Flight < flight)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        private Slot? Find(ulong flight)
        {
            int i = LowerBound(flight);
            return i < _n && _order[i].Flight == flight ? _order[i] : null;
        }

        private Slot NewTrack(ulong flight, MovementKind kind)
        {
            Slot s = _pool.Count > 0 ? _pool.Pop() : new Slot();
            s.Reset();
            s.Flight = flight;
            s.Kind = kind;
            s.HoldSince = Unscheduled;
            s.DueAt = Unscheduled;
            if (_n == _order.Length)
            {
                int cap = _n * 2;
                Array.Resize(ref _order, cap);
                Array.Resize(ref _scr, cap);
                Array.Resize(ref _askers, cap);
                Array.Resize(ref _standReqs, cap);
            }

            int at = LowerBound(flight);
            Array.Copy(_order, at, _order, at + 1, _n - at);
            _order[at] = s;
            _n++;
            return s;
        }

        private void RemoveTrack(Slot s)
        {
            int at = LowerBound(s.Flight);
            Array.Copy(_order, at + 1, _order, at, _n - at - 1);
            _n--;
            _order[_n] = null!;
            s.Reset();
            _pool.Push(s);
        }

        private int OrdinalOf(ContentId aircraft)
        {
            return _aircraftOrd.TryGetValue(aircraft, out int o) ? o : int.MaxValue;
        }

        // ------------------------------------------------------------ pending list

        private void ReadDayZero()
        {
            for (int k = 0; k < 2; k++)
            {
                MovementKind kind = k == 0 ? MovementKind.Arrival : MovementKind.Departure;
                IReadOnlyList<FlightId> flights = _schedule.MovementsBetween(0UL, SimConstants.TICKS_PER_SIM_DAY, kind);
                for (int i = 0; i < flights.Count; i++)
                {
                    if (!_schedule.TryGetFlight(flights[i], out FlightRecord r))
                    {
                        continue;
                    }

                    if (kind == MovementKind.Departure && _schedule.TryGetRotation(r.Id, out FlightId _))
                    {
                        continue;
                    }

                    ulong start = kind == MovementKind.Arrival ? ArrivalStart(r.ScheduledTick) : DueTick(r.ScheduledTick, TurnTicks(r.MinTurnaround));
                    if (_pendingCount >= PendingCapacity)
                    {
                        throw new ArgumentException(
                            "sim.airside: the pending list capacity of 2048 is exceeded by the day-0 read at flight "
                            + r.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "schedule");
                    }

                    InsertPending(new PendingEntry(r.Id.Value, start, kind == MovementKind.Departure));
                }
            }
        }

        private static ulong ArrivalStart(ulong sta)
        {
            return sta > CruiseLeadTicks ? sta - CruiseLeadTicks : 0UL;
        }

        private static ulong DueTick(ulong std, ulong minTurnTicks)
        {
            return std > minTurnTicks ? std - minTurnTicks : 0UL;
        }

        private static ulong TurnTicks(Fx minutes)
        {
            long t = Fx.Floor(minutes * Fx.FromInt((long)SimConstants.TICKS_PER_SIM_MINUTE));
            return t > 0 ? (ulong)t : 0UL;
        }

        private void InsertPending(PendingEntry e)
        {
            int lo = 0;
            int hi = _pendingCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_pending[mid].Flight < e.Flight)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            Array.Copy(_pending, lo, _pending, lo + 1, _pendingCount - lo);
            _pending[lo] = e;
            _pendingCount++;
        }

        private void OnPlanPublished(in EventEnvelope env, in FlightPlanPublished evt, in TickContext ctx)
        {
            ulong sched = evt.Kind == MovementKind.Arrival ? evt.SchedArr : evt.SchedDep;
            if (sched < SimConstants.TICKS_PER_SIM_DAY || sched == Unscheduled)
            {
                return;
            }

            if (evt.Kind == MovementKind.Departure && evt.HasRotation)
            {
                return;
            }

            ulong start;
            if (evt.Kind == MovementKind.Arrival)
            {
                start = ArrivalStart(sched);
            }
            else
            {
                ulong due = DueTick(sched, TurnTicks(evt.MinTurnaround));
                start = Math.Max(due, env.Tick + 1UL);
            }

            if (_pendingCount >= PendingCapacity)
            {
                throw new SimInvariantException(
                    "sim.airside: the pending list is full (capacity 2048), cannot append flight "
                    + evt.Flight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ctx.Tick);
            }

            InsertPending(new PendingEntry(evt.Flight.Value, start, evt.Kind == MovementKind.Departure));
        }

        private void OnMilestone(in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx)
        {
            if (evt.Milestone == FlightMilestone.DeboardComplete)
            {
                Slot? s = Find(evt.Flight.Value);
                if (s != null && s.Kind == MovementKind.Arrival && s.Phase == AircraftLegPhase.OnStand && s.HasRotation && !s.Recorded.HasValue)
                {
                    s.Recorded = new EventRef(env.Id, true);
                }
            }
            else if (evt.Milestone == FlightMilestone.BoardingComplete)
            {
                Slot? s = Find(evt.Flight.Value);
                if (s != null && s.Kind == MovementKind.Departure && s.Phase == AircraftLegPhase.OnStand && !s.Recorded.HasValue)
                {
                    s.Recorded = new EventRef(env.Id, true);
                }
            }
        }

        // ------------------------------------------------------------ ReassignStand

        private sealed class ReassignHandler : ICommandHandler
        {
            private readonly AirsideSystem _owner;

            public ReassignHandler(AirsideSystem owner)
            {
                _owner = owner;
            }

            public CommandKind Kind => CommandKind.ReassignStand;

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                if (payload.Length != 10)
                {
                    return CommandRejection.MalformedPayload;
                }

                ushort stand = (ushort)(payload[8] | (payload[9] << 8));
                return _owner.StandIndex(stand) < 0 ? CommandRejection.MalformedPayload : CommandRejection.None;
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                ulong flight = 0;
                for (int i = 7; i >= 0; i--)
                {
                    flight = (flight << 8) | cmd.Payload[i];
                }

                ushort stand = (ushort)(cmd.Payload[8] | (cmd.Payload[9] << 8));
                _owner.Reassign(flight, stand, ctx);
            }
        }

        private void Reassign(ulong flight, ushort standId, in TickContext ctx)
        {
            int target = StandIndex(standId);
            Slot? s = Find(flight);
            long reason = 0;
            if (s is null || s.Phase != AircraftLegPhase.OnStand || s.Stand < 0 || !_standHas[s.Stand] || _standOccupant[s.Stand] != flight)
            {
                reason = 1;
            }
            else if (_standHas[target])
            {
                reason = 2;
            }
            else if (s.SizeOrd > _standMaxOrd[target])
            {
                reason = 3;
            }

            if (reason != 0)
            {
                ctx.Log.Write(ctx.Tick, LogLevel.Info, _id, LogKey.AirsideReassignStandNoOp, new LogArgs(unchecked((long)flight), standId, reason));
                return;
            }

            Slot slot = s!;
            int old = slot.Stand;
            _standHas[old] = false;
            _standVacatedBy[old] = EventRef.None;
            _standHas[target] = true;
            _standOccupant[target] = flight;
            _standVacatedBy[target] = EventRef.None;
            slot.Stand = target;
            slot.AtNode = _standNode[target];
        }

        // ------------------------------------------------------------ small types

        internal sealed class Slot
        {
            public ulong Flight;
            public MovementKind Kind;
            public AircraftLegPhase Phase;
            public int AtNode = -1;
            public int OnEdge = -1;
            public int Stand = -1;
            public int Rwy = -1;
            public ulong PhaseEnteredAt;
            public ulong DueAt;
            public ulong HoldSince;
            public EventRef Recorded;

            public ulong Sched;
            public ulong MinTurnTicks;
            public int SizeOrd;
            public bool HasRotation;
            public ulong Rotation;
            public int AskEdge = -1;
            public Slot? QNext;
            public EventRef OpenHold;
            public EventRef Placed;
            public EventRef OffRunwayEvent;
            public bool ReqTakeoff;

            public void Reset()
            {
                Flight = 0;
                Kind = MovementKind.Arrival;
                Phase = AircraftLegPhase.AwaitingApproach;
                AtNode = -1;
                OnEdge = -1;
                Stand = -1;
                Rwy = -1;
                PhaseEnteredAt = 0;
                DueAt = Unscheduled;
                HoldSince = Unscheduled;
                Recorded = EventRef.None;
                Sched = 0;
                MinTurnTicks = 0;
                SizeOrd = 0;
                HasRotation = false;
                Rotation = 0;
                AskEdge = -1;
                QNext = null;
                OpenHold = EventRef.None;
                Placed = EventRef.None;
                OffRunwayEvent = EventRef.None;
                ReqTakeoff = false;
            }
        }

        private readonly struct PendingEntry
        {
            public readonly ulong Flight;
            public readonly ulong Start;
            public readonly bool IsDeparture;

            public PendingEntry(ulong flight, ulong start, bool isDeparture)
            {
                Flight = flight;
                Start = start;
                IsDeparture = isDeparture;
            }
        }

        private readonly struct WaitEntry
        {
            public readonly ulong Flight;
            public readonly bool IsDeparture;
            public readonly int SizeOrd;

            public WaitEntry(ulong flight, bool isDeparture, int sizeOrd)
            {
                Flight = flight;
                IsDeparture = isDeparture;
                SizeOrd = sizeOrd;
            }
        }
    }
}
