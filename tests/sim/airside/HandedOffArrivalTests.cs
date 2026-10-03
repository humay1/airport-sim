using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 "The handed-off arrival" (Q-062): at the handoff, on either
    /// path, the arrival leaves tracked state, as a departure does at
    /// Airborne. From that action on TryGetTrack(arrival) is false and it is
    /// not in TrackedFlights(), whether its departure is still on stand or
    /// has pushed back. Every arrival milestone has fired by then.
    /// </summary>
    public sealed class HandedOffArrivalTests
    {
        [Fact]
        public void test_handed_off_arrival_leaves_tracked_state_at_handoff()
        {
            // Fallback path. A 100-tick boarding hold keeps R_D on the stand
            // after the handoff, so "before Pushback" spans many ticks.
            var rig = new HostRig(Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00")), flow: ScriptedFlow.For(2UL, 100UL, 3, 77U));
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            Assert.Equal(2UL, rd);
            var tracked = new Dictionary<ulong, bool>();
            var listed = new Dictionary<ulong, bool>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                tracked[t] = rig.Airside.TryGetTrack(new FlightId(ra), out _);
                listed[t] = new List<FlightId>(rig.Airside.TrackedFlights()).Contains(new FlightId(ra));
            });

            ulong handoff = rig.Rec.Milestone(rd, FlightMilestone.OnStand).Tick;
            ulong pushback = rig.Rec.Milestone(rd, FlightMilestone.Pushback).Tick;
            Assert.True(pushback > handoff + 1UL, "fixture assumption: R_D stays on stand after the handoff");
            Assert.True(rig.Rec.Milestone(ra, FlightMilestone.DoorsOpen).Tick <= handoff);

            ulong airborne = rig.Rec.Milestone(ra, FlightMilestone.InboundAirborne).Tick;
            for (ulong t = airborne; t < handoff; t++)
            {
                Assert.True(tracked[t] && listed[t], "t=" + t.ToString(CultureInfo.InvariantCulture) + ": arrival untracked before its handoff");
            }

            for (ulong t = handoff; t < AirConst.TicksPerDay; t++)
            {
                Assert.False(tracked[t], "t=" + t.ToString(CultureInfo.InvariantCulture) + ": TryGetTrack(arrival) true after the handoff");
                Assert.False(listed[t], "t=" + t.ToString(CultureInfo.InvariantCulture) + ": arrival in TrackedFlights() after the handoff");
            }

            // Every arrival milestone fired, none after the handoff.
            Assert.All(rig.Rec.Milestones(ra), r => Assert.True(r.Tick <= handoff));
            Assert.Equal(5, rig.Rec.Milestones(ra).Count);
        }
    }
}
