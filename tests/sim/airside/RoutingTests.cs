using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.4 "Routing": threshold to stand is the least-TraversalTicks
    /// path, ties broken by ascending TaxiEdgeId at the first diverging edge.
    /// Two routes T -> S: via Ja on edges 5 then 1, via Jb on edges 3 then 9.
    /// The via-Ja route holds the lowest id overall (1), the via-Jb route the
    /// lowest id at the divergence (3), so only the pinned rule picks Jb on a
    /// tie.
    /// </summary>
    public sealed class RoutingTests
    {
        private static AirsideLayout TwoRoutes(uint ecTicks)
        {
            return new LayoutBuilder()
                .Runway(1, 1, 15, 10)
                .Node(1, TaxiNodeKind.RunwayThreshold)
                .Node(2, TaxiNodeKind.Junction)
                .Node(3, TaxiNodeKind.Junction)
                .Node(11, TaxiNodeKind.StandPosition)
                .Edge(5, 1, 2, 10)
                .Edge(1, 2, 11, 10)
                .Edge(3, 1, 3, ecTicks)
                .Edge(9, 3, 11, 10)
                .Stand(1, 11, AirsideContent.Super, 901)
                .Load();
        }

        private static List<ushort> EdgesTaken(HostRig rig, ulong flight)
        {
            var edges = new List<ushort>();
            rig.StepEach(3800UL, t =>
            {
                if (rig.Airside.TryGetTrack(new FlightId(flight), out AircraftTrack tr) && tr.OnEdge.HasValue)
                {
                    ushort e = tr.OnEdge.Value.Value;
                    if (edges.Count == 0 || edges[edges.Count - 1] != e)
                    {
                        edges.Add(e);
                    }
                }
            });
            return edges;
        }

        [Fact]
        public void test_routing_takes_least_traversal_ticks_route()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")), layout: TwoRoutes(15));
            ulong a1 = rig.Id("A1");
            Assert.Equal(new List<ushort> { 5, 1 }, EdgesTaken(rig, a1));
            Assert.Equal(AirConst.At(6, 0) + 10UL + 20UL, rig.Rec.Milestone(a1, FlightMilestone.OnStand).Milestone.PlannedTick);
        }

        [Fact]
        public void test_routing_breaks_ties_by_lowest_edge_id_at_first_divergence()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("A1", "A", "06:00")), layout: TwoRoutes(10));
            ulong a1 = rig.Id("A1");
            Assert.Equal(new List<ushort> { 3, 9 }, EdgesTaken(rig, a1));
            Assert.Equal(AirConst.At(6, 0) + 10UL + 20UL, rig.Rec.Milestone(a1, FlightMilestone.OnStand).Milestone.PlannedTick);
        }
    }
}
