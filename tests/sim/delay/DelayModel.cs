using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// A reference model of sim.delay written straight from 14 §14.4 to §14.9,
    /// in integer ticks, with no attempt at efficiency. The generated-stream
    /// tests feed it the events sim.delay received, in the same dispatch order,
    /// and compare everything IDelaySystem exposes after every event. It only
    /// models well-formed streams: any emitter bug makes it throw, which fails
    /// the test as a broken generator.
    /// </summary>
    internal sealed class DelayModel
    {
        private const int Runway = 0;
        private const int Taxiway = 1;
        private const int Stand = 2;
        private const int Turnaround = 3;
        private const int PassengerHold = 4;

        private readonly SortedDictionary<ulong, MRecord> _records = new SortedDictionary<ulong, MRecord>();
        private readonly Dictionary<ulong, MNode> _nodes = new Dictionary<ulong, MNode>();
        private ulong _next = 1UL;
        private ulong _nextBoundary = DConst.TicksPerDay;

        /// <summary>Every DelayEvent the model publishes, in publication order, as text.</summary>
        public readonly List<string> Published = new List<string>();

        public ulong NextId => _next;

        public int RecordCount => _records.Count;

        private sealed class MRecord
        {
            public ulong Flight;
            public MovementKind Kind;
            public bool HasRotation;
            public ulong Rotation;
            public ulong Root;
            public ulong Total;
            public int Checkpoints;
            public ulong Last;
            public bool Finalised;
            public ulong FinalisedAt = DConst.TickUnscheduled;
            public int Missed;
            public NodeId? MissedAt;
            public readonly List<ulong> Leaves = new List<ulong>();
            public readonly List<MInterval> Intervals = new List<MInterval>();
            public readonly Dictionary<EventId, ulong> LeafByOpener = new Dictionary<EventId, ulong>();
            public ulong Unexplained;
            public ulong Inbound;
        }

        private sealed class MInterval
        {
            public EventId Opener;
            public int Family;
            public ulong Key;
            public DelayCategory Category;
            public DelaySource Source;
            public ulong Start;
            public ulong End = DConst.TickUnscheduled;
            public ulong A;
            public ulong B;
        }

        private sealed class MNode
        {
            public ulong Id;
            public DelayNodeKind Kind;
            public ulong Subject;
            public ulong Parent;
            public DelayCategory? Category;
            public ulong Ticks;
            public bool RootCause;
            public ulong Linked;
            public EventRef Source;
            public DelaySource ExplSource;
            public ulong A;
            public ulong B;
            public ulong CreatedAt;

            public DelayNode ToNode()
            {
                return new DelayNode(
                    new DelayEventId(Id),
                    Kind,
                    new FlightId(Subject),
                    new DelayEventId(Parent),
                    Category,
                    Ticks,
                    DConst.Minutes(Ticks),
                    RootCause,
                    new FlightId(Linked),
                    Source,
                    new DelayExplanation(ExplSource, A, B),
                    CreatedAt);
            }
        }

        private static InvalidOperationException Bug(string what, ulong tick)
        {
            return new InvalidOperationException("model: the generated stream is not well formed at tick " + tick.ToString(CultureInfo.InvariantCulture) + ": " + what);
        }

        /// <summary>14 §14.8: the pruning sim.delay's Tick does at every day boundary up to and including <paramref name="tick"/>.</summary>
        public void PruneThrough(ulong tick)
        {
            while (_nextBoundary <= tick)
            {
                Prune(_nextBoundary);
                _nextBoundary += DConst.TicksPerDay;
            }
        }

        private void Prune(ulong boundary)
        {
            ulong day = boundary / DConst.TicksPerDay;
            if (day < DConst.RetentionDays - 1UL)
            {
                return;
            }

            ulong threshold = (day - (DConst.RetentionDays - 1UL)) * DConst.TicksPerDay;
            foreach (ulong id in new List<ulong>(_records.Keys))
            {
                if (!_records.TryGetValue(id, out MRecord? r))
                {
                    continue;
                }

                MRecord? partner = null;
                if (r.HasRotation)
                {
                    _records.TryGetValue(r.Rotation, out partner);
                }

                bool old = r.Finalised && r.FinalisedAt < threshold && (partner == null || (partner.Finalised && partner.FinalisedAt < threshold));
                if (old)
                {
                    Remove(r);
                    if (partner != null)
                    {
                        Remove(partner);
                    }
                }
            }
        }

        private void Remove(MRecord r)
        {
            _nodes.Remove(r.Root);
            foreach (ulong leaf in r.Leaves)
            {
                _nodes.Remove(leaf);
            }

            _records.Remove(r.Flight);
        }

        /// <summary>One event, as sim.delay's handler receives it.</summary>
        public void Apply(in EventEnvelope env, object evt)
        {
            PruneThrough(env.Tick);
            switch (evt)
            {
                case FlightPlanPublished p:
                    Publish(env, p);
                    break;
                case FlightMilestoneReached m:
                    Milestone(env, m);
                    break;
                case AircraftHeldForRunway e:
                    Open(env, e.Flight.Value, Runway, e.Runway.Value, DelayCategory.RunwayCongestion, DelaySource.RunwayHold, e.Runway.Value, (ulong)e.QueuePosition);
                    break;
                case AircraftHeldForRunwayReleased e:
                    Close(env, e.Flight.Value, Runway, e.Runway.Value);
                    break;
                case AircraftHeldOnTaxiway e:
                    Open(env, e.Flight.Value, Taxiway, e.Edge.Value, DelayCategory.TaxiCongestion, DelaySource.TaxiwayHold, e.Edge.Value, e.Blocking.HasValue ? e.Blocking.Value.Value : 0UL);
                    break;
                case AircraftHeldOnTaxiwayReleased e:
                    Close(env, e.Flight.Value, Taxiway, e.Edge.Value);
                    break;
                case StandUnavailable e:
                    {
                        ulong a = e.Stand.HasValue ? e.Stand.Value.Value + 1UL : 0UL;
                        Open(env, e.Flight.Value, Stand, a, DelayCategory.StandUnavailable, DelaySource.StandUnavailable, a, e.Occupying.HasValue ? e.Occupying.Value.Value : 0UL);
                        break;
                    }

                case StandAssigned e:
                    Close(env, e.Flight.Value, Stand, e.Stand.HasValue ? e.Stand.Value.Value + 1UL : 0UL);
                    break;
                case TurnaroundJobBlocked e:
                    if (e.WaitingOn != ResourceKind.JobDependency)
                    {
                        Open(env, e.Flight.Value, Turnaround, (ulong)(int)e.Job, e.Category, DelaySource.TurnaroundJobWait, (ulong)(int)e.Job, (ulong)(int)e.WaitingOn);
                    }

                    break;
                case TurnaroundJobUnblocked e:
                    if (e.WaitingOn != ResourceKind.JobDependency)
                    {
                        Close(env, e.Flight.Value, Turnaround, (ulong)(int)e.Job);
                    }

                    break;
                case DepartureHeldForPassengers e:
                    if (!e.HeldAt.HasValue)
                    {
                        throw Bug("passenger hold opened with no heldAt", env.Tick);
                    }

                    Open(env, e.Flight.Value, PassengerHold, 0UL, DelayCategory.PassengerLate, DelaySource.PassengerHold, e.HeldAt.Value.Value, (ulong)e.Outstanding);
                    break;
                case DepartureHeldForPassengersReleased e:
                    Close(env, e.Flight.Value, PassengerHold, 0UL);
                    break;
                case PassengersMissedFlight e:
                    {
                        MRecord r = Known(e.Flight.Value, env.Tick);
                        r.Missed += e.Count;
                        if (!r.MissedAt.HasValue)
                        {
                            r.MissedAt = e.LastBlockedAt;
                        }

                        break;
                    }

                default:
                    break;
            }
        }

        private MRecord Known(ulong flight, ulong tick)
        {
            if (!_records.TryGetValue(flight, out MRecord? r))
            {
                throw Bug("event for unknown flight " + flight.ToString(CultureInfo.InvariantCulture), tick);
            }

            return r;
        }

        private MNode NewNode(MRecord r, DelayNodeKind kind, DelayCategory? category, bool rootCause, ulong linked, EventRef source, DelaySource expl, ulong a, ulong b, ulong tick)
        {
            var n = new MNode
            {
                Id = _next++,
                Kind = kind,
                Subject = r.Flight,
                Parent = kind == DelayNodeKind.FlightTotal ? 0UL : r.Root,
                Category = category,
                RootCause = rootCause,
                Linked = linked,
                Source = source,
                ExplSource = expl,
                A = a,
                B = b,
                CreatedAt = tick,
            };
            _nodes[n.Id] = n;
            if (kind == DelayNodeKind.Allocation)
            {
                r.Leaves.Add(n.Id);
            }

            return n;
        }

        private void Publish(in EventEnvelope env, FlightPlanPublished p)
        {
            if (_records.ContainsKey(p.Flight.Value))
            {
                throw Bug("second FlightPlanPublished", env.Tick);
            }

            var r = new MRecord
            {
                Flight = p.Flight.Value,
                Kind = p.Kind,
                HasRotation = p.HasRotation,
                Rotation = p.Rotation.Value,
                Last = env.Tick,
            };
            _records[r.Flight] = r;
            r.Root = NewNode(r, DelayNodeKind.FlightTotal, null, false, 0UL, EventRef.None, DelaySource.FlightTotal, 0UL, 0UL, env.Tick).Id;
        }

        private void Open(in EventEnvelope env, ulong flight, int family, ulong key, DelayCategory category, DelaySource source, ulong a, ulong b)
        {
            MRecord r = Known(flight, env.Tick);
            if (r.Finalised)
            {
                return;
            }

            foreach (MInterval i in r.Intervals)
            {
                if (i.Family == family && i.Key == key && i.End == DConst.TickUnscheduled)
                {
                    throw Bug("two open intervals with one key", env.Tick);
                }
            }

            r.Intervals.Add(new MInterval { Opener = env.Id, Family = family, Key = key, Category = category, Source = source, Start = env.Tick, A = a, B = b });
        }

        private void Close(in EventEnvelope env, ulong flight, int family, ulong key)
        {
            MRecord r = Known(flight, env.Tick);
            if (r.Finalised)
            {
                return;
            }

            foreach (MInterval i in r.Intervals)
            {
                if (i.Family == family && i.Key == key && i.End == DConst.TickUnscheduled)
                {
                    i.End = env.Tick;
                    return;
                }
            }

            throw Bug("close with no open interval", env.Tick);
        }

        private int ChainDepth(MRecord r)
        {
            int depth = 1;
            foreach (ulong leaf in r.Leaves)
            {
                MNode n = _nodes[leaf];
                depth = Math.Max(depth, n.Linked != 0UL ? 2 + ChainDepth(_records[n.Linked]) : 2);
            }

            return depth;
        }

        private void Milestone(in EventEnvelope env, FlightMilestoneReached m)
        {
            MRecord r = Known(m.Flight.Value, env.Tick);
            FlightMilestone[] cps = DConst.Checkpoints(r.Kind);
            int k = Array.IndexOf(cps, m.Milestone);
            if (k < 0)
            {
                return;
            }

            if (r.Finalised || k != r.Checkpoints || m.PlannedTick == DConst.TickUnscheduled)
            {
                throw Bug("checkpoint out of order, twice, or unscheduled", env.Tick);
            }

            ulong actual = m.ActualTick;
            ulong late = actual > m.PlannedTick ? actual - m.PlannedTick : 0UL;
            if (late > r.Total)
            {
                ulong delta = late - r.Total;
                ulong rest;
                if (r.Kind == MovementKind.Departure && m.Milestone == FlightMilestone.OnStand)
                {
                    rest = delta;
                    if (r.HasRotation)
                    {
                        if (!_records.TryGetValue(r.Rotation, out MRecord? inbound) || !inbound.Finalised)
                        {
                            throw Bug("departure OnStand before its inbound finalised", env.Tick);
                        }

                        ulong take = Math.Min(delta, inbound.Total);
                        if (take > 0UL && 2 + ChainDepth(inbound) <= DConst.MaxAttributionDepth)
                        {
                            MNode leaf = NewNode(r, DelayNodeKind.Allocation, DelayCategory.LateInbound, false, inbound.Flight, EventRef.None, DelaySource.InboundAircraft, inbound.Flight, 0UL, env.Tick);
                            leaf.Ticks = take;
                            r.Inbound = leaf.Id;
                            rest = delta - take;
                        }
                    }
                }
                else
                {
                    rest = Allocate(r, actual, delta, env.Tick);
                }

                if (rest > 0UL)
                {
                    if (r.Unexplained == 0UL)
                    {
                        r.Unexplained = NewNode(r, DelayNodeKind.Allocation, DelayCategory.Propagated, true, 0UL, EventRef.None, DelaySource.Unexplained, 0UL, 0UL, env.Tick).Id;
                    }

                    _nodes[r.Unexplained].Ticks += rest;
                }
            }
            else if (late < r.Total)
            {
                ulong remove = r.Total - late;
                for (int i = r.Leaves.Count - 1; i >= 0 && remove > 0UL; i--)
                {
                    MNode n = _nodes[r.Leaves[i]];
                    ulong take = Math.Min(n.Ticks, remove);
                    n.Ticks -= take;
                    remove -= take;
                    if (n.Ticks == 0UL)
                    {
                        RemoveLeaf(r, n);
                    }
                }
            }

            r.Total = late;
            _nodes[r.Root].Ticks = late;
            r.Last = actual;
            r.Checkpoints++;
            r.Intervals.RemoveAll(i => i.End != DConst.TickUnscheduled && i.End <= actual);
            if (r.Checkpoints == cps.Length)
            {
                r.Finalised = true;
                r.FinalisedAt = env.Tick;
                r.Intervals.Clear();
                Published.Add(PublishedText(env.Tick, env.Id, _nodes[r.Root].ToNode()));
                foreach (ulong leaf in r.Leaves)
                {
                    Published.Add(PublishedText(env.Tick, env.Id, _nodes[leaf].ToNode()));
                }
            }
        }

        private void RemoveLeaf(MRecord r, MNode n)
        {
            r.Leaves.Remove(n.Id);
            _nodes.Remove(n.Id);
            if (r.Unexplained == n.Id)
            {
                r.Unexplained = 0UL;
            }

            if (r.Inbound == n.Id)
            {
                r.Inbound = 0UL;
            }

            EventId? opener = null;
            foreach (KeyValuePair<EventId, ulong> kv in r.LeafByOpener)
            {
                if (kv.Value == n.Id)
                {
                    opener = kv.Key;
                }
            }

            if (opener.HasValue)
            {
                r.LeafByOpener.Remove(opener.Value);
            }
        }

        /// <summary>14 §14.6 step 4: clip, first-blocker-wins ownership, cap in ascending OpenerId. Returns the residue.</summary>
        private ulong Allocate(MRecord r, ulong actual, ulong delta, ulong tick)
        {
            var union = new List<(ulong S, ulong E)>();
            var order = new List<MInterval>(r.Intervals);
            order.Sort((x, y) => x.Opener.CompareTo(y.Opener));
            ulong remaining = delta;
            foreach (MInterval i in order)
            {
                ulong s = Math.Max(i.Start, r.Last);
                ulong e = i.End == DConst.TickUnscheduled ? actual : Math.Min(i.End, actual);
                if (e <= s)
                {
                    continue;
                }

                ulong overlap = 0UL;
                foreach ((ulong us, ulong ue) in union)
                {
                    ulong lo = Math.Max(us, s);
                    ulong hi = Math.Min(ue, e);
                    if (hi > lo)
                    {
                        overlap += hi - lo;
                    }
                }

                ulong owned = (e - s) - overlap;
                union.Add((s, e));
                union = Merge(union);
                ulong a = Math.Min(owned, remaining);
                if (a == 0UL)
                {
                    continue;
                }

                if (!r.LeafByOpener.TryGetValue(i.Opener, out ulong leafId))
                {
                    leafId = NewNode(r, DelayNodeKind.Allocation, i.Category, true, 0UL, new EventRef(i.Opener, true), i.Source, i.A, i.B, tick).Id;
                    r.LeafByOpener[i.Opener] = leafId;
                }

                _nodes[leafId].Ticks += a;
                remaining -= a;
            }

            return remaining;
        }

        private static List<(ulong S, ulong E)> Merge(List<(ulong S, ulong E)> segments)
        {
            segments.Sort((x, y) => x.S.CompareTo(y.S));
            var merged = new List<(ulong S, ulong E)>();
            foreach ((ulong s, ulong e) in segments)
            {
                if (merged.Count > 0 && s <= merged[merged.Count - 1].E)
                {
                    (ulong ps, ulong pe) = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (ps, Math.Max(pe, e));
                }
                else
                {
                    merged.Add((s, e));
                }
            }

            return merged;
        }

        public static string PublishedText(ulong tick, EventId cause, in DelayNode node)
        {
            return string.Format(CultureInfo.InvariantCulture, "t={0} src={1} cause={2} {3}", tick, DConst.DelayPos, Show.Id(cause), Show.Node(node));
        }

        /// <summary>The same text Show.Tree gives for the implementation.</summary>
        public string Tree(ulong flight)
        {
            if (!_records.TryGetValue(flight, out MRecord? r))
            {
                return "flight " + flight.ToString(CultureInfo.InvariantCulture) + " absent";
            }

            var sb = new StringBuilder();
            sb.Append(Show.RecordLine(
                r.Flight,
                r.Kind,
                r.HasRotation,
                r.Rotation,
                r.Root,
                r.Total,
                DConst.Minutes(r.Total),
                r.Checkpoints,
                r.Last,
                r.Finalised,
                r.FinalisedAt,
                r.Missed,
                r.MissedAt)).Append('\n');
            sb.Append("  ").Append(Show.Node(_nodes[r.Root].ToNode())).Append('\n');
            foreach (ulong leaf in r.Leaves)
            {
                sb.Append("  ").Append(Show.Node(_nodes[leaf].ToNode())).Append('\n');
            }

            return sb.ToString();
        }

        public List<string> Snapshot()
        {
            var lines = new List<string>();
            foreach (ulong f in _records.Keys)
            {
                lines.Add(Tree(f));
            }

            return lines;
        }
    }
}
