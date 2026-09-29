using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// Interface conformance for IAirsideSystem (12 §12.9) and its
    /// construction (§12.12a): identity, registry position, the queries on a
    /// fresh system, Layout(), and the ReassignStand handler registered
    /// through SystemServices.Commands (§12.10).
    /// </summary>
    public sealed class AirsideSystemTests
    {
        private static HostRig Rig()
        {
            return new HostRig(Csv.Of(Csv.Row("X1", "A", "12:00")));
        }

        [Fact]
        public void test_airside_system_is_registry_position_three_named_after_module()
        {
            HostRig rig = Rig();
            Assert.Equal(AirConst.AirsideSystemId, rig.Airside.Id.Value);
            Assert.Equal("sim.airside", rig.Airside.Name);
        }

        [Fact]
        public void test_airside_system_fresh_queries_report_empty_airfield()
        {
            HostRig rig = Rig();
            Assert.Empty(rig.Airside.TrackedFlights());
            Assert.Equal(new List<ushort> { 1, 2, 3, 4 }, rig.Free());
            Assert.Equal(0, rig.Airside.RunwayQueueLength(new RunwayId(FixtureLayout.Runway)));
            Assert.False(rig.Airside.TryGetTrack(new FlightId(rig.Id("X1")), out _));
            Assert.False(rig.Airside.TryGetTrack(new FlightId(987654UL), out _));
            Assert.False(rig.Airside.TryGetStand(new StandId(99), out _));
            for (ushort s = FixtureLayout.S1; s <= FixtureLayout.S4; s++)
            {
                Assert.False(rig.Occupant(s).HasValue);
            }

            rig.RunTo(100);
            Assert.Empty(rig.Airside.TrackedFlights());
            Assert.Equal(new List<ushort> { 1, 2, 3, 4 }, rig.Free());
        }

        [Fact]
        public void test_airside_system_layout_returns_the_validated_layout()
        {
            AirsideLayout loaded = FixtureLayout.Layout();
            var rig = new HostRig(Csv.Of(Csv.Row("X1", "A", "12:00")), layout: loaded);
            AirsideLayout seen = rig.Airside.Layout();

            Assert.Equal(loaded.Runways.Count, seen.Runways.Count);
            Assert.Equal(loaded.Nodes.Count, seen.Nodes.Count);
            Assert.Equal(loaded.Edges.Count, seen.Edges.Count);
            Assert.Equal(loaded.Stands.Count, seen.Stands.Count);
            for (int i = 0; i < loaded.Runways.Count; i++)
            {
                Assert.Equal(loaded.Runways[i].Id, seen.Runways[i].Id);
                Assert.Equal(loaded.Runways[i].ThresholdNode, seen.Runways[i].ThresholdNode);
                Assert.Equal(loaded.Runways[i].DeclaredCapacityPerHour, seen.Runways[i].DeclaredCapacityPerHour);
                Assert.Equal(loaded.Runways[i].OccupancyTicks, seen.Runways[i].OccupancyTicks);
            }

            for (int i = 0; i < loaded.Nodes.Count; i++)
            {
                Assert.Equal(loaded.Nodes[i].Id, seen.Nodes[i].Id);
                Assert.Equal(loaded.Nodes[i].Kind, seen.Nodes[i].Kind);
            }

            for (int i = 0; i < loaded.Edges.Count; i++)
            {
                Assert.Equal(loaded.Edges[i].Id, seen.Edges[i].Id);
                Assert.Equal(loaded.Edges[i].From, seen.Edges[i].From);
                Assert.Equal(loaded.Edges[i].To, seen.Edges[i].To);
                Assert.Equal(loaded.Edges[i].TraversalTicks, seen.Edges[i].TraversalTicks);
                Assert.Equal(loaded.Edges[i].Bidirectional, seen.Edges[i].Bidirectional);
            }

            for (int i = 0; i < loaded.Stands.Count; i++)
            {
                Assert.Equal(loaded.Stands[i].Id, seen.Stands[i].Id);
                Assert.Equal(loaded.Stands[i].Node, seen.Stands[i].Node);
                Assert.Equal(loaded.Stands[i].MaxAircraftSizeCategory, seen.Stands[i].MaxAircraftSizeCategory);
                Assert.Equal(loaded.Stands[i].DepartureSinkNode, seen.Stands[i].DepartureSinkNode);
            }

            // Load-time data: unchanged by running (12 §12.9 "immutable").
            rig.RunTo(AirConst.TicksPerDay);
            Assert.Equal(loaded.Stands.Count, rig.Airside.Layout().Stands.Count);
        }

        [Fact]
        public void test_airside_system_registers_reassign_stand_handler()
        {
            // 08 §8.7: with no handler registered the kind is UnknownKind.
            HostRig rig = Rig();
            Assert.True(rig.Submit(Payload.ReassignCommand(5UL, rig.Id("X1"), FixtureLayout.S4), out CommandRejection reason));
            Assert.Equal(CommandRejection.None, reason);
        }
    }
}
