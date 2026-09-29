using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.9, AircraftTrack through an unimpeded arrival leg: tracking
    /// starts at InboundAirborne (§12.11), the phase runs left to right
    /// through OnStand, AtNode/OnEdge follow §12.9 (Q-008), and
    /// PhaseEnteredAt/DueAt follow §12.6 on every edge.
    /// </summary>
    public sealed class TrackTests
    {
        [Fact]
        public void test_track_follows_unimpeded_arrival_leg_to_stand()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")));
            ulong a1 = rig.Id("A1");
            var tracks = new Dictionary<ulong, AircraftTrack>();
            rig.StepEach(3700UL, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(a1), out AircraftTrack tr))
                {
                    tracks[t] = tr;
                }
            });

            ulong airborne = AirConst.At(6, 0) - AirConst.CruiseLead;
            Assert.False(tracks.ContainsKey(airborne - 1UL), "tracked before InboundAirborne");
            AircraftTrack first = tracks[airborne];
            Assert.Equal(a1, first.Flight.Value);
            Assert.Equal(MovementKind.Arrival, first.Kind);
            Assert.Equal(AircraftLegPhase.AwaitingApproach, first.Phase);
            Assert.False(first.AtNode.HasValue);
            Assert.False(first.OnEdge.HasValue);
            Assert.False(first.Stand.HasValue);
            Assert.Equal(AirConst.TickUnscheduled, first.PassengerHoldSince);
            Assert.Contains(new FlightId(a1), rig.Airside.TrackedFlights());

            AircraftTrack onRunway = tracks[3600UL];
            Assert.Equal(AircraftLegPhase.OnRunway, onRunway.Phase);
            Assert.Equal(FixtureLayout.Runway, onRunway.Runway!.Value.Value);

            AircraftTrack e1 = tracks[3610UL];
            Assert.Equal(AircraftLegPhase.Taxiing, e1.Phase);
            Assert.Equal(FixtureLayout.E1, e1.OnEdge!.Value.Value);
            Assert.Equal(FixtureLayout.Threshold, e1.AtNode!.Value.Value);
            Assert.Equal(3610UL, e1.PhaseEnteredAt);
            Assert.Equal(3640UL, e1.DueAt);

            AircraftTrack e2 = tracks[3640UL];
            Assert.Equal(FixtureLayout.E2, e2.OnEdge!.Value.Value);
            Assert.Equal(FixtureLayout.J1, e2.AtNode!.Value.Value);
            Assert.Equal(3640UL, e2.PhaseEnteredAt);
            Assert.Equal(3660UL, e2.DueAt);

            AircraftTrack stand = tracks[3660UL];
            Assert.Equal(AircraftLegPhase.OnStand, stand.Phase);
            Assert.Equal(FixtureLayout.S1, stand.Stand!.Value.Value);
            Assert.False(stand.OnEdge.HasValue);
            Assert.Equal(FixtureLayout.StandNode(FixtureLayout.S1), stand.AtNode!.Value.Value);
            Assert.Equal(a1, rig.Occupant(FixtureLayout.S1)!.Value.Value);

            // Phases run left to right with nothing in between (no hold happened).
            var phases = new List<AircraftLegPhase>();
            for (ulong t = airborne; t < 3700UL; t++)
            {
                AircraftLegPhase p = tracks[t].Phase;
                if (phases.Count == 0 || phases[phases.Count - 1] != p)
                {
                    phases.Add(p);
                }
            }

            Assert.Equal(
                new List<AircraftLegPhase> { AircraftLegPhase.AwaitingApproach, AircraftLegPhase.OnRunway, AircraftLegPhase.Taxiing, AircraftLegPhase.OnStand },
                phases);
            Assert.Empty(rig.Rec.Of<AircraftHeldForRunway>(a1));
            Assert.Empty(rig.Rec.Of<AircraftHeldOnTaxiway>(a1));
            Assert.Empty(rig.Rec.Of<StandUnavailable>(a1));

            var onE1 = new List<(ulong Tick, AircraftTrack Track)>();
            for (ulong t = 3610UL; t < 3640UL; t++)
            {
                Assert.True(tracks[t].OnEdge.HasValue, "off the edge at t=" + t.ToString(CultureInfo.InvariantCulture));
                onE1.Add((t, tracks[t]));
            }

            TaxiEdgeTests.AssertProgress(onE1);
        }

        [Fact]
        public void test_track_departure_leaves_tracked_state_at_airborne()
        {
            // 12 §12.6: at Airborne "the aircraft leaves the module's tracked state".
            var rig = new HostRig(Csv.Of(Csv.Row("D1", "D", "07:00")));
            ulong d1 = rig.Id("D1");
            rig.RunTo(5000UL);
            Rec airborne = rig.Rec.Milestone(d1, FlightMilestone.Airborne);
            Assert.True(airborne.Tick < 5000UL);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(d1), out _));
            Assert.DoesNotContain(new FlightId(d1), rig.Airside.TrackedFlights());
        }
    }
}
