using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.3 "PlannedTick per milestone" (schedule-anchored and
    /// cumulative, 10 §10.4) and "Which FlightId gets which milestone", on
    /// one unimpeded rotation run through the §12.8 fallback. The fixed door
    /// delay has no pinned value (open question), so DoorsOpen is checked
    /// against OnStand rather than against a literal.
    /// </summary>
    public sealed class MilestoneTests
    {
        private static readonly FlightMilestone[] ArrivalOrder =
        {
            FlightMilestone.InboundAirborne, FlightMilestone.Landed, FlightMilestone.OffRunway, FlightMilestone.OnStand, FlightMilestone.DoorsOpen,
        };

        private static readonly FlightMilestone[] DepartureOrder =
        {
            FlightMilestone.OnStand, FlightMilestone.DoorsClosed, FlightMilestone.Pushback, FlightMilestone.TakeoffRoll, FlightMilestone.Airborne,
        };

        [Fact]
        public void test_milestone_planned_ticks_follow_schedule_anchored_table()
        {
            var rig = new HostRig(Csv.Of(Csv.Pair("A1", "D1", "06:00", "08:00")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(3UL * AirConst.TicksPerHour * 4UL);

            ulong sta = AirConst.At(6, 0);
            ulong std = AirConst.At(8, 0);
            ulong occ = FixtureLayout.OccupancyTicks;
            ulong route = FixtureLayout.RouteTicks(FixtureLayout.S1);
            ulong minTurn = 35UL * AirConst.TicksPerMinute;

            AssertAt(rig, a1, FlightMilestone.InboundAirborne, sta - AirConst.CruiseLead, sta - AirConst.CruiseLead);
            AssertAt(rig, a1, FlightMilestone.Landed, sta, sta);
            AssertAt(rig, a1, FlightMilestone.OffRunway, sta + occ, sta + occ);
            AssertAt(rig, a1, FlightMilestone.OnStand, sta + occ + route, sta + occ + route);

            Rec doorsOpen = rig.Rec.Milestone(a1, FlightMilestone.DoorsOpen);
            ulong doorDelay = doorsOpen.Milestone.PlannedTick - (sta + occ + route);
            Assert.True(doorsOpen.Milestone.PlannedTick >= sta + occ + route);
            Assert.Equal(doorsOpen.Milestone.PlannedTick, doorsOpen.Milestone.ActualTick);
            ulong open = doorsOpen.Milestone.ActualTick;
            Assert.Equal(sta + occ + route + doorDelay, open);

            // Fallback: the departure's ground block is DoorsOpen + MinTurnaround (12 §12.8).
            ulong close = open + minTurn;
            AssertAt(rig, d1, FlightMilestone.OnStand, std - minTurn, close);
            AssertAt(rig, d1, FlightMilestone.DoorsClosed, std, close);
            AssertAt(rig, d1, FlightMilestone.Pushback, std, close);
            AssertAt(rig, d1, FlightMilestone.TakeoffRoll, std + route, close + route);
            AssertAt(rig, d1, FlightMilestone.Airborne, std + route + occ, close + route + occ);
        }

        [Fact]
        public void test_milestone_split_across_arrival_and_departure_flight_ids_in_order()
        {
            var rig = new HostRig(Csv.Of(Csv.Pair("A1", "D1", "06:00", "08:00")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(AirConst.TicksPerDay);

            AssertSequence(rig.Rec.Milestones(a1), ArrivalOrder);
            AssertSequence(rig.Rec.Milestones(d1), DepartureOrder);

            // sim.turnaround's milestones are never sim.airside's (12 §12.3, 10 §10.3).
            foreach (Rec r in rig.Rec.All)
            {
                if (r.Payload is FlightMilestoneReached m)
                {
                    Assert.True(
                        m.Milestone != FlightMilestone.DeboardComplete && m.Milestone != FlightMilestone.ReadyToBoard && m.Milestone != FlightMilestone.BoardingComplete,
                        "emitted a sim.turnaround milestone: " + r);
                    if (m.Milestone == FlightMilestone.PlanPublished)
                    {
                        Assert.Equal(AirConst.ScheduleSystemId, r.Env.Source.Value);
                    }
                    else
                    {
                        Assert.Equal(AirConst.AirsideSystemId, r.Env.Source.Value);
                    }
                }
            }
        }

        private static void AssertAt(HostRig rig, ulong flight, FlightMilestone m, ulong planned, ulong actual)
        {
            Rec r = rig.Rec.Milestone(flight, m);
            Assert.True(planned == r.Milestone.PlannedTick, m + " planned: expected " + planned + ", got " + r.Milestone.PlannedTick);
            Assert.True(actual == r.Milestone.ActualTick, m + " actual: expected " + actual + ", got " + r.Milestone.ActualTick);
            Assert.Equal(actual, r.Tick);
            Assert.Equal(flight, r.Milestone.Flight.Value);
        }

        private static void AssertSequence(List<Rec> recs, FlightMilestone[] expected)
        {
            var got = recs.ConvertAll(r => r.Milestone.Milestone);
            Assert.Equal(expected, got.ToArray());
            for (int i = 1; i < recs.Count; i++)
            {
                Assert.True(recs[i - 1].Id.CompareTo(recs[i].Id) < 0);
                Assert.True(recs[i - 1].Milestone.ActualTick <= recs[i].Milestone.ActualTick);
            }
        }
    }
}
