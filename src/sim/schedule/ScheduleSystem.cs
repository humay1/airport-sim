using System;
using System.Collections.Generic;
using System.Globalization;
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

        // 11-interfaces-schedule.md §11.2 (Q-038): PLAN_PUBLISH_LEAD_TICKS / TICKS_PER_SIM_MINUTE.
        // The largest minutes_before_std a referenced pax profile may use; it is what makes
        // "no injection is ever due before its flight's publication" (§11.6) true by
        // construction, so it is asserted once, at construction, rather than guarded per tick.
        private const uint MaxShowUpMinutesBeforeStd = (uint)(SimConstants.TICKS_PER_SIM_DAY / SimConstants.TICKS_PER_SIM_MINUTE);

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

        // Every occurring row's ordinal for a materialised day, ascending, fixed once at
        // that day's materialisation: the loop below visits rows in ascending RowOrdinal
        // and Occurs() preserves order, so nothing here is ever sorted or shifted.
        // Concatenating days 0..highestDay ascending, each day's ordinals ascending, is
        // exactly ascending FlightId (§11.3, §11.7), because FlightIdDayStride exceeds any
        // day's occurring row count. Publication itself only flips FlightState.Published —
        // O(1), nothing inserted into a sorted container — and PublishedFlights/the hash
        // walk filter this array by that flag at query time (review findings 5 and 6).
        private readonly Dictionary<uint, int[]> _dayOrdinals = new Dictionary<uint, int[]>();

        // Per-day movement indices, each sorted once over only that day's rows at
        // materialisation (§11.9's "day boundary" allowance), never re-sorted afterwards.
        // A day's ScheduledTicks all fall in [day * TICKS_PER_SIM_DAY, (day+1) *
        // TICKS_PER_SIM_DAY), a range strictly below the next day's, so concatenating
        // days ascending preserves the required ascending (ScheduledTick, FlightId) order
        // without ever touching an earlier day's list again (review finding 5).
        private readonly Dictionary<uint, List<(ulong Tick, FlightId Id)>> _departureByDay = new Dictionary<uint, List<(ulong Tick, FlightId Id)>>();
        private readonly Dictionary<uint, List<(ulong Tick, FlightId Id)>> _arrivalByDay = new Dictionary<uint, List<(ulong Tick, FlightId Id)>>();

        private readonly Dictionary<ulong, List<FlightId>> _publishQueue = new Dictionary<ulong, List<FlightId>>();

        // Queued by due tick; each entry names the flight and the index of one of its
        // precomputed InjectionEntry values, never a shared object (review finding 7).
        private readonly Dictionary<ulong, List<(FlightId Flight, int EntryIndex)>> _injectionQueue =
            new Dictionary<ulong, List<(FlightId Flight, int EntryIndex)>>();

        private uint _highestDay;
        private int _publishedTotalCount;

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

                // §11.9a "Show-up bound" (Q-038): checked after both resolutions, in table
                // order, so a resolution miss anywhere earlier always wins over a bound
                // failure. Buckets are strictly ascending (§11.6), so the first one over the
                // bound is the smallest offending value.
                IReadOnlyList<ShowUpBucket> curve = profile.ShowUpCurve;
                for (int b = 0; b < curve.Count; b++)
                {
                    if (curve[b].MinutesBeforeStd > MaxShowUpMinutesBeforeStd)
                    {
                        throw ShowUpBoundFailure(row.FlightRef, row.PaxProfile.Value, curve[b].MinutesBeforeStd);
                    }
                }

                _curves[i] = curve;
            }

            // Day 0 materialises here, before any TickContext exists (§11.9a construction),
            // so no ISimClock is available yet; MaterializeDay falls back to the identical
            // pure formula in that case only (see ScheduledTickOf).
            MaterializeDay(0, null);
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
                if (!_dayOrdinals.TryGetValue(d, out int[]? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Length; i++)
                {
                    FlightId id = FlightIdOf(d, ordinals[i]);
                    if (_byId[id].Published)
                    {
                        result.Add(id);
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

            Dictionary<uint, List<(ulong Tick, FlightId Id)>> byDay = kind == MovementKind.Arrival ? _arrivalByDay : _departureByDay;
            uint dayFrom = (uint)(fromInclusive / SimConstants.TICKS_PER_SIM_DAY);
            ulong dayToInclusiveRaw = (toExclusive - 1UL) / SimConstants.TICKS_PER_SIM_DAY;
            uint dayToInclusive = (uint)Math.Min(dayToInclusiveRaw, (ulong)_highestDay);

            for (uint d = dayFrom; d <= dayToInclusive; d++)
            {
                if (!byDay.TryGetValue(d, out List<(ulong Tick, FlightId Id)>? dayList))
                {
                    continue;
                }

                int i = LowerBound(dayList, fromInclusive);
                while (i < dayList.Count && dayList[i].Tick < toExclusive)
                {
                    result.Add(dayList[i].Id);
                    i++;
                }
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
            InjectionEntry[] injections = state.Injections;
            bool[] drained = state.Drained;
            for (int i = 0; i < injections.Length; i++)
            {
                if (!drained[i])
                {
                    total += injections[i].Count;
                }
            }

            return total;
        }

        public void Tick(in TickContext ctx)
        {
            ulong t = ctx.Tick;
            if (t % SimConstants.TICKS_PER_SIM_DAY == 0UL)
            {
                MaterializeDay((uint)((t / SimConstants.TICKS_PER_SIM_DAY) + 1UL), ctx.Clock);
            }

            if (_publishQueue.TryGetValue(t, out List<FlightId>? due))
            {
                for (int i = 0; i < due.Count; i++)
                {
                    FlightId id = due[i];
                    FlightState state = _byId[id];
                    state.Published = true;
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

            if (_injectionQueue.TryGetValue(t, out List<(FlightId Flight, int EntryIndex)>? dueInjections))
            {
                for (int i = 0; i < dueInjections.Count; i++)
                {
                    (FlightId flight, int entryIndex) = dueInjections[i];
                    FlightState state = _byId[flight];
                    InjectionEntry entry = state.Injections[entryIndex];
                    if (_flow != null)
                    {
                        bool hasBag = entry.ClassIndex >= 2;
                        bool assist = (entry.ClassIndex % 2) == 1;
                        var key = new CohortKey(flight, FlowDirection.Departing, state.Record.PaxProfile, hasBag, assist);
                        _flow.Inject(in key, entry.Count, state.Record.EntryNode);
                    }

                    state.Drained[entryIndex] = true;
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
                if (!_dayOrdinals.TryGetValue(d, out int[]? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Length; i++)
                {
                    FlightState state = _byId[FlightIdOf(d, ordinals[i])];
                    if (!state.Published)
                    {
                        continue;
                    }

                    FlightRecord r = state.Record;
                    h.Feed(r.Id.Value);
                    h.Feed(r.PublishTick);
                    h.Feed(r.ScheduledTick);
                }
            }

            ulong pendingTotal = 0;
            for (uint d = 0; d <= _highestDay; d++)
            {
                if (!_dayOrdinals.TryGetValue(d, out int[]? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Length; i++)
                {
                    FlightState state = _byId[FlightIdOf(d, ordinals[i])];
                    if (!state.Published)
                    {
                        continue;
                    }

                    bool[] drained = state.Drained;
                    for (int k = 0; k < drained.Length; k++)
                    {
                        if (!drained[k])
                        {
                            pendingTotal++;
                        }
                    }
                }
            }

            h.Feed(pendingTotal);

            for (uint d = 0; d <= _highestDay; d++)
            {
                if (!_dayOrdinals.TryGetValue(d, out int[]? ordinals))
                {
                    continue;
                }

                for (int i = 0; i < ordinals.Length; i++)
                {
                    FlightState state = _byId[FlightIdOf(d, ordinals[i])];
                    if (!state.Published)
                    {
                        continue;
                    }

                    InjectionEntry[] injections = state.Injections;
                    bool[] drained = state.Drained;
                    for (int k = 0; k < injections.Length; k++)
                    {
                        if (drained[k])
                        {
                            continue;
                        }

                        InjectionEntry e = injections[k];
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

        private static FlightId FlightIdOf(uint day, int ordinal)
        {
            return new FlightId(((ulong)day * FlightIdDayStride) + (ulong)ordinal + 1UL);
        }

        private void MaterializeDay(uint day, ISimClock? clock)
        {
            IReadOnlyList<FlightTemplate> rows = _table.Rows;
            var ordinals = new List<int>();
            var departuresThisDay = new List<(ulong Tick, FlightId Id)>();
            var arrivalsThisDay = new List<(ulong Tick, FlightId Id)>();

            for (int i = 0; i < rows.Count; i++)
            {
                FlightTemplate row = rows[i];
                if (!Occurs(row, day))
                {
                    continue;
                }

                ordinals.Add(row.RowOrdinal);

                FlightId flightId = FlightIdOf(day, row.RowOrdinal);
                ulong scheduledTick = ScheduledTickOf(clock, day, row.MinuteOfDay);
                ulong publishTick = scheduledTick >= PlanPublishLeadTicks ? scheduledTick - PlanPublishLeadTicks : 0UL;

                FlightId rotation;
                bool hasRotation;
                if (row.HasRotation)
                {
                    rotation = FlightIdOf(day, row.RotationRowOrdinal);
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

                InjectionEntry[] injections = row.Kind == MovementKind.Departure && row.PaxCount > 0
                    ? ComputeInjections(scheduledTick, row.PaxCount, row.HoldBagPermille, row.AssistPermille, _curves[i])
                    : Array.Empty<InjectionEntry>();

                var state = new FlightState(record, injections);
                _byId.Add(flightId, state);

                AddToBucket(_publishQueue, publishTick, flightId);
                for (int e = 0; e < injections.Length; e++)
                {
                    AddToBucket(_injectionQueue, injections[e].DueTick, (flightId, e));
                }

                List<(ulong Tick, FlightId Id)> movementList = row.Kind == MovementKind.Arrival ? arrivalsThisDay : departuresThisDay;
                movementList.Add((scheduledTick, flightId));
            }

            departuresThisDay.Sort(TickThenIdComparer);
            arrivalsThisDay.Sort(TickThenIdComparer);
            _departureByDay[day] = departuresThisDay;
            _arrivalByDay[day] = arrivalsThisDay;

            _dayOrdinals[day] = ordinals.ToArray();
            _highestDay = day;
        }

        private static ulong ScheduledTickOf(ISimClock? clock, uint day, uint minuteOfDay)
        {
            // §11.4: "Converted via ISimClock.TickOfDayTime(day, (hh*60+mm)*60)". clock is
            // ctx.Clock for every day materialised inside Tick. Day 0 materialises during
            // construction (see the constructor's comment), before any TickContext exists,
            // so it falls back to the identical pure formula (08 §8.2's TickOfDayTime is
            // d * TICKS_PER_SIM_DAY + secondOfDay / SIM_SECONDS_PER_TICK, and ISimClock
            // itself holds no mutable state, so this is an availability difference only,
            // never a determinism one).
            uint secondOfDay = minuteOfDay * 60U;
            if (clock != null)
            {
                return clock.TickOfDayTime(day, secondOfDay);
            }

            return ((ulong)day * SimConstants.TICKS_PER_SIM_DAY) + (secondOfDay / (uint)SimConstants.SIM_SECONDS_PER_TICK);
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

        private static InjectionEntry[] ComputeInjections(
            ulong scheduledTick,
            int paxCount,
            int holdPermille,
            int assistPermille,
            IReadOnlyList<ShowUpBucket> curve)
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

            var output = new List<InjectionEntry>();
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
                        output.Add(new InjectionEntry(c, classCounts[c], due));
                    }
                }
            }

            return output.ToArray();
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

        private static FormatException ShowUpBoundFailure(string flightRef, string profileId, uint minutesBeforeStd)
        {
            return new FormatException(
                "sim.schedule: flight_ref " + flightRef + ": pax_profile '" + profileId + "' has a show-up bucket with minutes_before_std "
                + minutesBeforeStd.ToString(CultureInfo.InvariantCulture) + ", exceeding MAX_SHOW_UP_MINUTES_BEFORE_STD ("
                + MaxShowUpMinutesBeforeStd.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private sealed class FlightState
        {
            public FlightState(FlightRecord record, InjectionEntry[] injections)
            {
                Record = record;
                Injections = injections;
                Drained = new bool[injections.Length];
            }

            public FlightRecord Record { get; }

            public bool Published { get; set; }

            /// <summary>Fixed at materialisation, ascending (bucketIndex, classIndex); never resized.</summary>
            public InjectionEntry[] Injections { get; }

            /// <summary>Parallel to <see cref="Injections"/>; true once that entry has been drained.</summary>
            public bool[] Drained { get; }
        }

        /// <summary>
        /// One passenger-count entry, plain data (review finding 7): the queue that drains
        /// it names its owning flight and its index into that flight's <see cref="FlightState.Injections"/>
        /// array, never a reference to this value itself.
        /// </summary>
        private readonly struct InjectionEntry
        {
            public InjectionEntry(int classIndex, int count, ulong dueTick)
            {
                ClassIndex = classIndex;
                Count = count;
                DueTick = dueTick;
            }

            public int ClassIndex { get; }

            public int Count { get; }

            public ulong DueTick { get; }
        }
    }
}
