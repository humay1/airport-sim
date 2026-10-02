using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.9 (Q-079): AircraftTrack.OpenHold holds the opening event of the
    /// flight's open runway, taxiway or passenger hold, from the hold to its
    /// release, and is EventRef.None before and after; StandState.VacatedBy
    /// holds the Pushback that freed the stand, from that Pushback until the
    /// stand is next occupied, and is EventRef.None while it is occupied.
    /// Checked at the end of every tick.
    /// </summary>
    public sealed class OpenHoldTests
    {
        private static void AssertOpenHoldSpans(Dictionary<ulong, EventRef> seen, ulong from, ulong to, EventId hold, string family)
        {
            foreach (KeyValuePair<ulong, EventRef> kv in seen)
            {
                string at = family + " t=" + kv.Key.ToString(CultureInfo.InvariantCulture) + ": ";
                if (kv.Key >= from && kv.Key < to)
                {
                    Assert.True(kv.Value.HasValue, at + "OpenHold unset during the hold");
                    Assert.Equal(hold, kv.Value.Id);
                }
                else
                {
                    Assert.False(kv.Value.HasValue, at + "OpenHold set outside the hold: " + Show.Cause(kv.Value));
                }
            }
        }

        private static Dictionary<ulong, EventRef> TrackOpenHold(HostRig rig, ulong flight, ulong until)
        {
            var seen = new Dictionary<ulong, EventRef>();
            rig.StepEach(until, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(flight), out AircraftTrack tr))
                {
                    seen[t] = tr.OpenHold;
                }
            });
            return seen;
        }

        [Fact]
        public void test_open_hold_and_vacated_by_track_cross_tick_causes()
        {
            // Runway hold: A2 (06:01) is held behind A1's 3600 slot from 3610 and released at 3640.
            var runway = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00"), Csv.Row("A2", "A", "06:01")));
            ulong a2 = runway.Id("A2");
            Dictionary<ulong, EventRef> rwy = TrackOpenHold(runway, a2, 3700UL);
            var rwyHold = runway.Rec.Of<AircraftHeldForRunway>(a2);
            Assert.Single(rwyHold);
            AssertOpenHoldSpans(rwy, rwyHold[0].Rec.Tick, runway.Rec.Of<AircraftHeldForRunwayReleased>(a2)[0].Rec.Tick, rwyHold[0].Rec.Id, "runway");
            Assert.Equal(3610UL, rwyHold[0].Rec.Tick);

            // Taxi hold: A1 waits at T for E1 from 3640 and enters at 3651 (TaxiTests).
            var taxi = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:03"), Csv.Row("D1", "D", "06:35")));
            ulong a1 = taxi.Id("A1");
            Dictionary<ulong, EventRef> tx = TrackOpenHold(taxi, a1, 3700UL);
            var taxiHold = taxi.Rec.Of<AircraftHeldOnTaxiway>(a1);
            Assert.Single(taxiHold);
            AssertOpenHoldSpans(tx, taxiHold[0].Rec.Tick, taxi.Rec.Of<AircraftHeldOnTaxiwayReleased>(a1)[0].Rec.Tick, taxiHold[0].Rec.Id, "taxi");
            Assert.Equal(3640UL, taxiHold[0].Rec.Tick);

            // Boarding hold and VacatedBy: R_D is held 50 ticks from its doors-close
            // point, then pushes back from S1; W (a320, 07:40) is assigned S1 at
            // its OffRunway, which ends the stand's VacatedBy.
            var rows = new List<string>(Csv.Pair("R_A", "R_D", "06:30", "08:00"));
            rows.Add(Csv.Row("W", "A", "07:40"));
            var board = new HostRig(Csv.Of(rows.ToArray()), flow: ScriptedFlow.For(2UL, 50UL, 3, 77U));
            ulong rd = board.Id("R_D");
            ulong w = board.Id("W");
            Assert.Equal(2UL, rd);
            var openHold = new Dictionary<ulong, EventRef>();
            var vacatedBy = new Dictionary<ulong, EventRef>();
            var occupied = new Dictionary<ulong, bool>();
            board.StepEach(AirConst.TicksPerDay, t =>
            {
                if (board.Airside.TryGetTrack(new FlightId(rd), out AircraftTrack tr))
                {
                    openHold[t] = tr.OpenHold;
                }

                Assert.True(board.Airside.TryGetStand(new StandId(FixtureLayout.S1), out StandState st));
                vacatedBy[t] = st.VacatedBy;
                occupied[t] = st.Occupant.HasValue;
            });

            var paxHold = board.Rec.Of<DepartureHeldForPassengers>(rd);
            Assert.Single(paxHold);
            ulong released = board.Rec.Of<DepartureHeldForPassengersReleased>(rd)[0].Rec.Tick;
            Assert.Equal(paxHold[0].Rec.Tick + 50UL, released);
            AssertOpenHoldSpans(openHold, paxHold[0].Rec.Tick, released, paxHold[0].Rec.Id, "boarding");

            Rec pushback = board.Rec.Milestone(rd, FlightMilestone.Pushback);
            ulong reoccupied = board.Rec.Milestone(w, FlightMilestone.OffRunway).Tick;
            Assert.True(reoccupied > pushback.Tick + 1UL, "fixture assumption: S1 stays free for a while after R_D pushes back");
            foreach (KeyValuePair<ulong, EventRef> kv in vacatedBy)
            {
                string at = "S1 t=" + kv.Key.ToString(CultureInfo.InvariantCulture) + ": ";
                if (kv.Key >= pushback.Tick && kv.Key < reoccupied)
                {
                    Assert.False(occupied[kv.Key], at + "occupied while vacated");
                    Assert.True(kv.Value.HasValue, at + "VacatedBy unset after the Pushback");
                    Assert.Equal(pushback.Id, kv.Value.Id);
                }
                else
                {
                    Assert.False(kv.Value.HasValue, at + "VacatedBy set: " + Show.Cause(kv.Value));
                }
            }

            Assert.True(occupied[reoccupied], "W did not take S1 at its OffRunway");
            Assert.Equal(w, board.Occupant(FixtureLayout.S1)!.Value.Value);
        }
    }
}
