using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 07 L10 applied to 12 §12.4 and §12.9: each struct has one public
    /// constructor taking its members in declared order, and each enum is
    /// numbered from 0 in declared order. Distinct values per member catch a
    /// swapped pair of same-typed members, which compiling alone cannot.
    /// </summary>
    public sealed class AirsideTypesTests
    {
        [Fact]
        public void test_airside_types_enums_are_numbered_in_declared_order()
        {
            Assert.Equal(0, (int)TaxiNodeKind.RunwayThreshold);
            Assert.Equal(1, (int)TaxiNodeKind.Junction);
            Assert.Equal(2, (int)TaxiNodeKind.StandPosition);
            Assert.Equal(3, Enum.GetValues(typeof(TaxiNodeKind)).Length);

            Assert.Equal(0, (int)AircraftLegPhase.AwaitingApproach);
            Assert.Equal(1, (int)AircraftLegPhase.HeldForRunway);
            Assert.Equal(2, (int)AircraftLegPhase.OnRunway);
            Assert.Equal(3, (int)AircraftLegPhase.HeldOnTaxiway);
            Assert.Equal(4, (int)AircraftLegPhase.Taxiing);
            Assert.Equal(5, (int)AircraftLegPhase.OnStand);
            Assert.Equal(6, (int)AircraftLegPhase.AwaitingPushbackClearance);
            Assert.Equal(7, (int)AircraftLegPhase.Departed);
            Assert.Equal(8, Enum.GetValues(typeof(AircraftLegPhase)).Length);
        }

        [Fact]
        public void test_airside_types_layout_structs_take_members_in_declared_order()
        {
            var r = new RunwayDef(new RunwayId(7), new TaxiNodeId(8), 270, 32, 11U);
            Assert.Equal(7, r.Id.Value);
            Assert.Equal(8, r.ThresholdNode.Value);
            Assert.Equal(270, r.ActiveDirectionDeg);
            Assert.Equal(32, r.DeclaredCapacityPerHour);
            Assert.Equal(11U, r.OccupancyTicks);

            var n = new TaxiNodeDef(new TaxiNodeId(9), TaxiNodeKind.StandPosition);
            Assert.Equal(9, n.Id.Value);
            Assert.Equal(TaxiNodeKind.StandPosition, n.Kind);

            var e = new TaxiEdgeDef(new TaxiEdgeId(3), new TaxiNodeId(4), new TaxiNodeId(5), 60U, true);
            Assert.Equal(3, e.Id.Value);
            Assert.Equal(4, e.From.Value);
            Assert.Equal(5, e.To.Value);
            Assert.Equal(60U, e.TraversalTicks);
            Assert.True(e.Bidirectional);

            var s = new StandDef(new StandId(12), new TaxiNodeId(13), new ContentId("medium"), new NodeId(901U));
            Assert.Equal(12, s.Id.Value);
            Assert.Equal(13, s.Node.Value);
            Assert.Equal("medium", s.MaxAircraftSizeCategory.Value);
            Assert.Equal(901U, s.DepartureSinkNode.Value);

            var runways = new[] { r };
            var nodes = new[] { n };
            var edges = new[] { e };
            var stands = new[] { s };
            var layout = new AirsideLayout(runways, nodes, edges, stands);
            Assert.Equal(7, layout.Runways[0].Id.Value);
            Assert.Equal(9, layout.Nodes[0].Id.Value);
            Assert.Equal(3, layout.Edges[0].Id.Value);
            Assert.Equal(12, layout.Stands[0].Id.Value);

            var rules = new AirsideRules(10U, 2U);
            Assert.Equal(10U, rules.BoardingHoldMaxMinutes);
            Assert.Equal(2U, rules.DoorsOpenDelayMinutes);
        }

        [Fact]
        public void test_airside_types_track_and_stand_state_take_members_in_declared_order()
        {
            var t = new AircraftTrack(
                new FlightId(100001UL),
                MovementKind.Departure,
                AircraftLegPhase.Taxiing,
                new TaxiNodeId(2),
                new TaxiEdgeId(3),
                Fx.FromRatio(1, 2),
                new StandId(4),
                new RunwayId(5),
                600UL,
                700UL,
                800UL,
                new EventRef(new EventId(900UL, 9U), true));
            Assert.Equal(100001UL, t.Flight.Value);
            Assert.Equal(MovementKind.Departure, t.Kind);
            Assert.Equal(AircraftLegPhase.Taxiing, t.Phase);
            Assert.Equal((ushort)2, t.AtNode!.Value.Value);
            Assert.Equal((ushort)3, t.OnEdge!.Value.Value);
            Assert.Equal(Fx.FromRatio(1, 2), t.EdgeProgress);
            Assert.Equal((ushort)4, t.Stand!.Value.Value);
            Assert.Equal((ushort)5, t.Runway!.Value.Value);
            Assert.Equal(600UL, t.PhaseEnteredAt);
            Assert.Equal(700UL, t.DueAt);
            Assert.Equal(800UL, t.PassengerHoldSince);
            Assert.True(t.RecordedCause.HasValue);
            Assert.Equal(new EventId(900UL, 9U), t.RecordedCause.Id);

            var none = new AircraftTrack(new FlightId(1UL), MovementKind.Arrival, AircraftLegPhase.AwaitingApproach, null, null, Fx.Zero, null, null, 0UL, AirConst.TickUnscheduled, AirConst.TickUnscheduled, EventRef.None);
            Assert.False(none.RecordedCause.HasValue);
            Assert.False(none.AtNode.HasValue);
            Assert.False(none.OnEdge.HasValue);
            Assert.False(none.Stand.HasValue);
            Assert.False(none.Runway.HasValue);

            var st = new StandState(new StandId(6), new FlightId(42UL));
            Assert.Equal(6, st.Id.Value);
            Assert.Equal(42UL, st.Occupant!.Value.Value);
            Assert.False(new StandState(new StandId(6), null).Occupant.HasValue);
        }
    }
}
