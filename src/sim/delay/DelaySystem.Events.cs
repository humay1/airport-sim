using System;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay
{
    internal sealed partial class DelaySystem
    {
        private static SimInvariantException Bug(string what, ulong tick, ulong flight)
        {
            return new SimInvariantException(
                "sim.delay: " + what + " (flight " + flight.ToString(CultureInfo.InvariantCulture) + ")",
                tick);
        }

        // ------------------------------------------------------------ publication

        private void OnPlan(in EventEnvelope env, in FlightPlanPublished evt, in TickContext ctx)
        {
            ulong flight = evt.Flight.Value;
            int pos = Search(flight);
            if (pos >= 0)
            {
                throw Bug("second FlightPlanPublished", env.Tick, flight);
            }

            int slot = AllocFlight();
            ref FlightRec f = ref _f[slot];
            f.Flight = flight;
            f.Kind = evt.Kind;
            f.HasRotation = evt.HasRotation;
            f.Rotation = evt.HasRotation ? evt.Rotation.Value : flight;
            f.Total = 0UL;
            f.Checkpoints = 0;
            f.Last = env.Tick;
            f.Finalised = false;
            f.FinalisedAt = Unscheduled;
            f.Missed = 0;
            f.HasMissedAt = false;
            f.MissedAt = 0U;
            f.Doomed = false;
            f.FirstLeaf = -1;
            f.LastLeaf = -1;
            f.FirstInterval = -1;
            f.LastInterval = -1;
            f.Root = -1;
            InsertFlight(~pos, flight, slot);
            int root = NewNode(slot, DelayNodeKind.FlightTotal, default, false, 0UL, false, default, DelaySource.FlightTotal, 0UL, 0UL, env.Tick);
            _f[slot].Root = root;
        }

        // ------------------------------------------------------------ checkpoints

        // The index of the milestone in the kind's checkpoint list, or -1; count is the list length.
        private static int CheckpointIndex(MovementKind kind, FlightMilestone m, out int count)
        {
            if (kind == MovementKind.Arrival)
            {
                count = 2;
                return m == FlightMilestone.Landed ? 0 : m == FlightMilestone.OnStand ? 1 : -1;
            }

            count = 3;
            return m == FlightMilestone.OnStand ? 0 : m == FlightMilestone.Pushback ? 1 : m == FlightMilestone.Airborne ? 2 : -1;
        }

        private void OnMilestone(in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx)
        {
            FlightMilestone m = evt.Milestone;
            if (m != FlightMilestone.Landed && m != FlightMilestone.OnStand && m != FlightMilestone.Pushback && m != FlightMilestone.Airborne)
            {
                return;
            }

            ulong flight = evt.Flight.Value;
            int slot = Find(flight);
            if (slot < 0)
            {
                throw Bug("checkpoint for a flight with no record", env.Tick, flight);
            }

            int k = CheckpointIndex(_f[slot].Kind, m, out int count);
            if (k < 0)
            {
                return;
            }

            if (_f[slot].Finalised || k != _f[slot].Checkpoints)
            {
                throw Bug("checkpoint out of order or repeated", env.Tick, flight);
            }

            if (evt.PlannedTick == Unscheduled)
            {
                throw Bug("checkpoint with no planned tick", env.Tick, flight);
            }

            ulong actual = evt.ActualTick;
            ulong late = actual > evt.PlannedTick ? actual - evt.PlannedTick : 0UL;
            ulong total = _f[slot].Total;
            if (late > total)
            {
                ulong delta = late - total;
                ulong rest;
                if (_f[slot].Kind == MovementKind.Departure && m == FlightMilestone.OnStand)
                {
                    rest = AllocateInbound(slot, delta, env.Tick);
                }
                else
                {
                    rest = AllocateGeneral(slot, actual, delta, env.Tick);
                }

                if (rest > 0UL)
                {
                    AddUnexplained(slot, rest, env.Tick);
                }
            }
            else if (late < total)
            {
                Recover(slot, total - late);
            }

            ref FlightRec f = ref _f[slot];
            f.Total = late;
            _n[f.Root].Ticks = late;
            f.Last = actual;
            f.Checkpoints++;
            bool terminal = f.Checkpoints == count;
            DiscardIntervals(slot, actual, terminal);
            if (terminal)
            {
                f.Finalised = true;
                f.FinalisedAt = env.Tick;
                Publish(slot, env, ctx);
            }
        }

        // 14 §14.6 step 3. Returns what is left for the Unexplained leaf.
        private ulong AllocateInbound(int slot, ulong delta, ulong tick)
        {
            if (!_f[slot].HasRotation)
            {
                return delta;
            }

            ulong flight = _f[slot].Flight;
            int inboundSlot = Find(_f[slot].Rotation);
            if (inboundSlot < 0 || !_f[inboundSlot].Finalised)
            {
                throw Bug("departure OnStand before its inbound finalised", tick, flight);
            }

            ulong take = Math.Min(delta, _f[inboundSlot].Total);
            if (take == 0UL || 2 + ChainDepth(inboundSlot) > SimConstants.MAX_ATTRIBUTION_DEPTH)
            {
                return delta;
            }

            ulong inbound = _f[inboundSlot].Flight;
            int leaf = NewNode(slot, DelayNodeKind.Allocation, DelayCategory.LateInbound, false, inbound, false, default, DelaySource.InboundAircraft, inbound, 0UL, tick);
            _n[leaf].Ticks = take;
            return delta - take;
        }

        // 14 §14.7: 1 with no leaves, else the deepest leaf, a linked one adding the linked tree's depth.
        private int ChainDepth(int slot)
        {
            int depth = 1;
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                int d = 2;
                if (_n[s].Linked != 0UL)
                {
                    int linked = Find(_n[s].Linked);
                    if (linked >= 0)
                    {
                        d = 2 + ChainDepth(linked);
                    }
                }

                if (d > depth)
                {
                    depth = d;
                }
            }

            return depth;
        }

        // 14 §14.6 step 4. Returns the residue.
        private ulong AllocateGeneral(int slot, ulong actual, ulong delta, ulong tick)
        {
            ulong remaining = delta;
            ulong last = _f[slot].Last;
            _segCount = 0;
            for (int s = _f[slot].FirstInterval; s >= 0 && remaining > 0UL; s = _i[s].FlightNext)
            {
                ulong start = _i[s].Start > last ? _i[s].Start : last;
                ulong end = _i[s].End == Unscheduled ? actual : (_i[s].End < actual ? _i[s].End : actual);
                if (end <= start)
                {
                    continue;
                }

                ulong owned = (end - start) - CoveredBy(start, end);
                AddSegment(start, end);
                ulong a = owned < remaining ? owned : remaining;
                if (a == 0UL)
                {
                    continue;
                }

                int leaf = FindLeafOf(slot, _i[s].Opener);
                if (leaf < 0)
                {
                    leaf = NewNode(slot, DelayNodeKind.Allocation, _i[s].Category, true, 0UL, true, _i[s].Opener, SourceOf(_i[s].Family), _i[s].A, _i[s].B, tick);
                }

                _n[leaf].Ticks += a;
                remaining -= a;
            }

            return remaining;
        }

        private static DelaySource SourceOf(int family)
        {
            switch (family)
            {
                case FamilyRunway:
                    return DelaySource.RunwayHold;
                case FamilyTaxiway:
                    return DelaySource.TaxiwayHold;
                case FamilyStand:
                    return DelaySource.StandUnavailable;
                case FamilyTurnaround:
                    return DelaySource.TurnaroundJobWait;
                default:
                    return DelaySource.PassengerHold;
            }
        }

        private int FindLeafOf(int slot, EventId opener)
        {
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                if (_n[s].HasSource && _n[s].Source.Equals(opener))
                {
                    return s;
                }
            }

            return -1;
        }

        // Ticks of [start, end) already covered by the segments added so far.
        private ulong CoveredBy(ulong start, ulong end)
        {
            ulong covered = 0UL;
            for (int g = 0; g < _segCount; g++)
            {
                ulong lo = _segStart[g] > start ? _segStart[g] : start;
                ulong hi = _segEnd[g] < end ? _segEnd[g] : end;
                if (hi > lo)
                {
                    covered += hi - lo;
                }
            }

            return covered;
        }

        // Adds [start, end), merging every segment it overlaps or touches.
        private void AddSegment(ulong start, ulong end)
        {
            int first = 0;
            while (first < _segCount && _segEnd[first] < start)
            {
                first++;
            }

            int lastPlusOne = first;
            while (lastPlusOne < _segCount && _segStart[lastPlusOne] <= end)
            {
                if (_segStart[lastPlusOne] < start)
                {
                    start = _segStart[lastPlusOne];
                }

                if (_segEnd[lastPlusOne] > end)
                {
                    end = _segEnd[lastPlusOne];
                }

                lastPlusOne++;
            }

            int removed = lastPlusOne - first;
            int shift = 1 - removed;
            if (shift > 0 && _segCount + shift > _segStart.Length)
            {
                Array.Resize(ref _segStart, _segStart.Length * 2);
                Array.Resize(ref _segEnd, _segEnd.Length * 2);
            }

            if (shift != 0)
            {
                Array.Copy(_segStart, lastPlusOne, _segStart, first + 1, _segCount - lastPlusOne);
                Array.Copy(_segEnd, lastPlusOne, _segEnd, first + 1, _segCount - lastPlusOne);
                _segCount += shift;
            }

            _segStart[first] = start;
            _segEnd[first] = end;
        }

        // 14 §14.6 step 5: at most one Unexplained leaf per flight.
        private void AddUnexplained(int slot, ulong ticks, ulong tick)
        {
            int leaf = -1;
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                if (_n[s].Src == DelaySource.Unexplained)
                {
                    leaf = s;
                    break;
                }
            }

            if (leaf < 0)
            {
                leaf = NewNode(slot, DelayNodeKind.Allocation, DelayCategory.Propagated, true, 0UL, false, default, DelaySource.Unexplained, 0UL, 0UL, tick);
            }

            _n[leaf].Ticks += ticks;
        }

        // 14 §14.6 step 6: the most recently created leaf gives up its ticks first.
        private void Recover(int slot, ulong remove)
        {
            int s = _f[slot].LastLeaf;
            while (s >= 0 && remove > 0UL)
            {
                int prev = _n[s].LeafPrev;
                ulong take = _n[s].Ticks < remove ? _n[s].Ticks : remove;
                _n[s].Ticks -= take;
                remove -= take;
                if (_n[s].Ticks == 0UL)
                {
                    FreeNode(s, slot);
                }

                s = prev;
            }
        }

        // 14 §14.5: closed intervals that end at or before the checkpoint go; at finalisation, all of them.
        private void DiscardIntervals(int slot, ulong actual, bool all)
        {
            int s = _f[slot].FirstInterval;
            while (s >= 0)
            {
                int next = _i[s].FlightNext;
                if (all || (_i[s].End != Unscheduled && _i[s].End <= actual))
                {
                    FreeInterval(s, slot);
                }

                s = next;
            }
        }

        // 14 §14.8: the root, then the leaves ascending, each caused by the terminal checkpoint.
        private void Publish(int slot, in EventEnvelope env, in TickContext ctx)
        {
            var cause = new EventRef(env.Id, true);
            var root = new DelayEvent(ToNode(_f[slot].Root));
            ctx.Events.Publish<DelayEvent>(in root, in cause);
            for (int s = _f[slot].FirstLeaf; s >= 0; s = _n[s].LeafNext)
            {
                var leaf = new DelayEvent(ToNode(s));
                ctx.Events.Publish<DelayEvent>(in leaf, in cause);
            }
        }

        // ------------------------------------------------------------ intervals

        private void Open(in EventEnvelope env, ulong flight, int family, ulong key, int job, DelayCategory category, ulong a, ulong b)
        {
            int slot = Find(flight);
            if (slot < 0)
            {
                throw Bug("interval opened for an unknown flight", env.Tick, flight);
            }

            if (_f[slot].Finalised)
            {
                return;
            }

            for (int s = _f[slot].FirstInterval; s >= 0; s = _i[s].FlightNext)
            {
                if (_i[s].Family == family && _i[s].End == Unscheduled && KeyOf(in _i[s]) == key)
                {
                    throw Bug("two open intervals with one key", env.Tick, flight);
                }
            }

            int slotI = AllocInterval();
            ref IntervalRec r = ref _i[slotI];
            r.Opener = env.Id;
            r.Flight = flight;
            r.Family = family;
            r.Job = job;
            r.Category = category;
            r.Start = env.Tick;
            r.End = Unscheduled;
            r.A = a;
            r.B = b;
            r.Prev = _lastInterval;
            r.Next = -1;
            if (_lastInterval >= 0)
            {
                _i[_lastInterval].Next = slotI;
            }
            else
            {
                _firstInterval = slotI;
            }

            _lastInterval = slotI;
            ref FlightRec f = ref _f[slot];
            r.FlightPrev = f.LastInterval;
            r.FlightNext = -1;
            if (f.LastInterval >= 0)
            {
                _i[f.LastInterval].FlightNext = slotI;
            }
            else
            {
                f.FirstInterval = slotI;
            }

            f.LastInterval = slotI;
        }

        private void Close(in EventEnvelope env, ulong flight, int family, ulong key)
        {
            int slot = Find(flight);
            if (slot < 0)
            {
                throw Bug("interval closed for an unknown flight", env.Tick, flight);
            }

            if (_f[slot].Finalised)
            {
                return;
            }

            for (int s = _f[slot].FirstInterval; s >= 0; s = _i[s].FlightNext)
            {
                if (_i[s].Family == family && _i[s].End == Unscheduled && KeyOf(in _i[s]) == key)
                {
                    _i[s].End = env.Tick;
                    return;
                }
            }

            throw Bug("interval closed with no open interval", env.Tick, flight);
        }

        private void OnRunwayHeld(in EventEnvelope env, in AircraftHeldForRunway e, in TickContext ctx)
        {
            Open(env, e.Flight.Value, FamilyRunway, e.Runway.Value, 0, DelayCategory.RunwayCongestion, e.Runway.Value, (ulong)e.QueuePosition);
        }

        private void OnRunwayReleased(in EventEnvelope env, in AircraftHeldForRunwayReleased e, in TickContext ctx)
        {
            Close(env, e.Flight.Value, FamilyRunway, e.Runway.Value);
        }

        private void OnTaxiwayHeld(in EventEnvelope env, in AircraftHeldOnTaxiway e, in TickContext ctx)
        {
            Open(env, e.Flight.Value, FamilyTaxiway, e.Edge.Value, 0, DelayCategory.TaxiCongestion, e.Edge.Value, e.Blocking.HasValue ? e.Blocking.Value.Value : 0UL);
        }

        private void OnTaxiwayReleased(in EventEnvelope env, in AircraftHeldOnTaxiwayReleased e, in TickContext ctx)
        {
            Close(env, e.Flight.Value, FamilyTaxiway, e.Edge.Value);
        }

        // 14 §14.5 (Q-107): the Stand key is the flight alone.
        private void OnStandUnavailable(in EventEnvelope env, in StandUnavailable e, in TickContext ctx)
        {
            ulong a = e.Stand.HasValue ? (ulong)e.Stand.Value.Value + 1UL : 0UL;
            Open(env, e.Flight.Value, FamilyStand, 0UL, 0, DelayCategory.StandUnavailable, a, e.Occupying.HasValue ? e.Occupying.Value.Value : 0UL);
        }

        private void OnStandAssigned(in EventEnvelope env, in StandAssigned e, in TickContext ctx)
        {
            Close(env, e.Flight.Value, FamilyStand, 0UL);
        }

        private void OnJobBlocked(in EventEnvelope env, in TurnaroundJobBlocked e, in TickContext ctx)
        {
            if (e.WaitingOn == ResourceKind.JobDependency)
            {
                return;
            }

            Open(env, e.Flight.Value, FamilyTurnaround, (ulong)(int)e.Job, (int)e.Job, e.Category, (ulong)(int)e.Job, (ulong)(int)e.WaitingOn);
        }

        private void OnJobUnblocked(in EventEnvelope env, in TurnaroundJobUnblocked e, in TickContext ctx)
        {
            if (e.WaitingOn == ResourceKind.JobDependency)
            {
                return;
            }

            Close(env, e.Flight.Value, FamilyTurnaround, (ulong)(int)e.Job);
        }

        private void OnHoldOpened(in EventEnvelope env, in DepartureHeldForPassengers e, in TickContext ctx)
        {
            if (!e.HeldAt.HasValue)
            {
                throw Bug("passenger hold opened with no heldAt", env.Tick, e.Flight.Value);
            }

            Open(env, e.Flight.Value, FamilyPassengerHold, 0UL, 0, DelayCategory.PassengerLate, e.HeldAt.Value.Value, (ulong)e.Outstanding);
        }

        private void OnHoldReleased(in EventEnvelope env, in DepartureHeldForPassengersReleased e, in TickContext ctx)
        {
            Close(env, e.Flight.Value, FamilyPassengerHold, 0UL);
        }

        // ------------------------------------------------------------ missed passengers

        private void OnMissed(in EventEnvelope env, in PassengersMissedFlight e, in TickContext ctx)
        {
            int slot = Find(e.Flight.Value);
            if (slot < 0)
            {
                throw Bug("missed passengers for an unknown flight", env.Tick, e.Flight.Value);
            }

            ref FlightRec f = ref _f[slot];
            f.Missed += e.Count;
            if (!f.HasMissedAt)
            {
                f.HasMissedAt = true;
                f.MissedAt = e.LastBlockedAt.Value;
            }
        }
    }
}
