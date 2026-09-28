using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// The one <see cref="IScheduleSystem"/> implementation. Spec: 11-interfaces-schedule.md,
    /// registry position 2 (§11.7).
    /// </summary>
    internal sealed class ScheduleSystem : IScheduleSystem
    {
        private const ulong PlanPublishLeadTicks = SimConstants.TICKS_PER_SIM_DAY;
        private const ulong FlightIdDayStride = 100000UL;
        private const ulong TickUnscheduled = ulong.MaxValue;

        private static readonly Comparison<(ulong Tick, FlightId Id)> TickThenIdComparer =
            (a, b) =>
            {
                int c = a.Tick.CompareTo(b.Tick);
                return c != 0 ? c : a.Id.Value.CompareTo(b.Id.Value);
            };

        private readonly ScheduleTable _table;
        private readonly IFlowSystem? _flow;
        private readonly IReadOnlyList<ShowUpBucket>[] _curves;

        private readonly Dictionary<FlightId, FlightState> _byId = new Dictionary<FlightId, FlightState>();

        // Published flight ordinals, per day, each kept sorted ascending. Because
        // FlightId = day * FlightIdDayStride + ordinal + 1 and stride exceeds any
        // day's row count, concatenating days 0..highestDay ascending, each day's
        // ordinals ascending, is exactly ascending FlightId (§11.3, §11.7) even
        // though publication order over time follows scheduled time, not ordinal.
        private readonly Dictionary<uint, List<int>> _publishedOrdinalsByDay = new Dictionary<uint, List<int>>();
        private int _publishedTotalCount;

        private readonly List<(ulong Tick, FlightId Id)> _departureIndex = new List<(ulong Tick, FlightId Id)>();
        private readonly List<(ulong Tick, FlightId Id)> _arrivalIndex = new List<(ulong Tick, FlightId Id)>();
        private readonly Dictionary<ulong, List<FlightId>> _publishQueue = new Dictionary<ulong, List<FlightId>>();
        private readonly Dictionary<ulong, List<InjectionEntry>> _injectionQueue = new Dictionary<ulong, List<InjectionEntry>>();

        private uint _highestDay;

        internal ScheduleSystem(in SystemServices services, in ScheduleTable table, IFlowSystem? flow)
        {
            _table = table;
            _flow = flow;

            IReadOnlyList<FlightTemplate> rows = table.Rows;
            _curves = new IReadOnlyList<ShowUpBucket>[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                FlightTemplate row = rows[i];
                if (!services.Content.TryGet(row.AircraftType, out AircraftDefinition _))
                {
                    throw ResolutionFailure(row.FlightRef, "aircraft_type", row.AircraftType.Value);
                }

                if (!services.Content.TryGet(row.PaxProfile, out PaxProfileDefinition profile))
                {
                    throw ResolutionFailure(row.FlightRef, "pax_profile", row.PaxProfile.Value);
                }

                _curves[i] = profile.ShowUpCurve;
            }

            MaterializeDay(0);
        }

        public SystemId Id => new SystemId(2);

        public string Name => "sim.schedule";

        public bool TryGetFlight(FlightId id, out FlightRecord flight)
        {
            if (_byId.TryGetValue(id, out FlightState? state))
            {
                flight = state.Record;
                return true;
            }

            flight = default;
            return false;
        }

        public IReadOnlyList<FlightId> PublishedFlights()
        {
            var result = new List<FlightId>(_publishedTotalCount);
            for (uint d = 0; d <= _highestDay; d++)
            {
                if (_publishedOrdinalsByDay.TryGetValue(d, out List<int>? ordinals))
                {
                    for (int i = 0; i < ordinals.Count; i++)
                    {
                        result.Add(new FlightId(((ulong)d * FlightIdDayStride) + (ulong)ordinals[i] + 1UL));
                    }
                }
            }

            return result;
        }

        public IReadOnlyList<FlightId> MovementsBetween(ulong fromInclusive, ulong toExclusive, MovementKind kind)
        {
            var result = new List<FlightId>();
            if (fromInclusive >= toExclusive)
            {
                return result;
            }

            List<(ulong Tick, FlightId Id)> index = kind == MovementKind.Arrival ? _arrivalIndex : _departureIndex;
            int i = LowerBound(index, fromInclusive);
            while (i < index.Count && index[i].Tick < toExclusive)
            {
                result.Add(index[i].Id);
                i++;
            }

            return result;
        }

        public bool TryGetRotation(FlightId flight, out FlightId counterpart)
        {
            if (_byId.TryGetValue(flight, out FlightState? state) && state.Record.HasRotation)
            {
                counterpart = state.Record.Rotation;
                return true;
            }

            counterpart = default;
            return false;
        }

        public int PendingInjectionCount(FlightId flight)
        {
            if (!_byId.TryGetValue(flight, out FlightState? state) || !state.Published)
            {
                return 0;
            }

            int total = 0;
            List<InjectionEntry> pending = state.Pending;
            for (int i = 0; i < pending.Count; i++)
            {
                total += pending[i].Count;
            }

            return total;
        }

        public void Tick(in TickContext ctx)
        {
            ulong t = ctx.Tick;
            if (t % SimConstants.TICKS_PER_SIM_DAY == 0UL)
            {
                MaterializeDay((uint)((t / SimConstants.TICKS_PER_SIM_DAY) + 1UL));
            }

            if (_publishQueue.TryGetValue(t, out List<FlightId>? due))
            {
                for (int i = 0; i < due.Count; i++)
                {
                    FlightId id = due[i];
                    FlightState state = _byId[id];
                    state.Published = true;
                    DecodeDayOrdinal(id, out uint pubDay, out int pubOrdinal);
                    InsertSorted(_publishedOrdinalsByDay[pubDay], pubOrdinal);
                    _publishedTotalCount++;

                    FlightRecord r = state.Record;
                    var plan = new FlightPlanPublished(
                        r.Id,
                        r.Kind,
                        r.Rotation,
                        r.HasRotation,
                        r.Airline,
                        r.AircraftType,
                        SchedArrOf(r),
                        SchedDepOf(r),
                        r.MinTurnaround);
                    EventId planId = ctx.Events.Publish(in plan, EventRef.None);

                    var milestone = new FlightMilestoneReached(r.Id, FlightMilestone.PlanPublished, t, t);
                    ctx.Events.Publish(in milestone, new EventRef(planId, true));
                }

                _publishQueue.Remove(t);
            }

            if (_injectionQueue.TryGetValue(t, out List<InjectionEntry>? dueInjections))
            {
                for (int i = 0; i < dueInjections.Count; i++)
                {
                    InjectionEntry e = dueInjections[i];
                    FlightState state = _byId[e.Flight];
                    if (_flow != null)
                    {
                        bool hasBag = e.ClassIndex >= 2;
                        bool assist = (e.ClassIndex % 2) == 1;
                        var key = new CohortKey(e.Flight, FlowDirection.Departing, state.Record.PaxProfile, hasBag, assist);
                        _flow.Inject(in key, e.Count, state.Record.EntryNode);
                    }

                    state.Pending.Remove(e);
                }

                _injectionQueue.Remove(t);
            }
        }

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            h.Feed(_table.FixtureHash);
            h.Feed((ulong)_highestDay);
            h.Feed((ulong)_publishedTotalCount);

            for (uint d = 0; d <= _highestDay; d++)
            {
                if (!_publishedOrdinalsByDay.TryGetValue(d, out List<int>? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Count; i++)
                {
                    FlightId id = new FlightId(((ulong)d * FlightIdDayStride) + (ulong)ordinals[i] + 1UL);
                    FlightRecord r = _byId[id].Record;
                    h.Feed(r.Id.Value);
                    h.Feed(r.PublishTick);
                    h.Feed(r.ScheduledTick);
                }
            }

            ulong pendingTotal = 0;
            for (uint d = 0; d <= _highestDay; d++)
            {
                if (!_publishedOrdinalsByDay.TryGetValue(d, out List<int>? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Count; i++)
                {
                    FlightId id = new FlightId(((ulong)d * FlightIdDayStride) + (ulong)ordinals[i] + 1UL);
                    pendingTotal += (ulong)_byId[id].Pending.Count;
                }
            }

            h.Feed(pendingTotal);

            for (uint d = 0; d <= _highestDay; d++)
            {
                if (!_publishedOrdinalsByDay.TryGetValue(d, out List<int>? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Count; i++)
                {
                    FlightId id = new FlightId(((ulong)d * FlightIdDayStride) + (ulong)ordinals[i] + 1UL);
                    List<InjectionEntry> pending = _byId[id].Pending;
                    for (int j = 0; j < pending.Count; j++)
                    {
                        InjectionEntry e = pending[j];
                        h.Feed(e.DueTick);
                        h.Feed((long)e.Count);
                        h.Feed((long)e.ClassIndex);
                    }
                }
            }

            return h.Result;
        }

        private ulong SchedArrOf(in FlightRecord r)
        {
            if (r.Kind == MovementKind.Arrival)
            {
                return r.ScheduledTick;
            }

            return r.HasRotation ? _byId[r.Rotation].Record.ScheduledTick : TickUnscheduled;
        }

        private ulong SchedDepOf(in FlightRecord r)
        {
            if (r.Kind == MovementKind.Departure)
            {
                return r.ScheduledTick;
            }

            return r.HasRotation ? _byId[r.Rotation].Record.ScheduledTick : TickUnscheduled;
        }

        private void MaterializeDay(uint day)
        {
            IReadOnlyList<FlightTemplate> rows = _table.Rows;
            int occurCount = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                FlightTemplate row = rows[i];
                if (!Occurs(row, day))
                {
                    continue;
                }

                occurCount++;

                ulong id = ((ulong)day * FlightIdDayStride) + (ulong)row.RowOrdinal + 1UL;
                var flightId = new FlightId(id);
                ulong scheduledTick = ((ulong)day * SimConstants.TICKS_PER_SIM_DAY) + ((ulong)row.MinuteOfDay * SimConstants.TICKS_PER_SIM_MINUTE);
                ulong publishTick = scheduledTick >= PlanPublishLeadTicks ? scheduledTick - PlanPublishLeadTicks : 0UL;

                FlightId rotation;
                bool hasRotation;
                if (row.HasRotation)
                {
                    ulong otherId = ((ulong)day * FlightIdDayStride) + (ulong)row.RotationRowOrdinal + 1UL;
                    rotation = new FlightId(otherId);
                    hasRotation = true;
                }
                else
                {
                    rotation = flightId;
                    hasRotation = false;
                }

                var record = new FlightRecord(
                    flightId,
                    row.Airline,
                    row.AircraftType,
                    row.Kind,
                    day,
                    scheduledTick,
                    publishTick,
                    rotation,
                    hasRotation,
                    row.MinTurnaround,
                    row.PaxProfile,
                    row.PaxCount,
                    row.HoldBagPermille,
                    row.AssistPermille,
                    row.EntryNode);

                var pending = new List<InjectionEntry>();
                if (row.Kind == MovementKind.Departure && row.PaxCount > 0)
                {
                    ComputeInjections(flightId, scheduledTick, row.PaxCount, row.HoldBagPermille, row.AssistPermille, _curves[i], pending);
                }

                var state = new FlightState(record, pending);
                _byId.Add(flightId, state);

                AddToBucket(_publishQueue, publishTick, flightId);
                for (int p = 0; p < pending.Count; p++)
                {
                    AddToBucket(_injectionQueue, pending[p].DueTick, pending[p]);
                }

                List<(ulong Tick, FlightId Id)> movementIndex = row.Kind == MovementKind.Arrival ? _arrivalIndex : _departureIndex;
                movementIndex.Add((scheduledTick, flightId));
            }

            _departureIndex.Sort(TickThenIdComparer);
            _arrivalIndex.Sort(TickThenIdComparer);

            _publishedOrdinalsByDay[day] = new List<int>(occurCount);

            _highestDay = day;
        }

        private static void DecodeDayOrdinal(FlightId id, out uint day, out int ordinal)
        {
            ulong zeroBased = id.Value - 1UL;
            day = (uint)(zeroBased / FlightIdDayStride);
            ordinal = (int)(zeroBased % FlightIdDayStride);
        }

        private static void InsertSorted(List<int> list, int value)
        {
            int lo = 0;
            int hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid] < value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            list.Insert(lo, value);
        }

        private static bool Occurs(in FlightTemplate row, uint day)
        {
            return row.RepeatDaily ? day >= row.FirstDay : day == row.FirstDay;
        }

        private static void AddToBucket<T>(Dictionary<ulong, List<T>> dict, ulong key, T value)
        {
            if (!dict.TryGetValue(key, out List<T>? list))
            {
                list = new List<T>();
                dict.Add(key, list);
            }

            list.Add(value);
        }

        private static int LowerBound(List<(ulong Tick, FlightId Id)> list, ulong tick)
        {
            int lo = 0;
            int hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].Tick < tick)
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

        private static void ComputeInjections(
            FlightId flight,
            ulong scheduledTick,
            int paxCount,
            int holdPermille,
            int assistPermille,
            IReadOnlyList<ShowUpBucket> curve,
            List<InjectionEntry> output)
        {
            int n = curve.Count;
            var bucketCounts = new int[n];
            var bucketRemainders = new long[n];
            long assigned = 0;
            for (int i = 0; i < n; i++)
            {
                long exact = (long)paxCount * curve[i].SharePermille;
                bucketCounts[i] = (int)(exact / 1000L);
                bucketRemainders[i] = exact % 1000L;
                assigned += bucketCounts[i];
            }

            DistributeRemainder(bucketCounts, bucketRemainders, paxCount - assigned);

            long holdW = holdPermille;
            long assistW = assistPermille;
            var classWeights = new[]
            {
                (1000L - holdW) * (1000L - assistW),
                (1000L - holdW) * assistW,
                holdW * (1000L - assistW),
                holdW * assistW,
            };

            for (int b = 0; b < n; b++)
            {
                int bucketCount = bucketCounts[b];
                if (bucketCount == 0)
                {
                    continue;
                }

                ulong lead = (ulong)curve[b].MinutesBeforeStd * SimConstants.TICKS_PER_SIM_MINUTE;
                ulong due = scheduledTick >= lead ? scheduledTick - lead : 0UL;

                var classCounts = new int[4];
                var classRemainders = new long[4];
                long classAssigned = 0;
                for (int c = 0; c < 4; c++)
                {
                    long exact = (long)bucketCount * classWeights[c];
                    classCounts[c] = (int)(exact / 1000000L);
                    classRemainders[c] = exact % 1000000L;
                    classAssigned += classCounts[c];
                }

                DistributeRemainder(classCounts, classRemainders, bucketCount - classAssigned);

                for (int c = 0; c < 4; c++)
                {
                    if (classCounts[c] > 0)
                    {
                        output.Add(new InjectionEntry(flight, c, classCounts[c], due));
                    }
                }
            }
        }

        private static void DistributeRemainder(int[] counts, long[] remainders, long leftover)
        {
            var taken = new bool[counts.Length];
            for (long k = 0; k < leftover; k++)
            {
                int best = -1;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (!taken[i] && (best < 0 || remainders[i] > remainders[best]))
                    {
                        best = i;
                    }
                }

                taken[best] = true;
                counts[best]++;
            }
        }

        private static FormatException ResolutionFailure(string flightRef, string column, string id)
        {
            return new FormatException(
                "sim.schedule: flight_ref " + flightRef + ": " + column + " '" + id + "' does not resolve to a definition of the expected kind");
        }

        private sealed class FlightState
        {
            public FlightState(FlightRecord record, List<InjectionEntry> pending)
            {
                Record = record;
                Pending = pending;
            }

            public FlightRecord Record { get; }

            public bool Published { get; set; }

            public List<InjectionEntry> Pending { get; }
        }

        private sealed class InjectionEntry
        {
            public InjectionEntry(FlightId flight, int classIndex, int count, ulong dueTick)
            {
                Flight = flight;
                ClassIndex = classIndex;
                Count = count;
                DueTick = dueTick;
            }

            public FlightId Flight { get; }

            public int ClassIndex { get; }

            public int Count { get; }

            public ulong DueTick { get; }
        }
    }
}
