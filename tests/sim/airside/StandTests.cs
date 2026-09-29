using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.7 stands: compatible and free, lowest StandId (the fixture
    /// declares stands in ascending id, so "earliest-declared" agrees);
    /// no stand free holds at the threshold with StandUnavailable { Flight,
    /// null, null }, then StandAssigned with Cause = the freeing Pushback;
    /// occupied OnStand through Pushback. §12.3/§12.8: the stand passes from
    /// the arrival's FlightId to the departure's without being freed.
    /// </summary>
    public sealed class StandTests
    {
        [Fact]
        public void test_stand_assignment_prefers_lowest_id_among_compatible_free_stands()
        {
            // S1 takes medium at most, S2 super, S3 heavy, S4 super.
            var rig = new HostRig(Csv.Of(
                Csv.Row("X1", "A", "06:00", aircraft: "a359"),
                Csv.Row("X2", "A", "06:10", aircraft: "a320"),
                Csv.Row("X3", "A", "06:20", aircraft: "a359"),
                Csv.Row("X4", "A", "06:30", aircraft: "a388")));
            rig.RunTo(5000UL);

            var expected = new (string Name, int Hh, int Mm, ushort Stand)[]
            {
                ("X1", 6, 0, FixtureLayout.S2),  // heavy: S1 excluded, S2 lowest compatible
                ("X2", 6, 10, FixtureLayout.S1), // medium: S1 free and lowest
                ("X3", 6, 20, FixtureLayout.S3), // heavy: S1 excluded, S2 taken
                ("X4", 6, 30, FixtureLayout.S4), // super: S1 and S3 excluded, S2 taken
            };
            foreach ((string name, int hh, int mm, ushort stand) in expected)
            {
                ulong id = rig.Id(name);
                Rec onStand = rig.Rec.Milestone(id, FlightMilestone.OnStand);
                Assert.True(onStand.HasTrack, name + " untracked at OnStand");
                Assert.Equal(stand, onStand.Track.Stand!.Value.Value);
                Assert.Equal(id, rig.Occupant(stand)!.Value.Value);
                ulong planned = AirConst.At(hh, mm) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(stand);
                Assert.Equal(planned, onStand.Milestone.PlannedTick);
                Assert.Equal(planned, onStand.Milestone.ActualTick);
                Assert.Empty(rig.Rec.Of<StandUnavailable>(id));
            }

            Assert.Empty(rig.Free());
        }

        /// <summary>X1-X3 fill S2-S4 for good; R_A/R_D rotate through S1; W (a320) finds no compatible stand free.</summary>
        internal static string[] FullApron(string wSched = "06:40")
        {
            var rows = new List<string>
            {
                Csv.Row("X1", "A", "06:00", aircraft: "a388"),
                Csv.Row("X2", "A", "06:10", aircraft: "a359"),
                Csv.Row("X3", "A", "06:20", aircraft: "a388"),
                Csv.Row("W", "A", wSched, aircraft: "a320"),
            };
            rows.AddRange(Csv.Pair("R_A", "R_D", "06:30", "08:00"));
            return rows.ToArray();
        }

        [Fact]
        public void test_stand_unavailable_holds_at_threshold_until_pushback_frees_stand()
        {
            var rig = new HostRig(Csv.Of(FullApron()));
            ulong w = rig.Id("W");
            ulong rd = rig.Id("R_D");
            var tracks = new Dictionary<ulong, AircraftTrack>();
            var free = new Dictionary<ulong, List<ushort>>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(w), out AircraftTrack tr))
                {
                    tracks[t] = tr;
                }

                free[t] = rig.Free();
            });

            Assert.Equal(FixtureLayout.S2, rig.Rec.Milestone(rig.Id("X1"), FlightMilestone.OnStand).Track.Stand!.Value.Value);
            Assert.Equal(FixtureLayout.S3, rig.Rec.Milestone(rig.Id("X2"), FlightMilestone.OnStand).Track.Stand!.Value.Value);
            Assert.Equal(FixtureLayout.S4, rig.Rec.Milestone(rig.Id("X3"), FlightMilestone.OnStand).Track.Stand!.Value.Value);
            Assert.Equal(FixtureLayout.S1, rig.Rec.Milestone(rig.Id("R_A"), FlightMilestone.OnStand).Track.Stand!.Value.Value);

            ulong offRunway = rig.Rec.Milestone(w, FlightMilestone.OffRunway).Milestone.ActualTick;
            Assert.Equal(AirConst.At(6, 40) + FixtureLayout.OccupancyTicks, offRunway);

            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<StandUnavailable, StandAssigned>(rig.Rec, w);
            Assert.Single(pairs);
            var unavailable = (StandUnavailable)pairs[0].Hold.Payload;
            Assert.Equal(offRunway, pairs[0].Hold.Tick);
            Assert.False(unavailable.Stand.HasValue);
            Assert.False(unavailable.Occupying.HasValue);

            Rec pushback = rig.Rec.Milestone(rd, FlightMilestone.Pushback);
            var assigned = (StandAssigned)pairs[0].Release.Payload;
            Assert.Equal(FixtureLayout.S1, assigned.Stand!.Value.Value);
            Assert.False(assigned.Occupying.HasValue);

            // Cause is the Pushback that freed S1 (12 §12.7). The stand is
            // occupied "through Pushback inclusive", so the assignment is on
            // that tick or the next; the spec does not pin which.
            Assert.Equal(pushback.Id, pairs[0].Release.Env.Cause.Id);
            ulong at = pairs[0].Release.Tick;
            Assert.True(at >= pushback.Tick && at <= pushback.Tick + 1UL, "StandAssigned at " + at.ToString(CultureInfo.InvariantCulture) + ", Pushback at " + pushback.Tick.ToString(CultureInfo.InvariantCulture));

            // While waiting: at the threshold node, on no edge, no stand, and
            // (until the Pushback tick, where the freeing is in flight) no stand free.
            for (ulong t = offRunway; t < pushback.Tick; t++)
            {
                AircraftTrack tr = tracks[t];
                Assert.Equal(FixtureLayout.Threshold, tr.AtNode!.Value.Value);
                Assert.False(tr.OnEdge.HasValue, "waiting for a stand but on an edge: " + Show.Track(tr));
                Assert.False(tr.Stand.HasValue, "waiting for a stand but holds one: " + Show.Track(tr));
                Assert.Empty(free[t]);
            }

            Rec onStand = rig.Rec.Milestone(w, FlightMilestone.OnStand);
            Assert.Equal(FixtureLayout.S1, onStand.Track.Stand!.Value.Value);
            Assert.Equal(AirConst.At(6, 40) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(FixtureLayout.S1), onStand.Milestone.PlannedTick);
            Assert.Equal(w, rig.Occupant(FixtureLayout.S1)!.Value.Value);
        }

        [Fact]
        public void test_stand_hands_off_from_arrival_to_departure_flightid_without_freeing()
        {
            // A 50-tick boarding hold keeps R_D on the stand long enough to see
            // the handoff between ticks, not only within one.
            var rig = new HostRig(Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00")), flow: ScriptedFlow.For(2UL, 50UL, 3, 77U));
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            Assert.Equal(2UL, rd);
            var occupant = new Dictionary<ulong, ulong?>();
            var free = new Dictionary<ulong, List<ushort>>();
            var rdTrack = new Dictionary<ulong, AircraftTrack>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                FlightId? o = rig.Occupant(FixtureLayout.S1);
                occupant[t] = o.HasValue ? o.Value.Value : (ulong?)null;
                free[t] = rig.Free();
                if (rig.Airside.TryGetTrack(new FlightId(rd), out AircraftTrack tr))
                {
                    rdTrack[t] = tr;
                }
            });

            ulong onStand = rig.Rec.Milestone(ra, FlightMilestone.OnStand).Milestone.ActualTick;
            ulong open = rig.Rec.Milestone(ra, FlightMilestone.DoorsOpen).Milestone.ActualTick;
            Rec depOnStand = rig.Rec.Milestone(rd, FlightMilestone.OnStand);
            ulong handoff = open + (35UL * AirConst.TicksPerMinute);
            Assert.Equal(handoff, depOnStand.Milestone.ActualTick);
            Assert.Equal(AirConst.At(8, 0) - (35UL * AirConst.TicksPerMinute), depOnStand.Milestone.PlannedTick);
            ulong pushback = rig.Rec.Milestone(rd, FlightMilestone.Pushback).Milestone.ActualTick;
            Assert.Equal(handoff + 50UL, pushback);

            for (ulong t = onStand; t < handoff; t++)
            {
                Assert.True(occupant[t] == ra, "t=" + t.ToString(CultureInfo.InvariantCulture) + ": S1 occupant should be the arrival");
            }

            for (ulong t = handoff; t < pushback; t++)
            {
                Assert.True(occupant[t] == rd, "t=" + t.ToString(CultureInfo.InvariantCulture) + ": S1 occupant should be the departure");
            }

            for (ulong t = onStand; t < pushback; t++)
            {
                Assert.DoesNotContain(FixtureLayout.S1, free[t]);
            }

            Assert.Contains(FixtureLayout.S1, free[pushback + 1UL]);

            AircraftTrack created = rdTrack[handoff];
            Assert.Equal(MovementKind.Departure, created.Kind);
            Assert.Equal(AircraftLegPhase.OnStand, created.Phase);
            Assert.Equal(FixtureLayout.S1, created.Stand!.Value.Value);
            Assert.False(rdTrack.ContainsKey(handoff - 1UL), "departure tracked before the handoff");

            // Never freed, only handed off: no stand event for either FlightId.
            Assert.Empty(rig.Rec.Of<StandAssigned>(ra));
            Assert.Empty(rig.Rec.Of<StandAssigned>(rd));
            Assert.Empty(rig.Rec.Of<StandUnavailable>(ra));
            Assert.Empty(rig.Rec.Of<StandUnavailable>(rd));
        }
    }
}
