using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Delay.Tests.Expect;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.5 "Keys" (Q-107): the Stand family's key is the flight alone.</summary>
    public sealed class DelayStandTests
    {
        [Fact]
        public void test_delay_stand_interval_pairs_null_stand_with_assigned_stand()
        {
            // As sim.airside emits them (12 §12.7): StandUnavailable with Stand
            // null, closed by StandAssigned with the granted stand 4. Arrival 11:
            // Landed on time at 900; wait [950, 1050); OnStand planned 1000,
            // actual 1150: delta 150 over [900, 1150): the wait owns 100, residue
            // 50. A = 0 (null stand on the opener), B = occupying from the opener.
            // Arrival 21: opened with stand 7 and closed with Stand null; A comes
            // from the opener only: 7 + 1 = 8. Landed 1900 on time, wait
            // [1950, 2000), OnStand planned 2000, actual 2050: delta 50 = the wait.
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival);
            s.Plan(0, 21, MovementKind.Arrival);
            s.Milestone(900, 11, FlightMilestone.Landed, 900);
            Step wait = s.StandUnavailable(950, 11, null, 55);
            s.StandAssigned(1050, 11, 4);
            s.Milestone(1150, 11, FlightMilestone.OnStand, 1000);
            s.Milestone(1900, 21, FlightMilestone.Landed, 1900);
            Step wait21 = s.StandUnavailable(1950, 21, 7, 66);
            s.StandAssigned(2000, 21, null);
            s.Milestone(2050, 21, FlightMilestone.OnStand, 2000);
            var rig = new DelayRig(s);
            rig.RunThrough(2050);

            Assert.Equal(
                new List<string>
                {
                    Leaf(3, 11, 1, DelayCategory.StandUnavailable, 100, true, 0, wait.Ref, DelaySource.StandUnavailable, 0, 55, 1150),
                    Unexplained(4, 11, 1, 50, 1150),
                },
                LeafTexts(rig, 11));
            Assert.Equal(
                new List<string> { Leaf(5, 21, 2, DelayCategory.StandUnavailable, 50, true, 0, wait21.Ref, DelaySource.StandUnavailable, 8, 66, 2050) },
                LeafTexts(rig, 21));
        }

        [Fact]
        public void test_delay_stand_second_unavailable_while_open_throws_with_tick()
        {
            // One open Stand interval per flight, whatever the Stand fields hold.
            var s = new Script();
            s.Plan(0, 11, MovementKind.Arrival);
            s.Milestone(900, 11, FlightMilestone.Landed, 900);
            s.StandUnavailable(950, 11, null, null);
            s.StandUnavailable(1000, 11, 4, 66);
            new DelayRig(s).AssertThrowsAt(1000, "second StandUnavailable while one is open");
        }
    }
}
