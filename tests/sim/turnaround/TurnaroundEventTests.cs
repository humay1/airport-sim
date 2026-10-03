using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.9 "The Cause of each emitted event" (Q-080), binding, under 10
    /// §10.2's rules, and the payloads of 10 §10.6 "From sim.turnaround".
    /// </summary>
    public sealed class TurnaroundEventTests
    {
        private static Rec Cause(Rig rig, Rec r)
        {
            Assert.True(r.Env.Cause.HasValue, "no Cause: " + r);
            return rig.Rec.ById(r.Env.Cause.Id);
        }

        private static bool IsCompletion(Rec r, ulong flight, JobKind kind)
        {
            return r.FromTurnaround && r.Payload is TurnaroundJobCompleted && r.Flight == flight && r.Job == kind;
        }

        /// <summary>The Started event's vehicle for the latest start of the job before <paramref name="before"/>.</summary>
        private static VehicleId? StartVehicle(Rig rig, ulong flight, JobKind kind, EventId before)
        {
            Rec? found = null;
            foreach (Rec r in rig.Rec.ForJob(flight, kind))
            {
                if (r.Payload is TurnaroundJobStarted && r.Id.CompareTo(before) < 0)
                {
                    found = r;
                }
            }

            Assert.True(found != null, "no TurnaroundJobStarted for " + kind + " of flight " + flight + " before " + Show.Id(before));
            return found!.Vehicle;
        }

        /// <summary>Checks every sim.turnaround event in the recording against 13 §13.9; returns how often each row was seen.</summary>
        internal static Dictionary<string, int> CheckCauses(Rig rig)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            void Saw(string row)
            {
                seen.TryGetValue(row, out int n);
                seen[row] = n + 1;
            }

            List<Rec> all = rig.Rec.All;
            for (int i = 0; i < all.Count; i++)
            {
                Rec r = all[i];
                if (!r.FromTurnaround)
                {
                    continue;
                }

                Rec cur = r;
                void Check(bool ok, string what)
                {
                    if (!ok)
                    {
                        Assert.Fail(what + ": " + cur + "\n--- trace ---\n" + rig.Rec.Dump());
                    }
                }

                // "sim.turnaround keeps no event id across ticks: every Cause above
                // is published earlier in the same tick."
                if (r.Env.Cause.HasValue)
                {
                    Check(r.Env.Cause.Id.Tick == r.Tick, "Cause from another tick");
                    Check(r.Env.Cause.Id.CompareTo(r.Id) < 0, "Cause not published earlier");
                }

                switch (r.Payload)
                {
                    case TurnaroundJobBlocked _:
                    {
                        // Every Blocked is emitted at creation; Cause = the flight's OnStand.
                        Rec cause = Cause(rig, r);
                        Check(cause.IsMilestone(FlightMilestone.OnStand) && cause.Flight == r.Flight && cause.Env.Source.Value == TConst.AirsideSystemId, "Blocked not caused by its flight's OnStand");
                        Assert.Equal(rig.Driver.OnStand(r.Flight), cause.Id);
                        Saw(r.Job == JobKind.Boarding ? "blocked-dependency" : "blocked-vehicle");
                        break;
                    }

                    case TurnaroundJobUnblocked _:
                    {
                        Rec cause = Cause(rig, r);
                        Check(cause.Payload is TurnaroundJobCompleted, "Unblocked not caused by a TurnaroundJobCompleted");
                        if (r.Job == JobKind.Boarding)
                        {
                            // The fifth prerequisite's completion: one of the
                            // flight's five, and no later completion among them.
                            Check(cause.Flight == r.Flight && Array.IndexOf(TConst.BoardingPrerequisites, cause.Job!.Value) >= 0, "Boarding Unblocked not caused by a prerequisite");
                            foreach (JobKind k in TConst.BoardingPrerequisites)
                            {
                                Rec? done = all.Find(x => IsCompletion(x, r.Flight, k));
                                Check(done != null && done.Id.CompareTo(cause.Id) <= 0, "Boarding unblocked before " + k + " completed");
                            }

                            Saw("unblocked-dependency");
                        }
                        else
                        {
                            // The vehicle-freeing completion: it freed the vehicle this job then starts with.
                            Rec? start = all.Find(x => x.FromTurnaround && x.Payload is TurnaroundJobStarted && x.Flight == r.Flight && x.Job == r.Job && x.Id.CompareTo(r.Id) > 0);
                            Check(start != null, "Unblocked never followed by Started");
                            VehicleId? freed = StartVehicle(rig, cause.Flight, cause.Job!.Value, cause.Id);
                            Check(freed.HasValue && start!.Vehicle.HasValue && freed.Value == start.Vehicle.Value, "Unblocked's Cause did not free the vehicle the job got");
                            Saw("unblocked-vehicle");
                        }

                        break;
                    }

                    case TurnaroundJobStarted _:
                    {
                        Rec? prev = null;
                        for (int j = i - 1; j >= 0; j--)
                        {
                            if (all[j].FromTurnaround && all[j].Flight == r.Flight && all[j].Job == r.Job)
                            {
                                prev = all[j];
                                break;
                            }
                        }

                        if (prev != null && prev.Payload is TurnaroundJobUnblocked)
                        {
                            // The Started that follows an Unblocked has the Unblocked's Cause.
                            Check(prev.Tick == r.Tick, "Started not in its Unblocked's tick");
                            Check(r.Env.Cause.HasValue && prev.Env.Cause.HasValue && r.Env.Cause.Id.Equals(prev.Env.Cause.Id), "Started's Cause differs from its Unblocked's");
                            Saw("started-after-unblock");
                        }
                        else
                        {
                            Check(prev == null, "Started after " + prev);
                            Rec cause = Cause(rig, r);
                            Check(cause.IsMilestone(FlightMilestone.OnStand) && cause.Flight == r.Flight, "Started at creation not caused by its OnStand");
                            Assert.Equal(rig.Driver.OnStand(r.Flight), cause.Id);
                            Saw(r.Job == JobKind.Deboard ? "started-deboard" : "started-at-creation");
                        }

                        break;
                    }

                    case TurnaroundJobCompleted _:
                        Check(!r.Env.Cause.HasValue, "Completed has a Cause");
                        Saw("completed");
                        break;

                    case FlightMilestoneReached m:
                    {
                        Rec cause = Cause(rig, r);
                        if (m.Milestone == FlightMilestone.DeboardComplete)
                        {
                            Check(IsCompletion(cause, r.Flight, JobKind.Deboard), "DeboardComplete not caused by Deboard's completion");
                            Saw("deboard-complete");
                        }
                        else if (m.Milestone == FlightMilestone.ReadyToBoard)
                        {
                            Rec? unblock = all.Find(x => x.FromTurnaround && x.Payload is TurnaroundJobUnblocked && x.Flight == r.Flight && x.Job == JobKind.Boarding);
                            Check(unblock != null && unblock.Env.Cause.Id.Equals(cause.Id), "ReadyToBoard's Cause is not the fifth prerequisite's completion");
                            Saw("ready-to-board");
                        }
                        else if (m.Milestone == FlightMilestone.BoardingComplete)
                        {
                            Check(IsCompletion(cause, r.Flight, JobKind.Boarding), "BoardingComplete not caused by Boarding's completion");
                            Saw("boarding-complete");
                        }
                        else
                        {
                            Check(false, "sim.turnaround emitted a milestone it does not own");
                        }

                        break;
                    }

                    default:
                        Check(false, "unexpected event");
                        break;
                }
            }

            return seen;
        }

        [Fact]
        public void test_turnaround_event_causes_follow_cause_table()
        {
            // A day of the 13 §13.11 setup: four vehicles against the Phase 0
            // schedule, so vehicle waits happen as well as the unimpeded path.
            var rig = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1);
            rig.RunTo(TConst.TicksPerDay);
            Dictionary<string, int> seen = CheckCauses(rig);

            // Every row of the table was exercised.
            foreach (string row in new[]
            {
                "blocked-vehicle", "blocked-dependency", "started-deboard", "started-at-creation", "unblocked-vehicle",
                "unblocked-dependency", "started-after-unblock", "completed", "deboard-complete", "ready-to-board", "boarding-complete",
            })
            {
                Assert.True(seen.ContainsKey(row), "the day never produced a '" + row + "' event");
            }
        }

        [Fact]
        public void test_turnaround_event_job_payloads_carry_catalogue_category_and_resource_kind()
        {
            // 13 §13.9, 10 §10.6 (Q-007): Blocked/Unblocked copy JobDef.Category;
            // a vehicle wait names ResourceKind.Vehicle, Boarding's JobDependency.
            JobDef[] jobs = Phase1Fixture.Jobs();
            var rig = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1);
            rig.RunTo(TConst.TicksPerDay);
            List<(Rec Rec, TurnaroundJobBlocked Evt)> blocked = rig.Rec.Of<TurnaroundJobBlocked>();
            List<(Rec Rec, TurnaroundJobUnblocked Evt)> unblocked = rig.Rec.Of<TurnaroundJobUnblocked>();
            Assert.NotEmpty(blocked);
            Assert.NotEmpty(unblocked);
            foreach ((Rec r, TurnaroundJobBlocked e) in blocked)
            {
                Assert.True(e.Category == Setups.Category(jobs, e.Job), "wrong category: " + r);
                Assert.True(e.WaitingOn == (e.Job == JobKind.Boarding ? ResourceKind.JobDependency : ResourceKind.Vehicle), "wrong WaitingOn: " + r);
                Assert.True(e.Job != JobKind.Deboard, "Deboard never blocks: " + r);
            }

            foreach ((Rec r, TurnaroundJobUnblocked e) in unblocked)
            {
                Assert.True(e.Category == Setups.Category(jobs, e.Job), "wrong category: " + r);
                Assert.True(e.WaitingOn == (e.Job == JobKind.Boarding ? ResourceKind.JobDependency : ResourceKind.Vehicle), "wrong WaitingOn: " + r);
            }

            foreach ((Rec r, TurnaroundJobStarted e) in rig.Rec.Of<TurnaroundJobStarted>())
            {
                VehicleKind? need = Setups.Requires(jobs, e.Job);
                if (need.HasValue)
                {
                    Assert.True(r.Vehicle.HasValue, "a vehicle job started without a vehicle: " + r);
                    Assert.True(rig.Turnaround.TryGetVehicle(r.Vehicle!.Value, out VehicleState v));
                    Assert.True(v.Kind == need.Value, "started with the wrong kind of vehicle: " + r);
                }
                else
                {
                    Assert.True(!r.Vehicle.HasValue, "a vehicle-less job got a vehicle: " + r);
                }
            }
        }
    }
}
