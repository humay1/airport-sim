using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// sim.turnaround (13-interfaces-turnaround.md). Phase 2 <see cref="Tick"/>
    /// prunes on a day's first tick (§13.10), then runs completions (§13.5
    /// step 1) and vehicle assignment (step 3); the phase 3 handler for
    /// FlightMilestoneReached{OnStand} creates the flight's jobs (step 2,
    /// §13.6). Every array is sized at construction to §13.10's capacities and
    /// never grows (Q-092).
    /// </summary>
    internal sealed class TurnaroundSystem : ITurnaroundSystem
    {
        // 13 §13.2.
        private const int JobKindBits = 8;
        private const int RetentionDays = 2;
        private const int FlightsCapacity = 4096;

        // Jobs of flight slot s are _jobs[s * Stride + (int)kind]. JobKind has
        // eight values, so the stride is a power of two (checked at construction).
        private const int StrideBits = 3;
        private const int Stride = 1 << StrideBits;

        // 13 §13.6: CabinClean, Catering, Fuel, BaggageLoad and PushbackPrep.
        private const int PrerequisiteCount = 5;

        private const ulong Unscheduled = ulong.MaxValue;

        private readonly SystemId _id = new SystemId(5);
        private readonly IScheduleSystem _schedule;
        private readonly JobDef[] _defs;
        private readonly ulong _maxPrerequisiteTicks;
        private readonly int _vehicleKindCount;

        // Vehicles, ascending VehicleId.
        private readonly ushort[] _vId;
        private readonly VehicleKind[] _vKind;
        private readonly int[] _vJob;
        private readonly EventRef[] _vFreedBy;
        private readonly int[][] _kindVehicles;

        // Per VehicleKind, the Blocked jobs in ascending EventId of their Blocked event.
        private readonly IntQueue[] _waiting;

        // Per flight slot.
        private readonly ulong[] _fFlight;
        private readonly int[] _fJobTotal;
        private readonly int[] _fDone;
        private readonly int[] _fPrerequisitesDone;
        private readonly ulong[] _fAnchor;
        private readonly ulong[] _fPlanReady;
        private readonly ulong[] _fPlanDone;
        private readonly int[] _freeSlots;
        private int _freeCount;

        // Slots in use, ordered by ascending flight value.
        private readonly int[] _order;
        private int _flightCount;

        // Finished flights' slots in finish order, a ring.
        private readonly int[] _finished;
        private int _finishedHead;
        private int _finishedCount;

        private readonly JobRec[] _jobs;
        private readonly int[] _active;
        private readonly int[] _due;
        private int _activeCount;

        public TurnaroundSystem(in SystemServices services, in TurnaroundSetup setup, IScheduleSystem schedule)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            string? failure = TurnaroundSetupValidator.Check(setup);
            if (failure != null)
            {
                throw new ArgumentException(failure, nameof(setup));
            }

            _schedule = schedule;

            int jobKinds = Enum.GetValues(typeof(JobKind)).Length;
            if (jobKinds > Stride)
            {
                throw new InvalidOperationException("JobKind has more values than the job table stride");
            }

            _defs = new JobDef[jobKinds];
            foreach (JobDef def in setup.Catalogue.Jobs)
            {
                _defs[(int)def.Kind] = def;
            }

            ulong maxPrep = 0UL;
            for (int k = (int)JobKind.CabinClean; k <= (int)JobKind.PushbackPrep; k++)
            {
                if (_defs[k].NominalDurationTicks > maxPrep)
                {
                    maxPrep = _defs[k].NominalDurationTicks;
                }
            }

            _maxPrerequisiteTicks = maxPrep;

            _vehicleKindCount = Enum.GetValues(typeof(VehicleKind)).Length;
            var fleet = new List<VehicleDef>(setup.Fleet.Vehicles);
            fleet.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
            int vehicles = fleet.Count;
            _vId = new ushort[vehicles];
            _vKind = new VehicleKind[vehicles];
            _vJob = new int[vehicles];
            _vFreedBy = new EventRef[vehicles];
            var perKind = new List<int>[_vehicleKindCount];
            for (int k = 0; k < _vehicleKindCount; k++)
            {
                perKind[k] = new List<int>();
            }

            for (int v = 0; v < vehicles; v++)
            {
                _vId[v] = fleet[v].Id.Value;
                _vKind[v] = fleet[v].Kind;
                _vJob[v] = -1;
                _vFreedBy[v] = EventRef.None;
                perKind[(int)fleet[v].Kind].Add(v);
            }

            _kindVehicles = new int[_vehicleKindCount][];
            _waiting = new IntQueue[_vehicleKindCount];
            for (int k = 0; k < _vehicleKindCount; k++)
            {
                _kindVehicles[k] = perKind[k].ToArray();
                _waiting[k] = new IntQueue(FlightsCapacity);
            }

            _fFlight = new ulong[FlightsCapacity];
            _fJobTotal = new int[FlightsCapacity];
            _fDone = new int[FlightsCapacity];
            _fPrerequisitesDone = new int[FlightsCapacity];
            _fAnchor = new ulong[FlightsCapacity];
            _fPlanReady = new ulong[FlightsCapacity];
            _fPlanDone = new ulong[FlightsCapacity];
            _freeSlots = new int[FlightsCapacity];
            for (int i = 0; i < FlightsCapacity; i++)
            {
                _freeSlots[i] = FlightsCapacity - 1 - i;
            }

            _freeCount = FlightsCapacity;
            _order = new int[FlightsCapacity];
            _finished = new int[FlightsCapacity];
            _jobs = new JobRec[FlightsCapacity * Stride];
            _active = new int[FlightsCapacity * Stride];
            _due = new int[FlightsCapacity * Stride];

            services.Events.Subscribe<FlightMilestoneReached>(_id, OnMilestone);
        }

        public SystemId Id => _id;

        public string Name => "sim.turnaround";

        public bool TryGetJob(JobId id, out TurnaroundJob job)
        {
            ulong kind = id.Value & ((1UL << JobKindBits) - 1UL);
            int slot = kind < (ulong)_defs.Length ? FindSlot(id.Value >> JobKindBits) : -1;
            if (slot < 0 || !_jobs[(slot << StrideBits) + (int)kind].Exists)
            {
                job = default;
                return false;
            }

            job = Snapshot((slot << StrideBits) + (int)kind);
            return true;
        }

        public IReadOnlyList<JobId> JobsForFlight(FlightId flight)
        {
            int slot = FindSlot(flight.Value);
            if (slot < 0)
            {
                return Array.Empty<JobId>();
            }

            int n = 0;
            for (int k = 0; k < _defs.Length; k++)
            {
                if (_jobs[(slot << StrideBits) + k].Exists)
                {
                    n++;
                }
            }

            var ids = new JobId[n];
            n = 0;
            for (int k = 0; k < _defs.Length; k++)
            {
                if (_jobs[(slot << StrideBits) + k].Exists)
                {
                    ids[n++] = JobIdOf((slot << StrideBits) + k);
                }
            }

            return ids;
        }

        public bool TryGetVehicle(VehicleId id, out VehicleState vehicle)
        {
            int lo = 0;
            int hi = _vId.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (_vId[mid] == id.Value)
                {
                    vehicle = new VehicleState(id, _vKind[mid], _vJob[mid] >= 0 ? JobIdOf(_vJob[mid]) : (JobId?)null);
                    return true;
                }

                if (_vId[mid] < id.Value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            vehicle = default;
            return false;
        }

        public IReadOnlyList<VehicleId> FreeVehicles(VehicleKind kind)
        {
            int k = (int)kind;
            if (k < 0 || k >= _vehicleKindCount)
            {
                return Array.Empty<VehicleId>();
            }

            int[] of = _kindVehicles[k];
            int n = 0;
            for (int i = 0; i < of.Length; i++)
            {
                if (_vJob[of[i]] < 0)
                {
                    n++;
                }
            }

            var free = new VehicleId[n];
            n = 0;
            for (int i = 0; i < of.Length; i++)
            {
                if (_vJob[of[i]] < 0)
                {
                    free[n++] = new VehicleId(_vId[of[i]]);
                }
            }

            return free;
        }

        public void Tick(in TickContext ctx)
        {
            Prune(ctx.Tick);
            CompleteDueJobs(ctx.Tick, ctx.Events);
            AssignVehicles(ctx.Tick, ctx.Events);
        }

        public ulong ComputeStateHash()
        {
            var h = new StateHasher();
            h.Feed((ulong)_vId.Length);
            for (int v = 0; v < _vId.Length; v++)
            {
                h.Feed((ulong)_vId[v]);
                h.Feed((ulong)(int)_vKind[v]);
                h.Feed(_vJob[v] >= 0);
                if (_vJob[v] >= 0)
                {
                    h.Feed(JobIdOf(_vJob[v]).Value);
                }
            }

            h.Feed((ulong)_flightCount);
            for (int o = 0; o < _flightCount; o++)
            {
                int slot = _order[o];
                for (int k = 0; k < _defs.Length; k++)
                {
                    int j = (slot << StrideBits) + k;
                    ref JobRec r = ref _jobs[j];
                    if (!r.Exists)
                    {
                        continue;
                    }

                    h.Feed(JobIdOf(j).Value);
                    h.Feed(_fFlight[slot]);
                    h.Feed((ulong)k);
                    h.Feed((ulong)(int)r.Status);
                    h.Feed(r.Vehicle >= 0);
                    if (r.Vehicle >= 0)
                    {
                        h.Feed((ulong)_vId[r.Vehicle]);
                    }

                    h.Feed(r.CreatedAt);
                    h.Feed(r.StartedAt);
                    h.Feed(r.DueAt);
                }
            }

            return h.Result;
        }

        // 13 §13.10 "Retention": on a day's first tick, before step 1, drop the
        // finished flights whose finish tick is before the previous sim-day.
        private void Prune(ulong tick)
        {
            if (tick % SimConstants.TICKS_PER_SIM_DAY != 0UL || _finishedCount == 0)
            {
                return;
            }

            ulong day = tick / SimConstants.TICKS_PER_SIM_DAY;
            if (day < (ulong)(RetentionDays - 1))
            {
                return;
            }

            ulong threshold = (day - (ulong)(RetentionDays - 1)) * SimConstants.TICKS_PER_SIM_DAY;
            while (_finishedCount > 0)
            {
                int slot = _finished[_finishedHead];
                if (FinishTick(slot) >= threshold)
                {
                    break;
                }

                _finishedHead = (_finishedHead + 1) % FlightsCapacity;
                _finishedCount--;
                RemoveFlight(slot);
            }
        }

        // The largest DueAt among the flight's jobs.
        private ulong FinishTick(int slot)
        {
            ulong latest = 0UL;
            for (int k = 0; k < _defs.Length; k++)
            {
                ref JobRec r = ref _jobs[(slot << StrideBits) + k];
                if (r.Exists && r.DueAt > latest)
                {
                    latest = r.DueAt;
                }
            }

            return latest;
        }

        // 13 §13.5 step 1. Completions in ascending JobId; a freed vehicle is
        // free for the assignment pass that follows in this same Tick.
        private void CompleteDueJobs(ulong tick, IEventPublisher events)
        {
            int due = 0;
            for (int i = 0; i < _activeCount; i++)
            {
                int j = _active[i];
                if (_jobs[j].DueAt == tick)
                {
                    _due[due++] = j;
                }
            }

            if (due == 0)
            {
                return;
            }

            for (int i = 1; i < due; i++)
            {
                int j = _due[i];
                ulong key = JobIdOf(j).Value;
                int p = i - 1;
                while (p >= 0 && JobIdOf(_due[p]).Value > key)
                {
                    _due[p + 1] = _due[p];
                    p--;
                }

                _due[p + 1] = j;
            }

            for (int i = 0; i < due; i++)
            {
                Complete(_due[i], tick, events);
            }

            int kept = 0;
            for (int i = 0; i < _activeCount; i++)
            {
                if (_jobs[_active[i]].Status == JobStatus.Active)
                {
                    _active[kept++] = _active[i];
                }
            }

            _activeCount = kept;
        }

        private void Complete(int j, ulong tick, IEventPublisher events)
        {
            int slot = j >> StrideBits;
            var kind = (JobKind)(j & (Stride - 1));
            var flight = new FlightId(_fFlight[slot]);
            ref JobRec r = ref _jobs[j];
            r.Status = JobStatus.Completed;

            EventId doneId = events.Publish(new TurnaroundJobCompleted(flight, kind, PlannedStart(j)), EventRef.None);
            var done = new EventRef(doneId, true);
            if (r.Vehicle >= 0)
            {
                _vJob[r.Vehicle] = -1;
                _vFreedBy[r.Vehicle] = done;
            }

            if (kind == JobKind.Deboard)
            {
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.DeboardComplete, _fPlanReady[slot], tick), done);
            }
            else if (kind == JobKind.Boarding)
            {
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.BoardingComplete, _fPlanDone[slot], tick), done);
            }
            else if (kind >= JobKind.CabinClean && kind <= JobKind.PushbackPrep && ++_fPrerequisitesDone[slot] == PrerequisiteCount)
            {
                int b = (slot << StrideBits) + (int)JobKind.Boarding;
                JobDef def = _defs[(int)JobKind.Boarding];
                events.Publish(new TurnaroundJobUnblocked(flight, JobKind.Boarding, ResourceKind.JobDependency, null, def.Category), done);
                StartJob(b, tick, done, events);
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.ReadyToBoard, _fPlanReady[slot], tick), done);
            }

            if (++_fDone[slot] == _fJobTotal[slot])
            {
                _finished[(_finishedHead + _finishedCount) % FlightsCapacity] = slot;
                _finishedCount++;
            }
        }

        // 13 §13.5 step 3. Each kind's waiting list is in ascending EventId of
        // the Blocked request: Blocked events are published, and jobs appended,
        // in the same order, so the head is always the lowest.
        private void AssignVehicles(ulong tick, IEventPublisher events)
        {
            for (int k = 0; k < _vehicleKindCount; k++)
            {
                IntQueue queue = _waiting[k];
                while (queue.Count > 0)
                {
                    int v = FirstFree(k);
                    if (v < 0)
                    {
                        break;
                    }

                    int j = queue.Dequeue();
                    _jobs[j].Vehicle = v;
                    _vJob[v] = j;
                    var kind = (JobKind)(j & (Stride - 1));
                    EventRef cause = _vFreedBy[v];
                    events.Publish(
                        new TurnaroundJobUnblocked(new FlightId(_fFlight[j >> StrideBits]), kind, ResourceKind.Vehicle, null, _defs[(int)kind].Category),
                        cause);
                    StartJob(j, tick, cause, events);
                }
            }
        }

        private void OnMilestone(in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx)
        {
            if (evt.Milestone != FlightMilestone.OnStand || FindSlot(evt.Flight.Value) >= 0)
            {
                return;
            }

            if (_flightCount == FlightsCapacity)
            {
                throw new SimInvariantException(
                    "sim.turnaround: no room for the jobs of flight " + evt.Flight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ctx.Tick);
            }

            if (!_schedule.TryGetFlight(evt.Flight, out FlightRecord flight))
            {
                throw new SimInvariantException("sim.turnaround: OnStand for a flight the schedule does not know", ctx.Tick);
            }

            ulong tick = ctx.Tick;
            var cause = new EventRef(env.Id, true);
            int slot = AddFlight(evt.Flight.Value);
            if (flight.Kind == MovementKind.Departure)
            {
                // 13 §13.6: the planned OnStand is the PlannedTick of the OnStand received.
                _fAnchor[slot] = evt.PlannedTick;
                _fPlanReady[slot] = evt.PlannedTick + _maxPrerequisiteTicks;
                _fPlanDone[slot] = _fPlanReady[slot] + _defs[(int)JobKind.Boarding].NominalDurationTicks;
                _fJobTotal[slot] = 6;
                for (JobKind k = JobKind.CabinClean; k <= JobKind.PushbackPrep; k++)
                {
                    CreateJob(slot, k, tick, cause, ctx.Events);
                }

                CreateJob(slot, JobKind.Boarding, tick, cause, ctx.Events);
            }
            else
            {
                _fAnchor[slot] = flight.ScheduledTick;
                _fPlanReady[slot] = flight.ScheduledTick + _defs[(int)JobKind.Deboard].NominalDurationTicks;
                _fJobTotal[slot] = 2;
                CreateJob(slot, JobKind.Deboard, tick, cause, ctx.Events);
                CreateJob(slot, JobKind.BaggageUnload, tick, cause, ctx.Events);
            }
        }

        // 13 §13.5 step 2 and §13.6. A vehicle job takes the lowest free
        // VehicleId of its kind at once; a non-empty waiting list implies no
        // vehicle of the kind is free (step 3 ran this tick).
        private void CreateJob(int slot, JobKind kind, ulong tick, EventRef cause, IEventPublisher events)
        {
            int j = (slot << StrideBits) + (int)kind;
            ref JobRec r = ref _jobs[j];
            r.Exists = true;
            r.Status = JobStatus.Blocked;
            r.Vehicle = -1;
            r.CreatedAt = tick;
            r.StartedAt = Unscheduled;
            r.DueAt = Unscheduled;

            var flight = new FlightId(_fFlight[slot]);
            JobDef def = _defs[(int)kind];
            if (kind == JobKind.Boarding)
            {
                events.Publish(new TurnaroundJobBlocked(flight, kind, ResourceKind.JobDependency, null, def.Category), cause);
                return;
            }

            if (!def.RequiresVehicle.HasValue)
            {
                StartJob(j, tick, cause, events);
                return;
            }

            int vk = (int)def.RequiresVehicle.Value;
            int v = _waiting[vk].Count == 0 ? FirstFree(vk) : -1;
            if (v >= 0)
            {
                r.Vehicle = v;
                _vJob[v] = j;
                StartJob(j, tick, cause, events);
                return;
            }

            if (!_waiting[vk].TryEnqueue(j))
            {
                throw new SimInvariantException(
                    "sim.turnaround: the waiting list is full at flight " + flight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    tick);
            }

            events.Publish(new TurnaroundJobBlocked(flight, kind, ResourceKind.Vehicle, null, def.Category), cause);
        }

        private void StartJob(int j, ulong tick, EventRef cause, IEventPublisher events)
        {
            ref JobRec r = ref _jobs[j];
            var kind = (JobKind)(j & (Stride - 1));
            r.Status = JobStatus.Active;
            r.StartedAt = tick;
            r.DueAt = tick + _defs[(int)kind].NominalDurationTicks;
            _active[_activeCount++] = j;
            events.Publish(new TurnaroundJobStarted(new FlightId(_fFlight[j >> StrideBits]), kind, PlannedStart(j)), cause);
        }

        // 13 §13.6 "Job event payloads" (Q-089): schedule-anchored, whatever
        // the actual start. Started and Completed carry the same value.
        private ulong PlannedStart(int j)
        {
            return (JobKind)(j & (Stride - 1)) == JobKind.Boarding ? _fPlanReady[j >> StrideBits] : _fAnchor[j >> StrideBits];
        }

        private int FirstFree(int vehicleKind)
        {
            int[] of = _kindVehicles[vehicleKind];
            for (int i = 0; i < of.Length; i++)
            {
                if (_vJob[of[i]] < 0)
                {
                    return of[i];
                }
            }

            return -1;
        }

        // The index into _order of the flight, or the insertion point as its complement.
        private int SearchOrder(ulong flight)
        {
            int lo = 0;
            int hi = _flightCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong at = _fFlight[_order[mid]];
                if (at == flight)
                {
                    return mid;
                }

                if (at < flight)
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

        private int FindSlot(ulong flight)
        {
            int at = SearchOrder(flight);
            return at >= 0 ? _order[at] : -1;
        }

        private int AddFlight(ulong flight)
        {
            int insert = ~SearchOrder(flight);
            int slot = _freeSlots[--_freeCount];
            _fFlight[slot] = flight;
            _fDone[slot] = 0;
            _fPrerequisitesDone[slot] = 0;
            Array.Copy(_order, insert, _order, insert + 1, _flightCount - insert);
            _order[insert] = slot;
            _flightCount++;
            return slot;
        }

        private void RemoveFlight(int slot)
        {
            int at = SearchOrder(_fFlight[slot]);
            Array.Copy(_order, at + 1, _order, at, _flightCount - at - 1);
            _flightCount--;
            for (int k = 0; k < Stride; k++)
            {
                _jobs[(slot << StrideBits) + k] = default;
            }

            _freeSlots[_freeCount++] = slot;
        }

        private JobId JobIdOf(int j)
        {
            return new JobId((_fFlight[j >> StrideBits] << JobKindBits) | (ulong)(uint)(j & (Stride - 1)));
        }

        private TurnaroundJob Snapshot(int j)
        {
            ref JobRec r = ref _jobs[j];
            return new TurnaroundJob(
                JobIdOf(j),
                new FlightId(_fFlight[j >> StrideBits]),
                (JobKind)(j & (Stride - 1)),
                r.Status,
                r.Vehicle >= 0 ? new VehicleId(_vId[r.Vehicle]) : (VehicleId?)null,
                r.CreatedAt,
                r.StartedAt,
                r.DueAt);
        }

        private struct JobRec
        {
            public bool Exists;
            public JobStatus Status;
            public int Vehicle;
            public ulong CreatedAt;
            public ulong StartedAt;
            public ulong DueAt;
        }
    }
}
