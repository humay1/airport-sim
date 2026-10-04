using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    // sim.delay (14-interfaces-delay.md). A pure function of the event stream: it
    // reads no content, holds no other system and draws no random numbers.
    internal sealed partial class DelaySystem : IDelaySystem
    {
        private const int InitialFlights = 1024;
        private const int InitialNodes = 4096;
        private const int InitialIntervals = 512;
        private const int InitialIdMap = 16384;

        private const ulong Unscheduled = ulong.MaxValue;

        // 14 §14.2.
        private const ulong RetentionDays = 2UL;

        private const int FamilyRunway = 0;
        private const int FamilyTaxiway = 1;
        private const int FamilyStand = 2;
        private const int FamilyTurnaround = 3;
        private const int FamilyPassengerHold = 4;

        private readonly SystemId _id = new SystemId(7);

        // Flight records in pooled slots, plus the slots in ascending FlightId.
        private FlightRec[] _f = new FlightRec[InitialFlights];
        private int _freeFlight;
        private ulong[] _orderIds = new ulong[InitialFlights];
        private int[] _orderSlots = new int[InitialFlights];
        private int _flightCount;

        private NodeRec[] _n = new NodeRec[InitialNodes];
        private int _freeNode;
        private int _firstNode = -1;
        private int _lastNode = -1;
        private readonly IdMap _nodeById = new IdMap(InitialIdMap);
        private ulong _nextId = 1UL;

        private IntervalRec[] _i = new IntervalRec[InitialIntervals];
        private int _freeInterval;
        private int _firstInterval = -1;
        private int _lastInterval = -1;

        // Scratch for first-blocker-wins ownership: merged, disjoint, ascending segments.
        private ulong[] _segStart = new ulong[16];
        private ulong[] _segEnd = new ulong[16];
        private int _segCount;

        public DelaySystem(in SystemServices services)
        {
            LinkFreeFlights(0);
            LinkFreeNodes(0);
            LinkFreeIntervals(0);

            IEventBus bus = services.Events;
            bus.Subscribe<FlightPlanPublished>(_id, OnPlan);
            bus.Subscribe<FlightMilestoneReached>(_id, OnMilestone);
            bus.Subscribe<AircraftHeldForRunway>(_id, OnRunwayHeld);
            bus.Subscribe<AircraftHeldForRunwayReleased>(_id, OnRunwayReleased);
            bus.Subscribe<AircraftHeldOnTaxiway>(_id, OnTaxiwayHeld);
            bus.Subscribe<AircraftHeldOnTaxiwayReleased>(_id, OnTaxiwayReleased);
            bus.Subscribe<StandUnavailable>(_id, OnStandUnavailable);
            bus.Subscribe<StandAssigned>(_id, OnStandAssigned);
            bus.Subscribe<TurnaroundJobBlocked>(_id, OnJobBlocked);
            bus.Subscribe<TurnaroundJobUnblocked>(_id, OnJobUnblocked);
            bus.Subscribe<DepartureHeldForPassengers>(_id, OnHoldOpened);
            bus.Subscribe<DepartureHeldForPassengersReleased>(_id, OnHoldReleased);
            bus.Subscribe<PassengersMissedFlight>(_id, OnMissed);
        }

        public SystemId Id => _id;

        public string Name => "sim.delay";

        // ------------------------------------------------------------ pools

        private void LinkFreeFlights(int from)
        {
            for (int s = from; s < _f.Length; s++)
            {
                _f[s].NextFree = s + 1 < _f.Length ? s + 1 : -1;
            }

            _freeFlight = from;
        }

        private void LinkFreeNodes(int from)
        {
            for (int s = from; s < _n.Length; s++)
            {
                _n[s].Next = s + 1 < _n.Length ? s + 1 : -1;
            }

            _freeNode = from;
        }

        private void LinkFreeIntervals(int from)
        {
            for (int s = from; s < _i.Length; s++)
            {
                _i[s].Next = s + 1 < _i.Length ? s + 1 : -1;
            }

            _freeInterval = from;
        }

        private int AllocFlight()
        {
            if (_freeFlight < 0)
            {
                int old = _f.Length;
                Array.Resize(ref _f, old * 2);
                LinkFreeFlights(old);
            }

            int slot = _freeFlight;
            _freeFlight = _f[slot].NextFree;
            return slot;
        }

        private int AllocNode()
        {
            if (_freeNode < 0)
            {
                int old = _n.Length;
                Array.Resize(ref _n, old * 2);
                LinkFreeNodes(old);
            }

            int slot = _freeNode;
            _freeNode = _n[slot].Next;
            return slot;
        }

        private int AllocInterval()
        {
            if (_freeInterval < 0)
            {
                int old = _i.Length;
                Array.Resize(ref _i, old * 2);
                LinkFreeIntervals(old);
            }

            int slot = _freeInterval;
            _freeInterval = _i[slot].Next;
            return slot;
        }

        // ------------------------------------------------------------ flight index

        // Position of the flight in the ascending order, or the complement of its insertion point.
        private int Search(ulong flight)
        {
            int lo = 0;
            int hi = _flightCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong v = _orderIds[mid];
                if (v == flight)
                {
                    return mid;
                }

                if (v < flight)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return ~lo;
        }

        private int Find(ulong flight)
        {
            int pos = Search(flight);
            return pos >= 0 ? _orderSlots[pos] : -1;
        }

        private void InsertFlight(int pos, ulong flight, int slot)
        {
            if (_flightCount == _orderIds.Length)
            {
                Array.Resize(ref _orderIds, _flightCount * 2);
                Array.Resize(ref _orderSlots, _flightCount * 2);
            }

            if (pos < _flightCount)
            {
                Array.Copy(_orderIds, pos, _orderIds, pos + 1, _flightCount - pos);
                Array.Copy(_orderSlots, pos, _orderSlots, pos + 1, _flightCount - pos);
            }

            _orderIds[pos] = flight;
            _orderSlots[pos] = slot;
            _flightCount++;
        }

        // ------------------------------------------------------------ nodes

        // Appends a node: its id is the next counter value, so it is last in id order.
        private int NewNode(int flightSlot, DelayNodeKind kind, DelayCategory category, bool rootCause, ulong linked, bool hasSource, EventId source, DelaySource src, ulong a, ulong b, ulong tick)
        {
            int slot = AllocNode();
            ulong id = _nextId++;
            ref NodeRec n = ref _n[slot];
            ref FlightRec f = ref _f[flightSlot];
            n.Id = id;
            n.Kind = kind;
            n.Subject = f.Flight;
            n.Parent = kind == DelayNodeKind.FlightTotal ? 0UL : _n[f.Root].Id;
            n.Category = category;
            n.Ticks = 0UL;
            n.RootCause = rootCause;
            n.Linked = linked;
            n.HasSource = hasSource;
            n.Source = source;
            n.Src = src;
            n.A = a;
            n.B = b;
            n.CreatedAt = tick;
            n.Prev = _lastNode;
            n.Next = -1;
            if (_lastNode >= 0)
            {
                _n[_lastNode].Next = slot;
            }
            else
            {
                _firstNode = slot;
            }

            _lastNode = slot;
            _nodeById.Add(id, slot);

            n.LeafPrev = -1;
            n.LeafNext = -1;
            if (kind == DelayNodeKind.Allocation)
            {
                n.LeafPrev = f.LastLeaf;
                if (f.LastLeaf >= 0)
                {
                    _n[f.LastLeaf].LeafNext = slot;
                }
                else
                {
                    f.FirstLeaf = slot;
                }

                f.LastLeaf = slot;
            }

            return slot;
        }

        // Unlinks and frees a node. Its id is never reused.
        private void FreeNode(int slot, int flightSlot)
        {
            ref NodeRec n = ref _n[slot];
            if (n.Kind == DelayNodeKind.Allocation)
            {
                ref FlightRec f = ref _f[flightSlot];
                if (n.LeafPrev >= 0)
                {
                    _n[n.LeafPrev].LeafNext = n.LeafNext;
                }
                else
                {
                    f.FirstLeaf = n.LeafNext;
                }

                if (n.LeafNext >= 0)
                {
                    _n[n.LeafNext].LeafPrev = n.LeafPrev;
                }
                else
                {
                    f.LastLeaf = n.LeafPrev;
                }
            }

            if (n.Prev >= 0)
            {
                _n[n.Prev].Next = n.Next;
            }
            else
            {
                _firstNode = n.Next;
            }

            if (n.Next >= 0)
            {
                _n[n.Next].Prev = n.Prev;
            }
            else
            {
                _lastNode = n.Prev;
            }

            _nodeById.Remove(n.Id);
            n.Id = 0UL;
            n.Next = _freeNode;
            _freeNode = slot;
        }

        private DelayNode ToNode(int slot)
        {
            ref NodeRec n = ref _n[slot];
            return new DelayNode(
                new DelayEventId(n.Id),
                n.Kind,
                new FlightId(n.Subject),
                new DelayEventId(n.Parent),
                n.Kind == DelayNodeKind.FlightTotal ? (DelayCategory?)null : n.Category,
                n.Ticks,
                Minutes(n.Ticks),
                n.RootCause,
                new FlightId(n.Linked),
                n.HasSource ? new EventRef(n.Source, true) : EventRef.None,
                new DelayExplanation(n.Src, n.A, n.B),
                n.CreatedAt);
        }

        private static Fx Minutes(ulong ticks)
        {
            return Fx.FromRatio((long)ticks, (long)SimConstants.TICKS_PER_SIM_MINUTE);
        }

        // ------------------------------------------------------------ intervals

        private static ulong KeyOf(in IntervalRec r)
        {
            switch (r.Family)
            {
                case FamilyRunway:
                case FamilyTaxiway:
                    return r.A;
                case FamilyTurnaround:
                    return (ulong)r.Job;
                default:
                    return 0UL;
            }
        }

        private void FreeInterval(int slot, int flightSlot)
        {
            ref IntervalRec r = ref _i[slot];
            ref FlightRec f = ref _f[flightSlot];
            if (r.FlightPrev >= 0)
            {
                _i[r.FlightPrev].FlightNext = r.FlightNext;
            }
            else
            {
                f.FirstInterval = r.FlightNext;
            }

            if (r.FlightNext >= 0)
            {
                _i[r.FlightNext].FlightPrev = r.FlightPrev;
            }
            else
            {
                f.LastInterval = r.FlightPrev;
            }

            if (r.Prev >= 0)
            {
                _i[r.Prev].Next = r.Next;
            }
            else
            {
                _firstInterval = r.Next;
            }

            if (r.Next >= 0)
            {
                _i[r.Next].Prev = r.Prev;
            }
            else
            {
                _lastInterval = r.Prev;
            }

            r.Next = _freeInterval;
            _freeInterval = slot;
        }

        // ------------------------------------------------------------ tick: pruning

        public void Tick(in TickContext ctx)
        {
            ulong tick = ctx.Tick;
            if (tick % SimConstants.TICKS_PER_SIM_DAY != 0UL)
            {
                return;
            }

            ulong day = tick / SimConstants.TICKS_PER_SIM_DAY;
            if (day < RetentionDays - 1UL)
            {
                return;
            }

            ulong threshold = (day - (RetentionDays - 1UL)) * SimConstants.TICKS_PER_SIM_DAY;
            bool any = false;
            for (int p = 0; p < _flightCount; p++)
            {
                int slot = _orderSlots[p];
                ref FlightRec r = ref _f[slot];
                if (r.Doomed || !r.Finalised || r.FinalisedAt >= threshold)
                {
                    continue;
                }

                int partner = r.HasRotation ? Find(r.Rotation) : -1;
                if (partner >= 0 && (!_f[partner].Finalised || _f[partner].FinalisedAt >= threshold))
                {
                    continue;
                }

                r.Doomed = true;
                if (partner >= 0)
                {
                    _f[partner].Doomed = true;
                }

                any = true;
            }

            if (!any)
            {
                return;
            }

            int kept = 0;
            for (int p = 0; p < _flightCount; p++)
            {
                int slot = _orderSlots[p];
                if (_f[slot].Doomed)
                {
                    RemoveFlight(slot);
                }
                else
                {
                    _orderIds[kept] = _orderIds[p];
                    _orderSlots[kept] = slot;
                    kept++;
                }
            }

            _flightCount = kept;
        }

        private void RemoveFlight(int slot)
        {
            ref FlightRec f = ref _f[slot];
            while (f.FirstInterval >= 0)
            {
                FreeInterval(f.FirstInterval, slot);
            }

            while (f.FirstLeaf >= 0)
            {
                FreeNode(f.FirstLeaf, slot);
            }

            FreeNode(f.Root, slot);
            f.Doomed = false;
            f.NextFree = _freeFlight;
            _freeFlight = slot;
        }

        // ------------------------------------------------------------ hash

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            h.Feed(_nextId);

            h.Feed((ulong)_flightCount);
            for (int p = 0; p < _flightCount; p++)
            {
                ref FlightRec f = ref _f[_orderSlots[p]];
                h.Feed(f.Flight);
                h.Feed((ulong)(int)f.Kind);
                h.Feed(f.HasRotation);
                h.Feed(f.Rotation);
                h.Feed(_n[f.Root].Id);
                h.Feed(f.Total);
                h.Feed((ulong)f.Checkpoints);
                h.Feed(f.Last);
                h.Feed(f.Finalised);
                h.Feed(f.FinalisedAt);
                h.Feed((long)f.Missed);
                h.Feed(f.HasMissedAt);
                if (f.HasMissedAt)
                {
                    h.Feed((ulong)f.MissedAt);
                }
            }

            int intervals = 0;
            for (int s = _firstInterval; s >= 0; s = _i[s].Next)
            {
                intervals++;
            }

            h.Feed((ulong)intervals);
            for (int s = _firstInterval; s >= 0; s = _i[s].Next)
            {
                ref IntervalRec r = ref _i[s];
                h.Feed(r.Opener.Tick);
                h.Feed((ulong)r.Opener.Sequence);
                h.Feed(r.Flight);
                h.Feed((ulong)r.Family);
                h.Feed((ulong)r.Job);
                h.Feed((ulong)(int)r.Category);
                h.Feed(r.Start);
                h.Feed(r.End);
                h.Feed(r.A);
                h.Feed(r.B);
            }

            int nodes = 0;
            for (int s = _firstNode; s >= 0; s = _n[s].Next)
            {
                nodes++;
            }

            h.Feed((ulong)nodes);
            for (int s = _firstNode; s >= 0; s = _n[s].Next)
            {
                ref NodeRec n = ref _n[s];
                h.Feed(n.Id);
                h.Feed((ulong)(int)n.Kind);
                h.Feed(n.Subject);
                h.Feed(n.Parent);
                bool hasCategory = n.Kind != DelayNodeKind.FlightTotal;
                h.Feed(hasCategory);
                if (hasCategory)
                {
                    h.Feed((ulong)(int)n.Category);
                }

                h.Feed(n.Ticks);
                h.Feed(n.RootCause);
                h.Feed(n.Linked);
                h.Feed(n.HasSource);
                if (n.HasSource)
                {
                    h.Feed(n.Source.Tick);
                    h.Feed((ulong)n.Source.Sequence);
                }

                h.Feed((ulong)(int)n.Src);
                h.Feed(n.A);
                h.Feed(n.B);
                h.Feed(n.CreatedAt);
            }

            return h.Result;
        }

        // ------------------------------------------------------------ queries

        public bool TryGetFlightDelay(FlightId flight, out FlightDelay delay)
        {
            int slot = Find(flight.Value);
            if (slot < 0)
            {
                delay = default;
                return false;
            }

            ref FlightRec f = ref _f[slot];
            delay = new FlightDelay(
                new FlightId(f.Flight),
                f.Kind,
                f.HasRotation,
                new FlightId(f.Rotation),
                new DelayEventId(_n[f.Root].Id),
                f.Total,
                Minutes(f.Total),
                f.Checkpoints,
                f.Last,
                f.Finalised,
                f.FinalisedAt,
                f.Missed,
                f.HasMissedAt ? new NodeId(f.MissedAt) : (NodeId?)null);
            return true;
        }

        public IReadOnlyList<FlightId> RetainedFlights()
        {
            var result = new FlightId[_flightCount];
            for (int p = 0; p < _flightCount; p++)
            {
                result[p] = new FlightId(_orderIds[p]);
            }

            return result;
        }

        public IReadOnlyList<DelayEventId> LeavesOf(FlightId flight)
        {
            int slot = Find(flight.Value);
            if (slot < 0)
            {
                return Array.Empty<DelayEventId>();
            }

            int count = 0;
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                count++;
            }

            var result = new DelayEventId[count];
            int k = 0;
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                result[k++] = new DelayEventId(_n[s].Id);
            }

            return result;
        }

        public bool TryGetNode(DelayEventId id, out DelayNode node)
        {
            if (id.Value != 0UL && _nodeById.TryGet(id.Value, out int slot))
            {
                node = ToNode(slot);
                return true;
            }

            node = default;
            return false;
        }
    }
}
