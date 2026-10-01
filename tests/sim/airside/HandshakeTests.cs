using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 "With sim.turnaround registered" (turnaroundRegistered =
    /// true, §12.12a), driven by a probe at sim.turnaround's registry position
    /// publishing the two milestones sim.airside consumes (§12.11). Step 3:
    /// DeboardComplete for the arrival hands the stand to the departure on
    /// sim.airside's next Tick, Cause = the DeboardComplete. Step 5:
    /// BoardingComplete for the departure closes the doors on the next Tick.
    /// The fallback never fires in this mode.
    /// </summary>
    public sealed class HandshakeTests
    {
        private const ulong Deboard = 4500UL;
        private const ulong Boarding = 5000UL;

        private static HostRig Rig(TurnaroundProbe probe, FakeFlowBase? flow = null)
        {
            var rows = new List<string>(Csv.Pair("R_A", "R_D", "06:30", "08:00"));
            rows.Add(Csv.Row("X1", "A", "06:40"));
            return new HostRig(Csv.Of(rows.ToArray()), flow: flow, turnaroundRegistered: true, turnaround: probe);
        }

        [Fact]
        public void test_handshake_fallback_never_fires_when_turnaround_registered()
        {
            var probe = new TurnaroundProbe();
            HostRig rig = Rig(probe);
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            rig.RunTo(AirConst.TicksPerDay);

            Assert.True(rig.Rec.Has(ra, FlightMilestone.DoorsOpen));
            Assert.Empty(rig.Rec.Milestones(rd));
            Assert.False(rig.Airside.TryGetTrack(new FlightId(rd), out _));
            Assert.Equal(ra, rig.Occupant(FixtureLayout.S1)!.Value.Value);
        }

        [Fact]
        public void test_handshake_deboard_complete_hands_off_stand_on_next_tick()
        {
            var probe = new TurnaroundProbe();
            HostRig rig = Rig(probe);
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            ulong x1 = rig.Id("X1");
            probe.Script.Add((Deboard, ra, FlightMilestone.DeboardComplete));
            probe.Script.Add((Deboard, x1, FlightMilestone.DeboardComplete));

            rig.RunTo(Deboard);
            Assert.False(rig.Track(ra).RecordedCause.HasValue, "RecordedCause set before any consumed event");
            rig.RunTo(Deboard + 1UL);
            Assert.True(rig.Rec.Milestone(ra, FlightMilestone.DoorsOpen).Tick < Deboard, "fixture assumption: R_A's doors open before the scripted DeboardComplete");
            Assert.Empty(rig.Rec.Milestones(rd));
            Assert.Equal(ra, rig.Occupant(FixtureLayout.S1)!.Value.Value);

            // 12 §12.8 step 3 (Q-062): the phase-3 handler records the event on the arrival.
            AircraftTrack waiting = rig.Track(ra);
            Assert.True(waiting.RecordedCause.HasValue);
            Assert.Equal(probe.Published.Find(p => p.Flight == ra).Id, waiting.RecordedCause.Id);
            Assert.False(rig.Track(x1).RecordedCause.HasValue, "a rotation-less arrival records nothing");

            rig.RunTo(Deboard + 2UL);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(ra), out _), "the arrival leaves tracked state at the handoff");
            Rec onStand = rig.Rec.Milestone(rd, FlightMilestone.OnStand);
            Assert.Equal(Deboard + 1UL, onStand.Milestone.ActualTick);
            Assert.Equal(AirConst.At(8, 0) - (35UL * AirConst.TicksPerMinute), onStand.Milestone.PlannedTick);
            Assert.True(onStand.Env.Cause.HasValue);
            Assert.Equal(probe.Published.Find(p => p.Flight == ra).Id, onStand.Env.Cause.Id);
            Assert.Equal(rd, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            AircraftTrack tr = rig.Track(rd);
            Assert.Equal(AircraftLegPhase.OnStand, tr.Phase);
            Assert.Equal(FixtureLayout.S1, tr.Stand!.Value.Value);
            Assert.Empty(rig.Rec.Of<StandAssigned>(rd));

            // A rotation-less arrival's DeboardComplete creates nothing.
            Assert.All(rig.Airside.TrackedFlights(), f => Assert.True(f.Value == ra || f.Value == rd || f.Value == x1));

            // Doors wait for BoardingComplete, which never comes here.
            rig.RunTo(AirConst.TicksPerDay);
            Assert.False(rig.Rec.Has(rd, FlightMilestone.DoorsClosed));
            Assert.Equal(rd, rig.Occupant(FixtureLayout.S1)!.Value.Value);
        }

        [Fact]
        public void test_handshake_boarding_complete_closes_doors_on_next_tick()
        {
            var probe = new TurnaroundProbe();
            ScriptedFlow flow = ScriptedFlow.None();
            HostRig rig = Rig(probe, flow);
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            probe.Script.Add((Deboard, ra, FlightMilestone.DeboardComplete));
            probe.Script.Add((Boarding, rd, FlightMilestone.BoardingComplete));
            rig.RunTo(Deboard + 2UL);
            Assert.False(rig.Track(rd).RecordedCause.HasValue, "the handoff leaves the departure's RecordedCause unset");

            // 12 §12.8 step 5 (Q-062): recorded on the departure, cleared at the doors-close point.
            rig.RunTo(Boarding + 1UL);
            AircraftTrack waiting = rig.Track(rd);
            Assert.True(waiting.RecordedCause.HasValue);
            Assert.Equal(probe.Published.Find(p => p.Flight == rd).Id, waiting.RecordedCause.Id);
            rig.RunTo(Boarding + 2UL);
            Assert.False(rig.Track(rd).RecordedCause.HasValue, "the doors-close point clears RecordedCause");

            rig.RunTo(AirConst.TicksPerDay);
            Rec closed = rig.Rec.Milestone(rd, FlightMilestone.DoorsClosed);
            Assert.Equal(Boarding + 1UL, closed.Milestone.ActualTick);
            Assert.Equal(AirConst.At(8, 0), closed.Milestone.PlannedTick);
            Rec pushback = rig.Rec.Milestone(rd, FlightMilestone.Pushback);
            Assert.Equal(Boarding + 1UL, pushback.Milestone.ActualTick);
            Assert.True(closed.Id.CompareTo(pushback.Id) < 0);

            var absorbs = flow.AbsorbsOf(rd);
            Assert.Single(absorbs);
            Assert.Equal(Boarding + 1UL, absorbs[0].Tick);
            Assert.Equal(FixtureLayout.Sink(FixtureLayout.S1), absorbs[0].Sink);
            Assert.Equal(Boarding + 1UL, flow.FirstQuery(rd));
        }

        [Fact]
        public void test_handshake_boarding_hold_starts_at_doors_close_point_with_boarding_complete_cause()
        {
            var probe = new TurnaroundProbe();
            ulong rdId = 2UL;
            ScriptedFlow flow = ScriptedFlow.For(rdId, 30UL, 4, 77U);
            HostRig rig = Rig(probe, flow);
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            Assert.Equal(rdId, rd);
            probe.Script.Add((Deboard, ra, FlightMilestone.DeboardComplete));
            probe.Script.Add((Boarding, rd, FlightMilestone.BoardingComplete));
            rig.RunTo(AirConst.TicksPerDay);

            var held = rig.Rec.Of<DepartureHeldForPassengers>(rd);
            Assert.Single(held);
            Assert.Equal(Boarding + 1UL, held[0].Rec.Tick);
            Assert.Equal(probe.Published.Find(p => p.Flight == rd).Id, held[0].Rec.Env.Cause.Id);
            Assert.Equal(4, held[0].Evt.Outstanding);
            Assert.Equal(77U, held[0].Evt.HeldAt!.Value.Value);
            Assert.Equal(Boarding + 1UL + 30UL, rig.Rec.Milestone(rd, FlightMilestone.DoorsClosed).Milestone.ActualTick);
        }
    }
}
