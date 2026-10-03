using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// sim.turnaround (13-interfaces-turnaround.md). Phase 2 <see cref="Tick"/>
    /// runs completions (§13.5 step 1) and vehicle assignment (step 3); the
    /// phase 3 handler for FlightMilestoneReached{OnStand} creates the flight's
    /// jobs (§13.5 step 2, §13.6). All state lives in arrays sized at
    /// construction; they grow, by doubling, only when more flights have been
    /// on stand than the capacity holds. Jobs are kept after completion so the
    /// queries can still answer for them.
    /// </summary>
    internal sealed class TurnaroundSystem : ITurnaroundSystem
    {
        // 13 §13.2.
        private const int JobKindBits = 8;

        // Jobs of flight slot s are _jobs[s * Stride + (int)kind]. JobKind has
        // eight values, so the stride is a power of two (checked at construction).
        private const int StrideBits = 3;
        private const int Stride = 1 << StrideBits;

        // 13 §13.6: CabinClean, Catering, Fuel, BaggageLoad and PushbackPrep.
        private const int PrerequisiteCount = 5;

        private const ulong Unscheduled = ulong.MaxValue;
        private const int InitialFlightCapacity = 2048;
        private const int InitialQueueCapacity = 64;

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
        private readonly IntQueue[] _blocked;

        // Per flight slot, in creation order.
        private ulong[] _fFlight;
        private bool[] _fDeparture;
        private int[] _fPrerequisitesDone;
        private ulong[] _fPlan1;
        private ulong[] _fPlan2;
        private int _flightCount;
        private int _flightCapacity;

        // Slots ordered by ascending flight value, for lookup and hashing.
        private int[] _order;

        private JobRec[] _jobs;
        private int[] _active;
        private int[] _due;
        private int _activeCount;

        public TurnaroundSystem(in SystemServices services, in TurnaroundSetup setup, IScheduleSystem schedule)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            TurnaroundSetupValidator.Validate(setup, "turnaround setup");
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
            _blocked = new IntQueue[_vehicleKindCount];
            for (int k = 0; k < _vehicleKindCount; k++)
            {
                _kindVehicles[k] = perKind[k].ToArray();
                _blocked[k] = new IntQueue(InitialQueueCapacity);
            }

            _flightCapacity = InitialFlightCapacity;
            _fFlight = new ulong[_flightCapacity];
            _fDeparture = new bool[_flightCapacity];
            _fPrerequisitesDone = new int[_flightCapacity];
            _fPlan1 = new ulong[_flightCapacity];
            _fPlan2 = new ulong[_flightCapacity];
            _order = new int[_flightCapacity];
            _jobs = new JobRec[_flightCapacity * Stride];
            _active = new int[_flightCapacity * Stride];
            _due = new int[_flightCapacity * Stride];

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
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.DeboardComplete, _fPlan1[slot], tick), done);
            }
            else if (kind == JobKind.Boarding)
            {
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.BoardingComplete, _fPlan2[slot], tick), done);
            }
            else if (kind >= JobKind.CabinClean && kind <= JobKind.PushbackPrep && ++_fPrerequisitesDone[slot] == PrerequisiteCount)
            {
                int b = (slot << StrideBits) + (int)JobKind.Boarding;
                JobDef def = _defs[(int)JobKind.Boarding];
                events.Publish(new TurnaroundJobUnblocked(flight, JobKind.Boarding, ResourceKind.JobDependency, null, def.Category), done);
                StartJob(b, tick, done, events);
                events.Publish(new FlightMilestoneReached(flight, FlightMilestone.ReadyToBoard, _fPlan1[slot], tick), done);
            }
        }

        // 13 §13.5 step 3. Each kind's blocked queue is in ascending EventId of
        // the Blocked request: Blocked events are published, and jobs queued,
        // in the same order, so the head is always the lowest.
        private void AssignVehicles(ulong tick, IEventPublisher events)
        {
            for (int k = 0; k < _vehicleKindCount; k++)
            {
                IntQueue queue = _blocked[k];
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
                        new TurnaroundJobUnblocked(new FlightId(_fFlight[j >> StrideBits]), kind, ResourceKind.Vehicle, new EntityId(_vId[v]), _defs[(int)kind].Category),
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

            if (!_schedule.TryGetFlight(evt.Flight, out FlightRecord flight))
            {
                throw new SimInvariantException("sim.turnaround: OnStand for a flight the schedule does not know", ctx.Tick);
            }

            ulong tick = ctx.Tick;
            var cause = new EventRef(env.Id, true);
            int slot = AddFlight(evt.Flight.Value);
            bool departure = flight.Kind == MovementKind.Departure;
            _fDeparture[slot] = departure;
            if (departure)
            {
                // 12 §12.3: the planned OnStand is STD minus MinTurnaround; 13 §13.6.
                long turn = Fx.Floor(flight.MinTurnaround * Fx.FromInt((long)SimConstants.TICKS_PER_SIM_MINUTE));
                ulong minTurn = turn > 0 ? (ulong)turn : 0UL;
                ulong plannedOnStand = flight.ScheduledTick > minTurn ? flight.ScheduledTick - minTurn : 0UL;
                _fPlan1[slot] = plannedOnStand + _maxPrerequisiteTicks;
                _fPlan2[slot] = _fPlan1[slot] + _defs[(int)JobKind.Boarding].NominalDurationTicks;
                for (JobKind k = JobKind.CabinClean; k <= JobKind.PushbackPrep; k++)
                {
                    CreateJob(slot, k, tick, cause, ctx.Events);
                }

                CreateJob(slot, JobKind.Boarding, tick, cause, ctx.Events);
            }
            else
            {
                _fPlan1[slot] = flight.ScheduledTick + _defs[(int)JobKind.Deboard].NominalDurationTicks;
                CreateJob(slot, JobKind.Deboard, tick, cause, ctx.Events);
                CreateJob(slot, JobKind.BaggageUnload, tick, cause, ctx.Events);
            }
        }

        // 13 §13.5 step 2 and §13.6. A vehicle job takes a free vehicle at
        // once; a queue that is not empty implies no vehicle of the kind is
        // free (step 3 ran this tick), so FIFO order is kept.
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
            int v = _blocked[vk].Count == 0 ? FirstFree(vk) : -1;
            if (v >= 0)
            {
                r.Vehicle = v;
                _vJob[v] = j;
                StartJob(j, tick, cause, events);
                return;
            }

            _blocked[vk].Enqueue(j);
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

        // The tick the job was planned to start: its creation, the flight's
        // OnStand. Boarding is planned from ReadyToBoard's planned tick. The
        // closing event carries the opening event's value (10 §10.6).
        private ulong PlannedStart(int j)
        {
            if ((JobKind)(j & (Stride - 1)) == JobKind.Boarding)
            {
                return _fPlan1[j >> StrideBits];
            }

            return _jobs[j].CreatedAt;
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

        private int FindSlot(ulong flight)
        {
            int lo = 0;
            int hi = _flightCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong at = _fFlight[_order[mid]];
                if (at == flight)
                {
                    return _order[mid];
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

            return -1;
        }

        private int AddFlight(ulong flight)
        {
            if (_flightCount == _flightCapacity)
            {
                Grow();
            }

            int slot = _flightCount;
            _fFlight[slot] = flight;
            _fPrerequisitesDone[slot] = 0;

            int lo = 0;
            int hi = _flightCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_fFlight[_order[mid]] < flight)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            Array.Copy(_order, lo, _order, lo + 1, _flightCount - lo);
            _order[lo] = slot;
            _flightCount++;
            return slot;
        }

        // The only allocation after construction: more flights have been on
        // stand than the arrays hold.
        private void Grow()
        {
            int capacity = _flightCapacity * 2;
            Array.Resize(ref _fFlight, capacity);
            Array.Resize(ref _fDeparture, capacity);
            Array.Resize(ref _fPrerequisitesDone, capacity);
            Array.Resize(ref _fPlan1, capacity);
            Array.Resize(ref _fPlan2, capacity);
            Array.Resize(ref _order, capacity);
            Array.Resize(ref _jobs, capacity * Stride);
            Array.Resize(ref _active, capacity * Stride);
            Array.Resize(ref _due, capacity * Stride);
            _flightCapacity = capacity;
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
