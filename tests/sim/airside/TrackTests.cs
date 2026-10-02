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
            for (ulong t = airborne; t < 3700UL; t++)
            {
                Assert.True(tracks.ContainsKey(t), "untracked at t=" + t.ToString(CultureInfo.InvariantCulture) + ", after InboundAirborne");
            }

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

        /// <summary>What a flight's own sim.airside events say so far, for the phase table.</summary>
        private sealed class Seen
        {
            public ulong? Inbound;
            public ulong? Landed;
            public ulong? OffRunway;
            public ulong? OnStand;
            public ulong? OnStandPlanned;
            public ulong? DoorsOpen;
            public ulong? TakeoffRoll;
            public ulong? TaxiHold;
            public ulong? RunwayHold;
            public ulong? StandAssigned;
            public bool WaitedForStand;
            public ushort? Stand;
        }

        [Fact]
        public void test_track_fields_follow_phase_table()
        {
            // 12 §12.9 "Track fields by phase" (Q-081), at the end of every tick
            // of the fixture day: phase0-200.csv on the §12.13 layout, the
            // fallback (turnaroundRegistered false), a flow fake that holds
            // departures, hold max 10 minutes, door delay 2. No command, so a
            // flight's Stand never changes once set.
            const ulong holdTicks = 10UL * AirConst.TicksPerMinute;
            var rig = new HostRig(ScheduleFixture.Bytes(), flow: new RuleFlow());
            var edges = new Dictionary<ushort, TaxiEdgeDef>();
            foreach (TaxiEdgeDef e in rig.Layout.Edges)
            {
                edges.Add(e.Id.Value, e);
            }

            var seen = new Dictionary<ulong, Seen>();
            int scanned = 0;
            var phases = new HashSet<(MovementKind, AircraftLegPhase)>();
            rig.StepEach(AirConst.TicksPerDay, t =>
            {
                for (; scanned < rig.Rec.All.Count; scanned++)
                {
                    Rec r = rig.Rec.All[scanned];
                    if (!r.FromAirside)
                    {
                        continue;
                    }

                    if (!seen.TryGetValue(r.Flight, out Seen? s))
                    {
                        s = new Seen();
                        seen.Add(r.Flight, s);
                    }

                    switch (r.Payload)
                    {
                        case FlightMilestoneReached m:
                            switch (m.Milestone)
                            {
                                case FlightMilestone.InboundAirborne: s.Inbound = r.Tick; break;
                                case FlightMilestone.Landed: s.Landed = r.Tick; break;
                                case FlightMilestone.OffRunway: s.OffRunway = r.Tick; break;
                                case FlightMilestone.OnStand: s.OnStand = r.Tick; s.OnStandPlanned = m.PlannedTick; break;
                                case FlightMilestone.DoorsOpen: s.DoorsOpen = r.Tick; break;
                                case FlightMilestone.TakeoffRoll: s.TakeoffRoll = r.Tick; break;
                            }

                            break;
                        case AircraftHeldOnTaxiway _: s.TaxiHold = r.Tick; break;
                        case AircraftHeldForRunway _: s.RunwayHold = r.Tick; break;
                        case StandUnavailable _: s.WaitedForStand = true; break;
                        case StandAssigned _: s.StandAssigned = r.Tick; break;
                    }
                }

                foreach (FlightId f in rig.Airside.TrackedFlights())
                {
                    Assert.True(rig.Airside.TryGetTrack(f, out AircraftTrack tr));
                    string why = Check(rig, t, tr, seen[f.Value], edges, holdTicks);
                    Assert.True(why.Length == 0, "t=" + t.ToString(CultureInfo.InvariantCulture) + ": " + why + " | " + Show.Track(tr));
                    phases.Add((tr.Kind, tr.Phase));
                }
            });

            // The day reached every row of the table.
            foreach (AircraftLegPhase p in new[] { AircraftLegPhase.AwaitingApproach, AircraftLegPhase.HeldForRunway, AircraftLegPhase.OnRunway, AircraftLegPhase.HeldOnTaxiway, AircraftLegPhase.Taxiing, AircraftLegPhase.OnStand })
            {
                Assert.Contains((MovementKind.Arrival, p), phases);
            }

            foreach (AircraftLegPhase p in new[] { AircraftLegPhase.OnStand, AircraftLegPhase.Taxiing, AircraftLegPhase.HeldForRunway, AircraftLegPhase.OnRunway })
            {
                Assert.Contains((MovementKind.Departure, p), phases);
            }
        }

        /// <summary>One track against its row; "" when it matches.</summary>
        private static string Check(HostRig rig, ulong t, in AircraftTrack tr, Seen s, Dictionary<ushort, TaxiEdgeDef> edges, ulong holdTicks)
        {
            const ulong none = AirConst.TickUnscheduled;
            AirportSim.Sim.Schedule.FlightRecord fr = rig.Flight(tr.Flight.Value);
            bool arrival = tr.Kind == MovementKind.Arrival;

            // Stand: once set, never changed (no command here), and kept after Pushback.
            if (tr.Stand.HasValue)
            {
                if (s.Stand.HasValue && s.Stand.Value != tr.Stand.Value.Value)
                {
                    return "Stand changed from " + s.Stand.Value;
                }

                s.Stand = tr.Stand.Value.Value;
            }
            else if (s.Stand.HasValue)
            {
                return "Stand cleared after it was set";
            }

            // Runway: set at the choice and never cleared while tracked (one runway).
            bool runwayExpected = !(arrival && tr.Phase == AircraftLegPhase.AwaitingApproach) && !(!arrival && tr.Phase == AircraftLegPhase.OnStand);
            if (runwayExpected != tr.Runway.HasValue || (tr.Runway.HasValue && tr.Runway.Value.Value != FixtureLayout.Runway))
            {
                return "Runway should be " + (runwayExpected ? "runway 1" : "unset");
            }

            // PlannedOnStand (Q-083): an arrival's emitted OnStand PlannedTick once
            // it has fired; TICK_UNSCHEDULED before that and on every departure.
            ulong plannedOnStand = arrival && s.OnStandPlanned.HasValue ? s.OnStandPlanned.Value : none;
            if (tr.PlannedOnStand != plannedOnStand)
            {
                return "PlannedOnStand should be " + plannedOnStand.ToString(CultureInfo.InvariantCulture);
            }

            // EdgeProgress (stored, written by S6): Fx.FromRatio(t - PhaseEnteredAt, TraversalTicks) on an edge, Zero off it.
            if (tr.OnEdge.HasValue)
            {
                if (tr.Phase != AircraftLegPhase.Taxiing)
                {
                    return "OnEdge set outside Taxiing";
                }

                uint traversal = edges[tr.OnEdge.Value.Value].TraversalTicks;
                if (tr.EdgeProgress != Fx.FromRatio((long)(t - tr.PhaseEnteredAt), traversal))
                {
                    return "EdgeProgress is not (t - PhaseEnteredAt) / TraversalTicks";
                }

                if (tr.DueAt != tr.PhaseEnteredAt + traversal || tr.PhaseEnteredAt > t)
                {
                    return "Taxiing PhaseEnteredAt/DueAt";
                }
            }
            else if (tr.EdgeProgress != Fx.Zero)
            {
                return "EdgeProgress nonzero off an edge";
            }

            switch (tr.Phase)
            {
                case AircraftLegPhase.AwaitingApproach:
                    if (!arrival || tr.AtNode.HasValue || tr.OnEdge.HasValue || tr.Stand.HasValue)
                    {
                        return "AwaitingApproach: off-graph, no stand";
                    }

                    return tr.PhaseEnteredAt == s.Inbound && tr.DueAt == fr.ScheduledTick ? string.Empty : "AwaitingApproach: entered at InboundAirborne, due at STA";
                case AircraftLegPhase.HeldForRunway:
                    if (arrival)
                    {
                        if (tr.AtNode.HasValue || tr.OnEdge.HasValue || tr.Stand.HasValue)
                        {
                            return "arrival HeldForRunway: off-graph, no stand";
                        }

                        return tr.PhaseEnteredAt == fr.ScheduledTick && tr.DueAt == none ? string.Empty : "arrival HeldForRunway: entered at STA, DueAt unscheduled";
                    }

                    if (tr.OnEdge.HasValue || tr.AtNode != new TaxiNodeId(FixtureLayout.Threshold) || !tr.Stand.HasValue)
                    {
                        return "departure HeldForRunway: at the threshold, Stand kept";
                    }

                    return tr.PhaseEnteredAt == s.RunwayHold && tr.DueAt == none ? string.Empty : "departure HeldForRunway: entered when it reached the threshold";
                case AircraftLegPhase.OnRunway:
                    if (tr.AtNode.HasValue || tr.OnEdge.HasValue || tr.Stand.HasValue != !arrival)
                    {
                        return "OnRunway: off-graph; Stand unset (arrival) or kept (departure)";
                    }

                    ulong? entered = arrival ? s.Landed : s.TakeoffRoll;
                    return tr.PhaseEnteredAt == entered && tr.DueAt == entered + FixtureLayout.OccupancyTicks ? string.Empty : "OnRunway: entered at the movement, due after OccupancyTicks";
                case AircraftLegPhase.HeldOnTaxiway:
                    if (tr.OnEdge.HasValue || !tr.AtNode.HasValue || tr.DueAt != none)
                    {
                        return "HeldOnTaxiway: at a node, DueAt unscheduled";
                    }

                    if (arrival && !tr.Stand.HasValue)
                    {
                        // Waiting for a stand at its threshold.
                        return tr.AtNode == new TaxiNodeId(FixtureLayout.Threshold) && tr.PhaseEnteredAt == s.OffRunway ? string.Empty : "waiting for a stand: at the threshold since OffRunway";
                    }

                    // Held for an edge; an arrival that waited for a stand keeps its OffRunway tick.
                    bool keptOffRunway = arrival && s.WaitedForStand && s.StandAssigned == s.TaxiHold;
                    ulong? expected = keptOffRunway ? s.OffRunway : s.TaxiHold;
                    return tr.PhaseEnteredAt == expected ? string.Empty : "held for an edge: PhaseEnteredAt";
                case AircraftLegPhase.Taxiing:
                    return tr.OnEdge.HasValue && tr.AtNode.HasValue && tr.Stand.HasValue ? string.Empty : "Taxiing: on an edge with its entry node and Stand";
                case AircraftLegPhase.OnStand:
                    if (tr.OnEdge.HasValue || !tr.Stand.HasValue || tr.AtNode != new TaxiNodeId(FixtureLayout.StandNode(tr.Stand.Value.Value)))
                    {
                        return "OnStand: at its stand's node";
                    }

                    if (tr.PhaseEnteredAt != s.OnStand)
                    {
                        return "OnStand: entered at its OnStand";
                    }

                    if (arrival)
                    {
                        ulong due;
                        if (!s.DoorsOpen.HasValue)
                        {
                            due = s.OnStand!.Value + AirConst.FixtureDoorDelayTicks;
                        }
                        else if (fr.HasRotation)
                        {
                            due = s.DoorsOpen.Value + ((ulong)Fx.Floor(fr.MinTurnaround) * AirConst.TicksPerMinute);
                        }
                        else
                        {
                            due = none;
                        }

                        return tr.DueAt == due ? string.Empty : "arrival OnStand: DueAt";
                    }

                    ulong depDue = tr.PassengerHoldSince != none ? tr.PassengerHoldSince + holdTicks : none;
                    return tr.DueAt == depDue ? string.Empty : "departure OnStand: DueAt is the hold deadline, else unscheduled";
                default:
                    return "reserved phase " + tr.Phase + " is never set at Phase 0/1";
            }
        }
    }
}
