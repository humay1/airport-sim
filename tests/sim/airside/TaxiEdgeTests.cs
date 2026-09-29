using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.6, the taxiway model: one aircraft per edge; a blocked aircraft
    /// holds at the node, emitting AircraftHeldOnTaxiway { Flight, EdgeId,
    /// blocking } and later the Released event with Cause = the hold. Plus
    /// §12.9 (Q-008): while OnEdge is set, AtNode is the node the aircraft
    /// entered the edge from, and EdgeProgress runs 0 to 1 from there.
    /// </summary>
    public sealed class TaxiEdgeTests
    {
        [Fact]
        public void test_taxi_edge_single_occupant_holds_second_aircraft()
        {
            // D1 pushes back from S1 at 3600, is on E2 3600-3620, then on E1
            // (J1 -> T) 3620-3650. A1 lands at 3630 and leaves the runway at
            // 3640 at T, wanting E1 while D1 still holds it.
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:03"), Csv.Row("D1", "D", "06:35")));
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            var a1Tracks = new Dictionary<ulong, AircraftTrack>();
            var d1Tracks = new Dictionary<ulong, AircraftTrack>();
            rig.StepEach(3800UL, t =>
            {
                string? v = AirsideAsserts.TaxiViolation(rig.Airside, t);
                Assert.True(v == null, v);
                if (rig.Airside.TryGetTrack(new FlightId(a1), out AircraftTrack ta))
                {
                    a1Tracks[t] = ta;
                }

                if (rig.Airside.TryGetTrack(new FlightId(d1), out AircraftTrack td))
                {
                    d1Tracks[t] = td;
                }
            });

            Assert.Equal(3640UL, rig.Rec.Milestone(a1, FlightMilestone.OffRunway).Milestone.ActualTick);

            List<(Rec Hold, Rec Release)> pairs = AirsideAsserts.Pairs<AircraftHeldOnTaxiway, AircraftHeldOnTaxiwayReleased>(rig.Rec, a1);
            Assert.Single(pairs);
            var hold = (AircraftHeldOnTaxiway)pairs[0].Hold.Payload;
            Assert.Equal(3640UL, pairs[0].Hold.Tick);
            Assert.Equal(FixtureLayout.E1, hold.Edge.Value);
            Assert.True(hold.Blocking.HasValue, "blocking flight not named");
            Assert.Equal(d1, hold.Blocking!.Value.Value);
            var release = (AircraftHeldOnTaxiwayReleased)pairs[0].Release.Payload;
            Assert.Equal(FixtureLayout.E1, release.Edge.Value);

            // Released the first tick E1 is free. D1 leaves it at 3650; which
            // of the two is processed first within that tick is not pinned.
            ulong released = pairs[0].Release.Tick;
            Assert.True(released >= 3650UL && released <= 3651UL, "released at " + released.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(FixtureLayout.E1, a1Tracks[released].OnEdge!.Value.Value);
            Assert.Equal(FixtureLayout.Threshold, a1Tracks[released].AtNode!.Value.Value);

            for (ulong t = 3640UL; t < 3650UL; t++)
            {
                AircraftTrack ta = a1Tracks[t];
                Assert.Equal(AircraftLegPhase.HeldOnTaxiway, ta.Phase);
                Assert.False(ta.OnEdge.HasValue, "held mid-edge: " + Show.Track(ta));
                Assert.Equal(FixtureLayout.Threshold, ta.AtNode!.Value.Value);
                Assert.Equal(FixtureLayout.E1, d1Tracks[t].OnEdge!.Value.Value);
            }

            Assert.Empty(rig.Rec.Of<AircraftHeldOnTaxiway>(d1));
        }

        [Fact]
        public void test_taxi_edge_at_node_is_entry_node_and_progress_runs_from_it()
        {
            // D1 crosses E1 (declared T -> J1, bidirectional) from J1 to T.
            var rig = new HostRig(Csv.Of(Csv.Row("D1", "D", "06:35")));
            ulong d1 = rig.Id("D1");
            var onE1 = new List<(ulong Tick, AircraftTrack Track)>();
            var onE2 = new List<(ulong Tick, AircraftTrack Track)>();
            rig.StepEach(3660UL, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(d1), out AircraftTrack tr) && tr.OnEdge.HasValue)
                {
                    if (tr.OnEdge.Value.Value == FixtureLayout.E1)
                    {
                        onE1.Add((t, tr));
                    }
                    else if (tr.OnEdge.Value.Value == FixtureLayout.E2)
                    {
                        onE2.Add((t, tr));
                    }
                }
            });

            Assert.NotEmpty(onE2);
            Assert.NotEmpty(onE1);
            foreach ((ulong t, AircraftTrack tr) in onE2)
            {
                Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S1), tr.AtNode!.Value.Value);
            }

            foreach ((ulong t, AircraftTrack tr) in onE1)
            {
                Assert.Equal(FixtureLayout.J1, tr.AtNode!.Value.Value);
                Assert.Equal(AircraftLegPhase.Taxiing, tr.Phase);
            }

            // Entering an edge sets PhaseEnteredAt = tick, DueAt = tick + TraversalTicks.
            Assert.Equal(3600UL, onE2[0].Track.PhaseEnteredAt);
            Assert.Equal(3620UL, onE2[0].Track.DueAt);
            Assert.Equal(3620UL, onE1[0].Track.PhaseEnteredAt);
            Assert.Equal(3650UL, onE1[0].Track.DueAt);
            AssertProgress(onE1);
            AssertProgress(onE2);
        }

        /// <summary>0..1, never decreasing along one edge, and strictly inside (0, 1) somewhere mid-edge.</summary>
        internal static void AssertProgress(List<(ulong Tick, AircraftTrack Track)> onEdge)
        {
            Fx previous = Fx.Zero;
            bool inside = false;
            foreach ((ulong t, AircraftTrack tr) in onEdge)
            {
                Fx p = tr.EdgeProgress;
                Assert.True(p >= Fx.Zero && p <= Fx.One, "EdgeProgress out of 0..1 at t=" + t.ToString(CultureInfo.InvariantCulture) + ": " + Show.Track(tr));
                Assert.True(p >= previous, "EdgeProgress went backwards at t=" + t.ToString(CultureInfo.InvariantCulture));
                inside |= p > Fx.Zero && p < Fx.One;
                previous = p;
            }

            Assert.True(inside, "EdgeProgress never strictly between 0 and 1 during the traversal");
        }
    }
}
