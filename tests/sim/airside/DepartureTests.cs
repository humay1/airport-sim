using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 "The boarding hold (both paths)" (D6), with a fake
    /// IFlowSystem answering TryGetOutstanding (§12.13). The doors-close
    /// point here is the fallback's, DoorsOpen + MinTurnaround. At it, with
    /// passengers outstanding: DepartureHeldForPassengers { Flight,
    /// o.Count, o.MostHeldAt }, PassengerHoldSince = tick, DueAt = tick +
    /// BoardingHoldMaxMinutes × TICKS_PER_SIM_MINUTE. Released when nobody is
    /// outstanding or tick >= DueAt, with Cause = the hold, then Absorb,
    /// DoorsClosed and Pushback.
    /// </summary>
    public sealed class DepartureTests
    {
        internal const ulong Rd = 2UL;

        internal static HostRig Rig(FakeFlowBase? flow, uint holdMinutes = 10U, bool turnaroundRegistered = false)
        {
            var rig = new HostRig(Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00")), holdMinutes: holdMinutes, flow: flow, turnaroundRegistered: turnaroundRegistered);
            Assert.Equal(Rd, rig.Id("R_D"));
            return rig;
        }

        /// <summary>The fallback doors-close point: the arrival's DoorsOpen + its MinTurnaround.</summary>
        internal static ulong DoorsClosePoint(HostRig rig)
        {
            return rig.Rec.Milestone(rig.Id("R_A"), FlightMilestone.DoorsOpen).Milestone.ActualTick + (35UL * AirConst.TicksPerMinute);
        }

        [Fact]
        public void test_departure_holds_while_passengers_outstanding_and_releases_when_all_at_gate()
        {
            var flow = new ScriptedFlow((s, f) => f != Rd ? null : s < 30UL ? (5, 77U) : s < 60UL ? (2, 78U) : ((int, uint)?)null);
            HostRig rig = Rig(flow);
            var tracks = new Dictionary<ulong, AircraftTrack>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(Rd), out AircraftTrack tr))
                {
                    tracks[t] = tr;
                }
            });

            ulong h = DoorsClosePoint(rig);
            Assert.Equal(h, flow.FirstQuery(Rd));
            Rec onStand = rig.Rec.Milestone(Rd, FlightMilestone.OnStand);
            Assert.Equal(h, onStand.Tick);

            var held = rig.Rec.Of<DepartureHeldForPassengers>(Rd);
            Assert.Single(held);
            Assert.Equal(h, held[0].Rec.Tick);
            Assert.Equal(Rd, held[0].Evt.Flight.Value);
            Assert.Equal(5, held[0].Evt.Outstanding);
            Assert.Equal(77U, held[0].Evt.HeldAt!.Value.Value);

            Assert.Equal(h, tracks[h].PassengerHoldSince);
            Assert.Equal(h + 100UL, tracks[h].DueAt);
            for (ulong t = h; t < h + 60UL; t++)
            {
                Assert.Equal(h, tracks[t].PassengerHoldSince);
            }

            var released = rig.Rec.Of<DepartureHeldForPassengersReleased>(Rd);
            Assert.Single(released);
            Assert.Equal(h + 60UL, released[0].Rec.Tick);
            Assert.Equal(0, released[0].Evt.Outstanding);
            Assert.False(released[0].Evt.HeldAt.HasValue, "HeldAt is null on the Released event (10 §10.6)");
            Assert.Equal(held[0].Rec.Id, released[0].Rec.Env.Cause.Id);

            Rec closed = rig.Rec.Milestone(Rd, FlightMilestone.DoorsClosed);
            Rec pushback = rig.Rec.Milestone(Rd, FlightMilestone.Pushback);
            Assert.Equal(h + 60UL, closed.Milestone.ActualTick);
            Assert.Equal(AirConst.At(8, 0), closed.Milestone.PlannedTick);
            Assert.Equal(h + 60UL, pushback.Milestone.ActualTick);
            Assert.True(released[0].Rec.Id.CompareTo(closed.Id) < 0 && closed.Id.CompareTo(pushback.Id) < 0, "order must be Released, DoorsClosed, Pushback");

            var absorbs = flow.AbsorbsOf(Rd);
            Assert.Single(absorbs);
            Assert.Equal(h + 60UL, absorbs[0].Tick);
            Assert.Equal(FixtureLayout.Sink(FixtureLayout.S1), absorbs[0].Sink);

            if (tracks.TryGetValue(h + 60UL, out AircraftTrack after))
            {
                Assert.Equal(AirConst.TickUnscheduled, after.PassengerHoldSince);
            }
        }

        [Fact]
        public void test_departure_hold_times_out_at_boarding_hold_max_and_remainder_is_missed()
        {
            foreach (uint minutes in new[] { 10U, 3U })
            {
                var flow = new ScriptedFlow((s, f) => f != Rd ? null : s < 50UL ? (4, 77U) : (3, 77U));
                HostRig rig = Rig(flow, holdMinutes: minutes);
                rig.RunTo(AirConst.TicksPerDay);

                ulong h = DoorsClosePoint(rig);
                ulong due = h + (minutes * AirConst.TicksPerMinute);
                string at = " (hold " + minutes.ToString(CultureInfo.InvariantCulture) + " min)";

                var released = rig.Rec.Of<DepartureHeldForPassengersReleased>(Rd);
                Assert.True(released.Count == 1, "expected one release" + at);
                Assert.True(due == released[0].Rec.Tick, "released at " + released[0].Rec.Tick.ToString(CultureInfo.InvariantCulture) + ", due " + due.ToString(CultureInfo.InvariantCulture) + at);

                // Outstanding is the count still upstream at release: 4 until
                // 50 ticks in, 3 after; a 3-minute hold ends before the drop.
                Assert.Equal(minutes == 10U ? 3 : 4, released[0].Evt.Outstanding);
                Assert.Equal(due, rig.Rec.Milestone(Rd, FlightMilestone.DoorsClosed).Milestone.ActualTick);
                Assert.Equal(due, rig.Rec.Milestone(Rd, FlightMilestone.Pushback).Milestone.ActualTick);

                // The remainder is missed at that Absorb, reported by sim.flow,
                // never re-published by sim.airside (12 §12.7).
                var absorbs = flow.AbsorbsOf(Rd);
                Assert.Single(absorbs);
                Assert.Equal(due, absorbs[0].Tick);
                Assert.Empty(rig.Rec.Of<PassengersMissedFlight>());

                // Queried once at the doors-close point and once per held tick, then never again.
                var expected = new List<ulong>();
                for (ulong t = h; t <= due; t++)
                {
                    expected.Add(t);
                }

                Assert.Equal(expected, flow.QueryTicks(Rd));
            }
        }

        [Fact]
        public void test_departure_hold_emits_one_event_pair_with_most_held_at_node()
        {
            // The node holding most outstanding passengers moves from 55 to 66
            // mid-hold; the opening event names the node at the hold's start.
            var flow = new ScriptedFlow((s, f) => f != Rd ? null : s < 20UL ? (6, 55U) : s < 70UL ? (6, 66U) : ((int, uint)?)null);
            HostRig rig = Rig(flow);
            rig.RunTo(AirConst.TicksPerDay);
            ulong h = DoorsClosePoint(rig);

            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<DepartureHeldForPassengers, DepartureHeldForPassengersReleased>(rig.Rec, Rd);
            Assert.Single(pairs);
            Assert.Single(rig.Rec.Of<DepartureHeldForPassengers>());
            Assert.Single(rig.Rec.Of<DepartureHeldForPassengersReleased>());

            var hold = (DepartureHeldForPassengers)pairs[0].Hold.Payload;
            Assert.Equal(h, pairs[0].Hold.Tick);
            Assert.Equal(6, hold.Outstanding);
            Assert.Equal(55U, hold.HeldAt!.Value.Value);

            var release = (DepartureHeldForPassengersReleased)pairs[0].Release.Payload;
            Assert.Equal(h + 70UL, pairs[0].Release.Tick);
            Assert.Equal(0, release.Outstanding);
            Assert.False(release.HeldAt.HasValue);
        }
    }
}
