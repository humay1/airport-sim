using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.8 "Publication", 10 §10.7, 06 "Event contract".</summary>
    public sealed class DelayEventsTests
    {
        [Fact]
        public void test_delay_events_published_once_per_node_at_finalisation()
        {
            // Roots: 11 → 1, 12 → 2, 31 → 3.
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival, 12);
            s.Plan(0, 12, MovementKind.Departure, 11);
            s.Plan(0, 31, MovementKind.Arrival);

            // Arrival 11: runway leaf 100 (id 4) at Landed, residue 50 (id 5) at
            // OnStand, as in test_delay_late_inbound_capped_at_inbound_total.
            s.RunwayHeld(900, 11, 2, 3);
            s.RunwayReleased(1050, 11, 2);
            s.Milestone(1100, 11, FlightMilestone.Landed, 1000);
            Step arrOnStand = s.Milestone(1350, 11, FlightMilestone.OnStand, 1200);

            // Arrival 31 on time: a zero-delay flight publishes its root alone.
            s.Milestone(1360, 31, FlightMilestone.Landed, 1360);
            Step onTime = s.Milestone(1400, 31, FlightMilestone.OnStand, 1400);

            // Departure 12: OnStand 200 late → inbound 150 (id 6), residue 50
            // (id 7). Catering [1700, 1900); Pushback planned 1700, actual 2000:
            // delta 100, Catering owns 200, capped to 100 (id 8). Airborne
            // planned 1780, actual 2080: delta 0. Total 300.
            s.Milestone(1600, 12, FlightMilestone.OnStand, 1400);
            s.JobBlocked(1700, 12, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobUnblocked(1900, 12, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(2000, 12, FlightMilestone.Pushback, 1700);
            Step airborne = s.Milestone(2080, 12, FlightMilestone.Airborne, 1780);

            // After finalisation: a missed-passenger count and an ignored
            // interval change no tree and publish nothing (§14.5, §14.9).
            s.Missed(2100, 12, 3, 9);
            s.TaxiHeld(2110, 11, 4, null);
            s.TaxiReleased(2120, 11, 4);
            var rig = new DelayRig(s);

            // A live tree is never published.
            rig.RunTo(1350);
            Assert.Empty(rig.R.Delays);
            rig.RunTo(2080);
            Assert.Equal(4, rig.R.Delays.Count);
            rig.RunThrough(2200);

            var expected = new List<(ulong Tick, EventId Cause, ulong Node)>
            {
                (1350, arrOnStand.Id, 1), (1350, arrOnStand.Id, 4), (1350, arrOnStand.Id, 5),
                (1400, onTime.Id, 3),
                (2080, airborne.Id, 2), (2080, airborne.Id, 6), (2080, airborne.Id, 7), (2080, airborne.Id, 8),
            };
            var want = new List<string>();
            foreach ((ulong tick, EventId cause, ulong node) in expected)
            {
                want.Add(DelayModel.PublishedText(tick, cause, rig.Node(new DelayEventId(node))));
            }

            var got = new List<string>();
            foreach ((EventEnvelope env, DelayEvent evt) in rig.R.Delays)
            {
                Assert.Equal(DConst.DelayPos, env.Source.Value);
                Assert.True(env.Cause.HasValue, "DelayEvent without a Cause");
                got.Add(DelayModel.PublishedText(env.Tick, env.Cause.Id, evt.Node));
            }

            Assert.Equal(want, got);

            // The published nodes are the final trees, field for field.
            Assert.Equal(0UL, rig.Node(new DelayEventId(3)).Ticks);
            Assert.Equal(300UL, rig.Node(new DelayEventId(2)).Ticks);
            Assert.Equal(3, rig.Record(12).MissedPassengers);
        }
    }
}
