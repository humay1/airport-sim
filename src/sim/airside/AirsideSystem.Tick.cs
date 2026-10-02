using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Airside
{
    /// <summary>The per-tick steps S1 to S7. Spec: 12-interfaces-airside.md §12.8a.</summary>
    internal sealed partial class AirsideSystem
    {
        private const int ModeRunwayDue = 1;
        private const int ModeOnStand = 2;
        private const int ModeEdgeDue = 3;
        private const int ModeRunwayRequest = 4;

        private int[]? _cand;

        public void Tick(in TickContext ctx)
        {
            ulong t = ctx.Tick;
            _now = t;
            _askerCount = 0;
            _standReqCount = 0;

            // S1: the snapshot is read through _standFreedTick and _edgeLeftTick, which
            // record what was vacated during this tick, so there is nothing to copy.
            StepInbound(ctx, t);
            StepRunwayExits(ctx, t);
            StepGround(ctx, t);
            StepStands(ctx, t);
            StepTaxi(ctx, t);
            StepRunway(ctx, t);
        }

        private static EventRef Ref(EventId id)
        {
            return new EventRef(id, true);
        }

        private EventId Milestone(in TickContext ctx, ulong flight, FlightMilestone m, ulong planned, in EventRef cause)
        {
            return ctx.Events.Publish(new FlightMilestoneReached(new FlightId(flight), m, planned, ctx.Tick), cause);
        }

        private int Gather(int mode, ulong t)
        {
            int n = 0;
            for (int i = 0; i < _n; i++)
            {
                Slot s = _order[i];
                bool take;
                switch (mode)
                {
                    case ModeRunwayDue:
                        take = s.Phase == AircraftLegPhase.OnRunway && s.DueAt <= t;
                        break;
                    case ModeOnStand:
                        take = s.Phase == AircraftLegPhase.OnStand;
                        break;
                    case ModeEdgeDue:
                        take = s.Phase == AircraftLegPhase.Taxiing && s.DueAt <= t;
                        break;
                    default:
                        take = s.ReqTakeoff || (s.Kind == MovementKind.Arrival && s.Phase == AircraftLegPhase.AwaitingApproach && s.DueAt <= t);
                        break;
                }

                if (take)
                {
                    _scr[n++] = s;
                }
            }

            return n;
        }

        // ------------------------------------------------------------ S2

        private void StepInbound(in TickContext ctx, ulong t)
        {
            int kept = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                PendingEntry e = _pending[i];
                if (!e.IsDeparture && e.Start <= t)
                {
                    StartArrival(e.Flight, ctx, t);
                }
                else
                {
                    _pending[kept++] = e;
                }
            }

            _pendingCount = kept;
        }

        private void StartArrival(ulong flight, in TickContext ctx, ulong t)
        {
            if (!_schedule.TryGetFlight(new FlightId(flight), out FlightRecord r))
            {
                throw new SimInvariantException("sim.airside: pending arrival " + flight.ToString(CultureInfo.InvariantCulture) + " is not in the schedule", t);
            }

            Slot s = NewTrack(flight, MovementKind.Arrival);
            s.Phase = AircraftLegPhase.AwaitingApproach;
            s.PhaseEnteredAt = t;
            s.DueAt = r.ScheduledTick;
            s.Sched = r.ScheduledTick;
            s.MinTurnTicks = TurnTicks(r.MinTurnaround);
            s.SizeOrd = OrdinalOf(r.AircraftType);
            s.HasRotation = r.HasRotation;
            s.Rotation = r.Rotation.Value;
            Milestone(ctx, flight, FlightMilestone.InboundAirborne, ArrivalStart(r.ScheduledTick), EventRef.None);
        }

        // ------------------------------------------------------------ S3

        private void StepRunwayExits(in TickContext ctx, ulong t)
        {
            int n = Gather(ModeRunwayDue, t);
            for (int i = 0; i < n; i++)
            {
                Slot s = _scr[i];
                int r = s.Rwy;
                _rwyOccupant[r] = null;
                if (s.Kind == MovementKind.Arrival)
                {
                    Milestone(ctx, s.Flight, FlightMilestone.OffRunway, s.Sched + _rwyOccTicks[r], EventRef.None);
                    s.RouteRwy = r;
                    s.Rwy = -1;
                    s.Phase = AircraftLegPhase.HeldOnTaxiway;
                    s.PhaseEnteredAt = t;
                    s.DueAt = Unscheduled;
                    _standReqs[_standReqCount++] = s;
                }
                else
                {
                    ulong roll = s.Sched + _depTicks[(s.DepartStand * _rwyId.Length) + r];
                    Milestone(ctx, s.Flight, FlightMilestone.Airborne, roll + _rwyOccTicks[r], EventRef.None);
                    RemoveTrack(s);
                }
            }
        }

        // ------------------------------------------------------------ S4

        private void StepGround(in TickContext ctx, ulong t)
        {
            int n = Gather(ModeOnStand, t);
            for (int i = 0; i < n; i++)
            {
                Slot s = _scr[i];
                if (s.Kind == MovementKind.Arrival)
                {
                    if (!s.DoorsOpenFired)
                    {
                        if (s.DueAt <= t)
                        {
                            DoorsOpen(s, ctx, t);
                        }
                    }
                    else if (s.HasRotation && HandoffDue(s, t))
                    {
                        Handoff(s, ctx, t);
                    }
                }
                else if (s.HoldSince != Unscheduled)
                {
                    bool has = _flow!.TryGetOutstanding(new FlightId(s.Flight), out OutstandingPassengers o);
                    if (!has || t >= s.DueAt)
                    {
                        ctx.Events.Publish(new DepartureHeldForPassengersReleased(new FlightId(s.Flight), has ? o.Count : 0, null), Ref(s.HoldEvent));
                        s.HoldSince = Unscheduled;
                        s.DueAt = Unscheduled;
                        CloseDoors(s, ctx, t);
                    }
                }
                else if (_turnaround && s.Recorded.HasValue)
                {
                    DoorsClosePoint(s, s.Recorded, ctx, t);
                }
            }
        }

        private bool HandoffDue(Slot s, ulong t)
        {
            return _turnaround ? s.Recorded.HasValue : s.DueAt <= t;
        }

        private void DoorsOpen(Slot s, in TickContext ctx, ulong t)
        {
            s.DoorsOpenFired = true;
            Milestone(ctx, s.Flight, FlightMilestone.DoorsOpen, s.PlannedOnStand + _delayTicks, EventRef.None);
            s.DueAt = s.HasRotation && !_turnaround ? t + s.MinTurnTicks : Unscheduled;
            if (s.HasRotation && HandoffDue(s, t))
            {
                Handoff(s, ctx, t);
            }
        }

        private void Handoff(Slot arrival, in TickContext ctx, ulong t)
        {
            EventRef cause = _turnaround ? arrival.Recorded : EventRef.None;
            int stand = arrival.Stand;
            ulong depId = arrival.Rotation;
            if (!_schedule.TryGetFlight(new FlightId(depId), out FlightRecord dr))
            {
                throw new SimInvariantException("sim.airside: rotation " + depId.ToString(CultureInfo.InvariantCulture) + " is not in the schedule", t);
            }

            CreateDeparture(depId, dr, stand, cause, arrival, ctx, t);
        }

        /// <summary>
        /// Creates a departure's track directly in OnStand at <paramref name="stand"/>, fires its
        /// OnStand and, in the fallback, runs its doors-close point at once. When
        /// <paramref name="arrival"/> is set, it is removed after the stand is handed over.
        /// </summary>
        private void CreateDeparture(ulong flight, FlightRecord r, int stand, EventRef cause, Slot? arrival, in TickContext ctx, ulong t)
        {
            Slot d = NewTrack(flight, MovementKind.Departure);
            d.Phase = AircraftLegPhase.OnStand;
            d.AtNode = _standNode[stand];
            d.Stand = stand;
            d.DepartStand = stand;
            d.PhaseEnteredAt = t;
            d.Sched = r.ScheduledTick;
            d.MinTurnTicks = TurnTicks(r.MinTurnaround);
            d.SizeOrd = OrdinalOf(r.AircraftType);
            d.HasRotation = r.HasRotation;
            d.Rotation = r.Rotation.Value;
            _standHas[stand] = true;
            _standOccupant[stand] = flight;
            if (arrival != null)
            {
                RemoveTrack(arrival);
            }

            EventId on = Milestone(ctx, flight, FlightMilestone.OnStand, DueTick(d.Sched, d.MinTurnTicks), cause);
            if (!_turnaround)
            {
                DoorsClosePoint(d, Ref(on), ctx, t);
            }
        }

        private void DoorsClosePoint(Slot d, EventRef cause, in TickContext ctx, ulong t)
        {
            d.Recorded = EventRef.None;
            if (_flow != null && _holdTicks > 0 && _flow.TryGetOutstanding(new FlightId(d.Flight), out OutstandingPassengers o))
            {
                d.HoldEvent = ctx.Events.Publish(new DepartureHeldForPassengers(new FlightId(d.Flight), o.Count, o.MostHeldAt), cause);
                d.HoldSince = t;
                d.DueAt = t + _holdTicks;
                return;
            }

            CloseDoors(d, ctx, t);
        }

        private void CloseDoors(Slot d, in TickContext ctx, ulong t)
        {
            if (_flow != null)
            {
                _flow.Absorb(_standSink[d.Stand], new FlightId(d.Flight));
            }

            Milestone(ctx, d.Flight, FlightMilestone.DoorsClosed, d.Sched, EventRef.None);
            EventId push = Milestone(ctx, d.Flight, FlightMilestone.Pushback, d.Sched, EventRef.None);

            int st = d.Stand;
            _standHas[st] = false;
            _standFreedTick[st] = t;
            _standFreedCause[st] = Ref(push);
            d.Stand = -1;

            int r = ChooseRunway();
            d.Rwy = r;
            d.RouteRwy = r;
            int di = (d.DepartStand * _rwyId.Length) + r;
            d.RouteBase = _depBase[di];
            d.RouteLen = _depLen[di];
            d.RoutePos = 0;
            d.AtNode = _standNode[d.DepartStand];
            d.Phase = AircraftLegPhase.HeldOnTaxiway;
            d.PhaseEnteredAt = t;
            d.DueAt = Unscheduled;
            d.HoldSince = Unscheduled;
            Ask(d);
        }

        private int ChooseRunway()
        {
            int best = 0;
            for (int r = 1; r < _rwyQLen.Length; r++)
            {
                if (_rwyQLen[r] < _rwyQLen[best])
                {
                    best = r;
                }
            }

            return best;
        }

        private void Ask(Slot s)
        {
            s.AskEdge = _routeEdge[s.RouteBase + s.RoutePos];
            _askers[_askerCount++] = s;
        }

        // ------------------------------------------------------------ S5

        private int FindStand(int sizeOrd, ulong t)
        {
            for (int i = 0; i < _standId.Length; i++)
            {
                if (!_standHas[i] && _standFreedTick[i] != t && sizeOrd <= _standMaxOrd[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private void StepStands(in TickContext ctx, ulong t)
        {
            int kept = 0;
            for (int i = 0; i < _waitCount; i++)
            {
                WaitEntry w = _wait[i];
                int st = FindStand(w.SizeOrd, t);
                if (st < 0)
                {
                    _wait[kept++] = w;
                }
                else if (w.IsDeparture)
                {
                    StartDeparture(w.Flight, st, ctx, t);
                }
                else
                {
                    AssignArrival(Find(w.Flight)!, st, true, ctx);
                }
            }

            _waitCount = kept;

            int dn = 0;
            int keep = 0;
            for (int i = 0; i < _pendingCount; i++)
            {
                PendingEntry e = _pending[i];
                if (e.IsDeparture && e.Start <= t)
                {
                    _depDue[dn++] = e;
                }
                else
                {
                    _pending[keep++] = e;
                }
            }

            _pendingCount = keep;

            int ai = 0;
            int di = 0;
            while (ai < _standReqCount || di < dn)
            {
                if (di >= dn || (ai < _standReqCount && _standReqs[ai].Flight < _depDue[di].Flight))
                {
                    Slot a = _standReqs[ai++];
                    int st = FindStand(a.SizeOrd, t);
                    if (st >= 0)
                    {
                        AssignArrival(a, st, false, ctx);
                    }
                    else
                    {
                        CheckWaitRoom(a.Flight, t);
                        ctx.Events.Publish(new StandUnavailable(new FlightId(a.Flight), null, null), EventRef.None);
                        _wait[_waitCount++] = new WaitEntry(a.Flight, false, a.SizeOrd);
                    }
                }
                else
                {
                    ulong flight = _depDue[di++].Flight;
                    if (!_schedule.TryGetFlight(new FlightId(flight), out FlightRecord r))
                    {
                        throw new SimInvariantException("sim.airside: pending departure " + flight.ToString(CultureInfo.InvariantCulture) + " is not in the schedule", t);
                    }

                    int size = OrdinalOf(r.AircraftType);
                    int st = FindStand(size, t);
                    if (st >= 0)
                    {
                        StartDeparture(flight, st, ctx, t);
                    }
                    else
                    {
                        CheckWaitRoom(flight, t);
                        _wait[_waitCount++] = new WaitEntry(flight, true, size);
                    }
                }
            }
        }

        private void CheckWaitRoom(ulong flight, ulong t)
        {
            if (_waitCount >= StandWaitCapacity)
            {
                throw new SimInvariantException(
                    "sim.airside: the stand-wait queue is full (capacity 1024), cannot append flight " + flight.ToString(CultureInfo.InvariantCulture), t);
            }
        }

        private void StartDeparture(ulong flight, int stand, in TickContext ctx, ulong t)
        {
            if (!_schedule.TryGetFlight(new FlightId(flight), out FlightRecord r))
            {
                throw new SimInvariantException("sim.airside: departure " + flight.ToString(CultureInfo.InvariantCulture) + " is not in the schedule", t);
            }

            CreateDeparture(flight, r, stand, EventRef.None, null, ctx, t);
        }

        private void AssignArrival(Slot a, int stand, bool fromQueue, in TickContext ctx)
        {
            _standHas[stand] = true;
            _standOccupant[stand] = a.Flight;
            a.Stand = stand;
            int pair = (a.RouteRwy * _standId.Length) + stand;
            a.RouteBase = _arrBase[pair];
            a.RouteLen = _arrLen[pair];
            a.RoutePos = 0;
            if (fromQueue)
            {
                ctx.Events.Publish(new StandAssigned(new FlightId(a.Flight), new StandId(_standId[stand]), null), _standFreedCause[stand]);
            }

            Ask(a);
        }

        // ------------------------------------------------------------ S6

        private void StepTaxi(in TickContext ctx, ulong t)
        {
            int n = Gather(ModeEdgeDue, t);
            for (int i = 0; i < n; i++)
            {
                Slot s = _scr[i];
                int e = s.OnEdge;
                _edgeOcc[e] = null;
                _edgeLeftTick[e] = t;
                _edgeLeftFlight[e] = s.Flight;
                s.AtNode = _routeNode[s.RouteBase + s.RoutePos];
                s.OnEdge = -1;
                s.RoutePos++;
                if (s.RoutePos < s.RouteLen)
                {
                    s.Phase = AircraftLegPhase.HeldOnTaxiway;
                    s.PhaseEnteredAt = t;
                    s.DueAt = Unscheduled;
                    Ask(s);
                }
                else if (s.Kind == MovementKind.Arrival)
                {
                    s.Phase = AircraftLegPhase.OnStand;
                    s.PhaseEnteredAt = t;
                    s.PlannedOnStand = s.Sched + _rwyOccTicks[s.RouteRwy] + _arrTicks[(s.RouteRwy * _standId.Length) + s.Stand];
                    Milestone(ctx, s.Flight, FlightMilestone.OnStand, s.PlannedOnStand, EventRef.None);
                    s.DueAt = t + _delayTicks;
                    if (_delayTicks == 0UL)
                    {
                        DoorsOpen(s, ctx, t);
                    }
                }
                else
                {
                    s.Phase = AircraftLegPhase.HeldForRunway;
                    s.PhaseEnteredAt = t;
                    s.DueAt = Unscheduled;
                    s.ReqTakeoff = true;
                }
            }

            GrantEdges(ctx, t);
        }

        private void GrantEdges(in TickContext ctx, ulong t)
        {
            int k = _askerCount;
            for (int i = 1; i < k; i++)
            {
                Slot x = _askers[i];
                int j = i - 1;
                while (j >= 0 && (_askers[j].AskEdge > x.AskEdge || (_askers[j].AskEdge == x.AskEdge && _askers[j].Flight > x.Flight)))
                {
                    _askers[j + 1] = _askers[j];
                    j--;
                }

                _askers[j + 1] = x;
            }

            int[] cand = _cand ??= new int[_edgeId.Length];
            int cn = 0;
            for (int i = 0; i < k; i++)
            {
                int e = _askers[i].AskEdge;
                if (_edgeStamp[e] != t)
                {
                    _edgeStamp[e] = t;
                    cand[cn++] = e;
                }
            }

            for (int i = 0; i < _heldEdgeCount; i++)
            {
                int e = _heldEdges[i];
                if (_edgeStamp[e] != t)
                {
                    _edgeStamp[e] = t;
                    cand[cn++] = e;
                }
            }

            for (int i = 1; i < cn; i++)
            {
                int x = cand[i];
                int j = i - 1;
                while (j >= 0 && cand[j] > x)
                {
                    cand[j + 1] = cand[j];
                    j--;
                }

                cand[j + 1] = x;
            }

            int ai = 0;
            for (int c = 0; c < cn; c++)
            {
                int e = cand[c];
                int end = ai;
                while (end < k && _askers[end].AskEdge == e)
                {
                    end++;
                }

                Slot? occ = _edgeOcc[e];
                if (occ != null || _edgeLeftTick[e] == t)
                {
                    ulong blocking = occ != null ? occ.Flight : _edgeLeftFlight[e];
                    for (int a = ai; a < end; a++)
                    {
                        HoldOnEdge(_askers[a], e, blocking, ctx, t);
                    }
                }
                else
                {
                    Slot? grantee = null;
                    int first = ai;
                    if (_edgeQLen[e] > 0)
                    {
                        grantee = DequeueEdge(e);
                        ctx.Events.Publish(new AircraftHeldOnTaxiwayReleased(new FlightId(grantee.Flight), new TaxiEdgeId(_edgeId[e]), null), Ref(grantee.HoldEvent));
                    }
                    else if (first < end)
                    {
                        grantee = _askers[first++];
                    }

                    if (grantee != null)
                    {
                        _edgeOcc[e] = grantee;
                        grantee.OnEdge = e;
                        grantee.Phase = AircraftLegPhase.Taxiing;
                        grantee.PhaseEnteredAt = t;
                        grantee.DueAt = t + _edgeTicks[e];
                        for (int a = first; a < end; a++)
                        {
                            HoldOnEdge(_askers[a], e, grantee.Flight, ctx, t);
                        }
                    }
                }

                ai = end;
            }
        }

        private void HoldOnEdge(Slot s, int e, ulong blocking, in TickContext ctx, ulong t)
        {
            s.QNext = null;
            if (_edgeQTail[e] is null)
            {
                _edgeQHead[e] = s;
            }
            else
            {
                _edgeQTail[e]!.QNext = s;
            }

            _edgeQTail[e] = s;
            _edgeQLen[e]++;
            if (!_edgeIsHeld[e])
            {
                _edgeIsHeld[e] = true;
                _heldEdges[_heldEdgeCount++] = e;
            }

            s.HoldEvent = ctx.Events.Publish(new AircraftHeldOnTaxiway(new FlightId(s.Flight), new TaxiEdgeId(_edgeId[e]), new FlightId(blocking)), EventRef.None);
            s.Phase = AircraftLegPhase.HeldOnTaxiway;
            s.PhaseEnteredAt = t;
            s.DueAt = Unscheduled;
        }

        private Slot DequeueEdge(int e)
        {
            Slot head = _edgeQHead[e]!;
            _edgeQHead[e] = head.QNext;
            if (head.QNext is null)
            {
                _edgeQTail[e] = null;
            }

            head.QNext = null;
            _edgeQLen[e]--;
            if (_edgeQLen[e] == 0)
            {
                _edgeIsHeld[e] = false;
                for (int i = 0; i < _heldEdgeCount; i++)
                {
                    if (_heldEdges[i] == e)
                    {
                        _heldEdges[i] = _heldEdges[--_heldEdgeCount];
                        break;
                    }
                }
            }

            return head;
        }

        // ------------------------------------------------------------ S7

        private void StepRunway(in TickContext ctx, ulong t)
        {
            for (int r = 0; r < _rwyId.Length; r++)
            {
                Slot? head = _rwyQHead[r];
                if (head != null && t >= _rwyNextSlot[r] && _rwyOccupant[r] is null)
                {
                    _rwyQHead[r] = head.QNext;
                    if (head.QNext is null)
                    {
                        _rwyQTail[r] = null;
                    }

                    head.QNext = null;
                    _rwyQLen[r]--;
                    ctx.Events.Publish(new AircraftHeldForRunwayReleased(new FlightId(head.Flight), new RunwayId(_rwyId[r]), 0), Ref(head.HoldEvent));
                    ClaimRunway(head, r, ctx, t);
                }
            }

            int n = Gather(ModeRunwayRequest, t);
            for (int i = 0; i < n; i++)
            {
                Slot s = _scr[i];
                int r;
                if (s.Kind == MovementKind.Arrival)
                {
                    r = ChooseRunway();
                    s.Rwy = r;
                    s.RouteRwy = r;
                }
                else
                {
                    s.ReqTakeoff = false;
                    r = s.Rwy;
                }

                if (t >= _rwyNextSlot[r] && _rwyOccupant[r] is null)
                {
                    ClaimRunway(s, r, ctx, t);
                }
                else
                {
                    s.QNext = null;
                    if (_rwyQTail[r] is null)
                    {
                        _rwyQHead[r] = s;
                    }
                    else
                    {
                        _rwyQTail[r]!.QNext = s;
                    }

                    _rwyQTail[r] = s;
                    _rwyQLen[r]++;
                    s.HoldEvent = ctx.Events.Publish(new AircraftHeldForRunway(new FlightId(s.Flight), new RunwayId(_rwyId[r]), _rwyQLen[r]), EventRef.None);
                    s.Phase = AircraftLegPhase.HeldForRunway;
                    s.PhaseEnteredAt = t;
                    s.DueAt = Unscheduled;
                }
            }
        }

        private void ClaimRunway(Slot s, int r, in TickContext ctx, ulong t)
        {
            _rwyNextSlot[r] = t + _rwySep[r];
            _rwyOccupant[r] = s;
            s.Phase = AircraftLegPhase.OnRunway;
            s.PhaseEnteredAt = t;
            s.DueAt = t + _rwyOccTicks[r];
            s.AtNode = _rwyNode[r];
            s.Rwy = r;
            if (s.Kind == MovementKind.Arrival)
            {
                Milestone(ctx, s.Flight, FlightMilestone.Landed, s.Sched, EventRef.None);
            }
            else
            {
                Milestone(ctx, s.Flight, FlightMilestone.TakeoffRoll, s.Sched + _depTicks[(s.DepartStand * _rwyId.Length) + r], EventRef.None);
            }
        }
    }
}
