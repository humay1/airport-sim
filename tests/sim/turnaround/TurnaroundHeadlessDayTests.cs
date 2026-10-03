using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 07 "Testing": one headless sim-day of tests/fixtures/schedule/phase0-200.csv
    /// with the 13 §13.11 setup (five vehicles) and sim.airside registered on
    /// tests/fixtures/airside/phase1-single-runway.json (13 §13.11, Q-087),
    /// asserting 13's own rules at every sim-hour and over the whole event
    /// stream.
    /// </summary>
    public sealed class TurnaroundHeadlessDayTests
    {
        /// <summary>13 §13.3-§13.7 on the module's current state, for every flight the driver has put on stand.</summary>
        private static void CheckState(AirsideRig rig, JobDef[] jobs, VehicleDef[] fleet)
        {
            ulong now = rig.Host.CurrentTick;
            string at = " at tick " + now.ToString(CultureInfo.InvariantCulture);
            var holder = new Dictionary<ushort, JobId>();

            foreach (Movement m in rig.Moves.Values)
            {
                bool onStand = rig.Rec.OnStands.TryGetValue(m.Flight, out (EventId Id, ulong Tick) os);
                ulong onStandTick = os.Tick;
                IReadOnlyList<JobId> ids = rig.Turnaround.JobsForFlight(new FlightId(m.Flight));
                if (!onStand)
                {
                    Assert.True(ids.Count == 0, "jobs for flight " + m.Flight + " before its OnStand" + at);
                    continue;
                }

                // 13 §13.6/§13.7: the flight's kinds, ascending, with derived ids.
                JobKind[] kinds = m.Departure ? TConst.DepartureJobs : TConst.ArrivalJobs;
                Assert.True(ids.Count == kinds.Length, "flight " + m.Flight + " has " + ids.Count + " jobs" + at);
                bool prerequisitesDone = true;
                for (int i = 0; i < kinds.Length; i++)
                {
                    Assert.Equal(TConst.Job(m.Flight, kinds[i]), ids[i]);
                    Assert.True(rig.Turnaround.TryGetJob(ids[i], out TurnaroundJob j), "TryGetJob false for a listed job" + at);
                    string what = Show.Job(j) + at;
                    Assert.True(j.Id == ids[i] && j.Flight.Value == m.Flight && j.Kind == kinds[i], "job identity: " + what);
                    Assert.True(j.CreatedAt == onStandTick, "CreatedAt is not the OnStand tick: " + what);
                    VehicleKind? need = Setups.Requires(jobs, j.Kind);
                    ulong duration = Setups.Duration(jobs, j.Kind);
                    switch (j.Status)
                    {
                        case JobStatus.Blocked:
                            Assert.True(j.StartedAt == TConst.TickUnscheduled && j.DueAt == TConst.TickUnscheduled, "Blocked job is scheduled: " + what);
                            Assert.True(j.Vehicle == null, "Blocked job holds a vehicle: " + what);
                            Assert.True(j.Kind != JobKind.Deboard, "Deboard blocked: " + what);
                            break;
                        case JobStatus.Active:
                            Assert.True(j.StartedAt >= j.CreatedAt && j.StartedAt <= now, "StartedAt: " + what);
                            Assert.True(j.DueAt == j.StartedAt + duration, "DueAt != StartedAt + NominalDurationTicks: " + what);
                            Assert.True(j.DueAt >= now, "Active past its DueAt: " + what);
                            if (need.HasValue)
                            {
                                Assert.True(j.Vehicle.HasValue, "Active vehicle job without a vehicle: " + what);
                                Assert.True(!holder.ContainsKey(j.Vehicle!.Value.Value), "vehicle " + j.Vehicle.Value.Value + " on two Active jobs" + at);
                                holder[j.Vehicle.Value.Value] = j.Id;
                            }
                            else
                            {
                                Assert.True(j.Vehicle == null, "vehicle-less job holds a vehicle: " + what);
                            }

                            break;
                        case JobStatus.Completed:
                            Assert.True(j.DueAt == j.StartedAt + duration && j.DueAt < now, "Completed timing: " + what);
                            break;
                    }

                    if (Array.IndexOf(TConst.BoardingPrerequisites, j.Kind) >= 0 && j.Status != JobStatus.Completed)
                    {
                        prerequisitesDone = false;
                    }

                    if (j.Kind == JobKind.Boarding)
                    {
                        Assert.True(prerequisitesDone == (j.Status != JobStatus.Blocked), "Boarding " + j.Status + " with prerequisites done=" + prerequisitesDone + ": " + what);
                    }
                }
            }

            // Vehicles: kind from the fleet, Assignment matches the Active job
            // holding it, FreeVehicles is the unassigned ones, ascending.
            foreach (VehicleKind k in (VehicleKind[])Enum.GetValues(typeof(VehicleKind)))
            {
                var free = new List<ushort>();
                foreach (VehicleDef d in fleet)
                {
                    if (d.Kind != k)
                    {
                        continue;
                    }

                    Assert.True(rig.Turnaround.TryGetVehicle(d.Id, out VehicleState v), "TryGetVehicle false for a fleet vehicle" + at);
                    Assert.Equal(d.Kind, v.Kind);
                    if (holder.TryGetValue(d.Id.Value, out JobId held))
                    {
                        Assert.True(v.Assignment.HasValue && v.Assignment.Value == held, "vehicle " + d.Id.Value + " Assignment disagrees with its Active job" + at);
                        TurnaroundJob j;
                        rig.Turnaround.TryGetJob(held, out j);
                        Assert.True(Setups.Requires(jobs, j.Kind) == k, "vehicle of the wrong kind on " + Show.Job(j));
                    }
                    else
                    {
                        Assert.True(!v.Assignment.HasValue, "vehicle " + d.Id.Value + " assigned to no Active job" + at);
                        free.Add(d.Id.Value);
                    }
                }

                free.Sort();
                var listed = new List<ushort>();
                foreach (VehicleId id in rig.Turnaround.FreeVehicles(k))
                {
                    listed.Add(id.Value);
                }

                Assert.Equal(free, listed);
            }
        }

        [Fact]
        public void test_turnaround_headless_day_fixture_holds_spec_invariants()
        {
            JobDef[] jobs = Phase1Fixture.Jobs();
            VehicleDef[] fleet = Phase1Fixture.Fleet();
            var rig = new AirsideRig(ScheduleFixture.Bytes(), Setups.Of(jobs, fleet));
            for (ulong t = 600UL; t <= TConst.TicksPerDay; t += 600UL)
            {
                rig.RunTo(t);
                CheckState(rig, jobs, fleet);
            }

            Recorder rec = rig.Rec;

            // 13 §13.11: the five-vehicle fleet is contended over the day, and
            // the unimpeded path still completes rotations, which sim.airside
            // then closes and pushes back (12 §12.8 step 5).
            Assert.NotEmpty(rec.Of<TurnaroundJobBlocked>());
            Assert.NotEmpty(rec.Of<TurnaroundJobUnblocked>());
            Assert.NotEmpty(rec.Milestones(FlightMilestone.ReadyToBoard));
            Assert.NotEmpty(rec.Milestones(FlightMilestone.BoardingComplete));
            foreach (Rec b in rec.Milestones(FlightMilestone.BoardingComplete))
            {
                if (b.Tick + 1UL < TConst.TicksPerDay)
                {
                    rig.AirsideMilestone(b.Flight, FlightMilestone.DoorsClosed);
                }
            }

            // 10 §10.4: each milestone at most once per flight; 13 §13.6: an
            // arrival's DeboardComplete always comes (Deboard needs no vehicle).
            var once = new HashSet<(ulong, FlightMilestone)>();
            foreach (Rec r in rec.All)
            {
                if (r.FromTurnaround && r.Payload is FlightMilestoneReached m)
                {
                    Assert.True(once.Add((m.Flight.Value, m.Milestone)), "second " + m.Milestone + " for flight " + m.Flight.Value);
                    Assert.True(rig.Moves[m.Flight.Value].Departure == (m.Milestone != FlightMilestone.DeboardComplete), "milestone on the wrong FlightId: " + r);
                }
            }

            foreach (Movement m in rig.Moves.Values)
            {
                if (!m.Departure && rec.OnStands.TryGetValue(m.Flight, out (EventId Id, ulong Tick) os) && os.Tick + 60UL < TConst.TicksPerDay)
                {
                    Assert.True(once.Contains((m.Flight, FlightMilestone.DeboardComplete)), "no DeboardComplete for arrival " + m.Flight);
                }
            }

            // 10 §10.3 rule 2 and 13 §13.5: per job, the lifecycle is
            // [Blocked [Unblocked]] then [Started [Completed]], with Started
            // only after an Unblocked when the job blocked.
            var seq = new Dictionary<(ulong, JobKind), string>();
            foreach (Rec r in rec.All)
            {
                if (!r.FromTurnaround || r.Job == null)
                {
                    continue;
                }

                var key = (r.Flight, r.Job.Value);
                seq.TryGetValue(key, out string? s);
                string letter = r.Payload is TurnaroundJobBlocked ? "B" : r.Payload is TurnaroundJobUnblocked ? "U" : r.Payload is TurnaroundJobStarted ? "S" : "C";
                seq[key] = (s ?? string.Empty) + letter;
            }

            var legal = new HashSet<string>(StringComparer.Ordinal) { "S", "SC", "B", "BUS", "BUSC" };
            foreach (KeyValuePair<(ulong, JobKind), string> kv in seq)
            {
                Assert.True(legal.Contains(kv.Value), "job " + kv.Key.Item2 + " of flight " + kv.Key.Item1 + " went " + kv.Value);
                Assert.True(rig.Turnaround.TryGetJob(TConst.Job(kv.Key.Item1, kv.Key.Item2), out TurnaroundJob j), "job gone: " + kv.Key);
                JobStatus expected = kv.Value.EndsWith("C", StringComparison.Ordinal) ? JobStatus.Completed : kv.Value.EndsWith("S", StringComparison.Ordinal) ? JobStatus.Active : JobStatus.Blocked;
                Assert.True(j.Status == expected, "events " + kv.Value + " but state " + Show.Job(j));
            }
        }
    }
}
