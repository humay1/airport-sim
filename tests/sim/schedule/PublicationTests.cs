using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.5: at each tick, flights whose PublishTick is that tick are
    /// published in ascending FlightId, each as FlightPlanPublished then
    /// FlightMilestoneReached{PlanPublished} caused by it, once per flight.
    /// </summary>
    public sealed class PublicationTests
    {
        private static HostRig RunDays(int days, bool withFlow = false)
        {
            var rig = new HostRig(Fixture.Bytes(), withFlow: withFlow);
            rig.RunTo((ulong)days * SchedConst.TicksPerDay);
            return rig;
        }

        [Fact]
        public void test_publication_emits_plan_then_milestone_once_per_flight()
        {
            HostRig rig = RunDays(1);
            List<RecordedEvent> ev = rig.Events!.Events;
            var oracle = new ScheduleOracle(Fixture.Text(), 3);
            var expectedFlights = new HashSet<ulong>();
            foreach (OracleFlight f in oracle.PublishedAfter(SchedConst.TicksPerDay - 1UL))
            {
                expectedFlights.Add(f.Id);
            }

            Assert.Equal(400, expectedFlights.Count);
            Assert.Equal(2 * expectedFlights.Count, ev.Count);
            var seen = new HashSet<ulong>();
            for (int i = 0; i < ev.Count; i += 2)
            {
                RecordedEvent plan = ev[i];
                RecordedEvent ms = ev[i + 1];
                Assert.True(plan.IsPlan, "event " + i + " should be FlightPlanPublished: " + plan);
                Assert.False(ms.IsPlan, "event " + (i + 1) + " should be FlightMilestoneReached: " + ms);
                Assert.Equal(plan.Plan.Flight, ms.Milestone.Flight);
                Assert.True(seen.Add(plan.Plan.Flight.Value), "published twice: " + plan);
                Assert.Contains(plan.Plan.Flight.Value, expectedFlights);

                Assert.Equal(SchedConst.ScheduleSystemId, plan.Env.Source.Value);
                Assert.Equal(SchedConst.ScheduleSystemId, ms.Env.Source.Value);
                Assert.False(plan.Env.Cause.HasValue, "a plan is a root event: " + plan);
                Assert.True(ms.Env.Cause.HasValue, "milestone must name its cause: " + ms);
                Assert.Equal(plan.Env.Id, ms.Env.Cause.Id);
                Assert.Equal(plan.Env.Tick, ms.Env.Tick);

                FlightRecord r = rig.Flight(plan.Plan.Flight.Value);
                Assert.Equal(FlightMilestone.PlanPublished, ms.Milestone.Milestone);
                Assert.Equal(r.PublishTick, ms.Milestone.PlannedTick);
                Assert.Equal(r.PublishTick, ms.Milestone.ActualTick);
                Assert.Equal(r.PublishTick, plan.Env.Tick);
            }
        }

        [Fact]
        public void test_publication_orders_same_tick_flights_by_ascending_id()
        {
            HostRig rig = RunDays(2);
            List<RecordedEvent> ev = rig.Events!.Events;
            Assert.Equal(1200, ev.Count);
            for (int i = 2; i < ev.Count; i += 2)
            {
                RecordedEvent prev = ev[i - 2];
                RecordedEvent cur = ev[i];
                Assert.True(prev.Env.Tick <= cur.Env.Tick);
                if (prev.Env.Tick == cur.Env.Tick)
                {
                    Assert.True(prev.Flight < cur.Flight, "not ascending FlightId within tick: " + prev + " then " + cur);
                }
            }
        }

        [Fact]
        public void test_publication_tick_zero_publishes_every_day_zero_flight()
        {
            var rig = new HostRig(Fixture.Bytes());
            Assert.Empty(rig.Schedule.PublishedFlights());
            rig.RunTo(1);
            List<RecordedEvent> ev = rig.Events!.Events;
            Assert.Equal(400, ev.Count);
            for (int i = 0; i < 200; i++)
            {
                Assert.Equal((ulong)i + 1UL, ev[2 * i].Flight);
                Assert.Equal(0UL, ev[2 * i].Env.Tick);
                Assert.Equal(new EventId(0UL, (uint)(2 * i)), ev[2 * i].Env.Id);
                Assert.Equal(new EventId(0UL, (uint)((2 * i) + 1)), ev[(2 * i) + 1].Env.Id);
            }

            Assert.Equal(200, rig.Schedule.PublishedFlights().Count);
        }

        [Fact]
        public void test_publication_later_day_flight_publishes_at_scheduled_minus_lead()
        {
            HostRig rig = RunDays(2);
            var oracle = new ScheduleOracle(Fixture.Text(), 3);
            var tickOf = new Dictionary<ulong, ulong>();
            foreach (RecordedEvent e in rig.Events!.Events)
            {
                if (e.IsPlan)
                {
                    tickOf[e.Flight] = e.Env.Tick;
                }
            }

            int checkedCount = 0;
            foreach (OracleFlight f in oracle.Flights)
            {
                if (f.Day == 1 || f.Day == 2)
                {
                    Assert.Equal(f.Sched - SchedConst.PublishLead, tickOf[f.Id]);
                    checkedCount++;
                }
            }

            Assert.Equal(400, checkedCount);
            Assert.Equal(600, tickOf.Count);
        }

        [Fact]
        public void test_publication_plan_copies_record_and_rotation_fields()
        {
            HostRig rig = RunDays(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            Assert.Equal(800, rig.Events!.Events.Count);
            foreach (RecordedEvent e in rig.Events.Events)
            {
                if (!e.IsPlan)
                {
                    continue;
                }

                FlightPlanPublished p = e.Plan;
                FlightRecord r = rig.Flight(p.Flight.Value);
                OracleFlight f = oracle.ById(p.Flight.Value);
                Assert.Equal(r.Kind, p.Kind);
                Assert.Equal(r.Rotation, p.Rotation);
                Assert.Equal(r.HasRotation, p.HasRotation);
                Assert.Equal(r.Airline.Value, p.Airline.Value);
                Assert.Equal(r.AircraftType.Value, p.AircraftType.Value);
                Assert.Equal(r.MinTurnaround, p.MinTurnaround);
                Assert.Equal(f.SchedArr, p.SchedArr);
                Assert.Equal(f.SchedDep, p.SchedDep);
            }
        }

        [Fact]
        public void test_publication_lone_flights_use_tick_unscheduled_for_missing_side()
        {
            var rig = new HostRig(Fixture.Bytes());
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            ulong loneDep = oracle.ByRef("BRW100", 0).Id;
            ulong loneArr = oracle.ByRef("CTX199", 0).Id;
            int found = 0;
            foreach (RecordedEvent e in rig.Events!.Events)
            {
                if (e.IsPlan && e.Flight == loneDep)
                {
                    found++;
                    Assert.Equal(SchedConst.TickUnscheduled, e.Plan.SchedArr);
                    Assert.Equal(450UL, e.Plan.SchedDep);
                    Assert.False(e.Plan.HasRotation);
                    Assert.Equal(loneDep, e.Plan.Rotation.Value);
                }
                else if (e.IsPlan && e.Flight == loneArr)
                {
                    found++;
                    Assert.Equal(13900UL, e.Plan.SchedArr);
                    Assert.Equal(SchedConst.TickUnscheduled, e.Plan.SchedDep);
                    Assert.False(e.Plan.HasRotation);
                }
            }

            Assert.Equal(2, found);
        }

        [Fact]
        public void test_publication_never_reemits_a_flight_across_days()
        {
            HostRig rig = RunDays(3);
            var plans = new HashSet<ulong>();
            var milestones = new HashSet<ulong>();
            foreach (RecordedEvent e in rig.Events!.Events)
            {
                Assert.True((e.IsPlan ? plans : milestones).Add(e.Flight), "re-emitted: " + e);
            }

            Assert.Equal(800, plans.Count);
            Assert.True(plans.SetEquals(milestones));
        }

        [Fact]
        public void test_publication_published_flights_ascend_and_never_shrink()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            var previous = new List<ulong>();
            for (ulong t = 0; t < 2UL * SchedConst.TicksPerDay; t += 60UL)
            {
                rig.RunTo(t + 1UL);
                List<ulong> now = Describe.Ids(rig.Schedule.PublishedFlights());
                for (int i = 1; i < now.Count; i++)
                {
                    Assert.True(now[i - 1] < now[i], "PublishedFlights not ascending at tick " + t);
                }

                var set = new HashSet<ulong>(now);
                foreach (ulong id in previous)
                {
                    Assert.Contains(id, set);
                }

                previous = now;
            }

            Assert.Equal(600, previous.Count);
        }

        [Fact]
        public void test_publication_is_the_same_with_flow_registered()
        {
            HostRig a = RunDays(1);
            HostRig b = RunDays(1, withFlow: true);
            Assert.Equal(800, a.Events!.Events.Count);
            Assert.Equal(a.Events.Trace(), b.Events!.Trace());
        }
    }
}
