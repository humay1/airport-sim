using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 "The boarding hold (both paths)" on the path T-021 ships: the
    /// fallback's doors-close point (DoorsOpen + the arrival's MinTurnaround),
    /// Cause = the departure's OnStand. And "The stand stays occupied during
    /// the hold. An aircraft waiting for that stand gets an ordinary
    /// StandUnavailable interval".
    /// </summary>
    public sealed class BoardingHoldTests
    {
        [Fact]
        public void test_boarding_hold_applies_in_turnaround_fallback_path()
        {
            var flow = ScriptedFlow.For(DepartureTests.Rd, 40UL, 5, 77U);
            var rig = new HostRig(
                Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00", arrMinTurn: "35", depMinTurn: "45")),
                flow: flow,
                turnaroundRegistered: false);
            ulong rd = rig.Id("R_D");
            Assert.Equal(DepartureTests.Rd, rd);
            rig.RunTo(AirConst.TicksPerDay);

            ulong h = DepartureTests.DoorsClosePoint(rig);
            Rec onStand = rig.Rec.Milestone(rd, FlightMilestone.OnStand);
            Assert.Equal(h, onStand.Tick);
            Assert.Equal(h, flow.FirstQuery(rd));

            var held = rig.Rec.Of<DepartureHeldForPassengers>(rd);
            Assert.Single(held);
            Assert.Equal(h, held[0].Rec.Tick);
            Assert.True(held[0].Rec.Env.Cause.HasValue);
            Assert.Equal(onStand.Id, held[0].Rec.Env.Cause.Id);
            Assert.True(onStand.Id.CompareTo(held[0].Rec.Id) < 0);

            Assert.Equal(h + 40UL, rig.Rec.Milestone(rd, FlightMilestone.DoorsClosed).Milestone.ActualTick);
            Assert.Equal(h + 40UL, flow.AbsorbsOf(rd)[0].Tick);
        }

        [Fact]
        public void test_boarding_hold_keeps_stand_occupied_and_waiting_arrival_gets_stand_unavailable()
        {
            var flow = ScriptedFlow.For(DepartureTests.Rd, 200UL, 3, 77U);
            var rig = new HostRig(Csv.Of(StandTests.FullApron()), holdMinutes: 30U, flow: flow);
            ulong rd = rig.Id("R_D");
            ulong w = rig.Id("W");
            Assert.Equal(DepartureTests.Rd, rd);
            var occupant = new Dictionary<ulong, ulong?>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                FlightId? o = rig.Occupant(FixtureLayout.S1);
                occupant[t] = o.HasValue ? o.Value.Value : (ulong?)null;
            });

            ulong h = DepartureTests.DoorsClosePoint(rig);
            ulong p = rig.Rec.Milestone(rd, FlightMilestone.Pushback).Milestone.ActualTick;
            Assert.Equal(h + 200UL, p);
            for (ulong t = h; t < p; t++)
            {
                Assert.True(occupant[t] == rd, "t=" + t.ToString(CultureInfo.InvariantCulture) + ": S1 not held by the departure during its boarding hold");
            }

            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<StandUnavailable, StandAssigned>(rig.Rec, w);
            Assert.Single(pairs);
            Assert.True(pairs[0].Hold.Tick < h);
            Assert.True(pairs[0].Release.Tick >= p && pairs[0].Release.Tick <= p + 1UL);
            Assert.Equal(rig.Rec.Milestone(rd, FlightMilestone.Pushback).Id, pairs[0].Release.Env.Cause.Id);
        }
    }
}
