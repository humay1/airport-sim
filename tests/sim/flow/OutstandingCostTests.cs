using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// TryGetOutstanding's cost rule (09 §9.7a): O(the flight's cohorts),
    /// served from the per-flight index; it never scans all nodes or all
    /// cohorts, and it does not allocate. The flight under test has the same
    /// single cohort in a small sim and in a large one that adds about 200
    /// nodes and 20000 cohorts of other flights. These tests pin the result
    /// and the allocation rule; the scan rule is enforced at review, since
    /// through the public API only a timing test could observe it.
    /// </summary>
    public sealed class OutstandingCostTests
    {
        private const uint ExtraSources = 200;
        private const int FlightsPerSource = 100;
        private static readonly FlightId Probe = new FlightId(1);

        /// <summary>Source 1 -> Queue 2 -> Gate 3 -> Sink 4; lane closed, so nothing moves.</summary>
        private static Rig Small()
        {
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One)), recordEvents: false);
            rig.InjectNow(1, 4, Probe.Value);
            return rig;
        }

        /// <summary>
        /// The small graph plus Sources 100..299, each feeding Queue 2, each
        /// holding FlightsPerSource one-flight cohorts of other flights
        /// (distinct keys, so none merge, §9.3). Flight 1 holds the same
        /// single cohort on Source 1 as in Small().
        /// </summary>
        private static Rig Large()
        {
            TestGraph g = new TestGraph()
                .Node(1, "source").Queue(2, 1, 0, FlowKit.Lane).Node(3, "gate").Node(4, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4);
            for (uint s = 100; s < 100 + ExtraSources; s++)
            {
                g.Node(s, "source").Edge(s, 2);
            }

            var rig = Rig.Create(g, Graphs.Content(Graphs.Lane(Fx.One)), recordEvents: false);
            ulong flight = 1000;
            for (uint s = 100; s < 100 + ExtraSources; s++)
            {
                for (int k = 0; k < FlightsPerSource; k++)
                {
                    rig.InjectNow(s, 3, flight++);
                }
            }

            rig.InjectNow(1, 4, Probe.Value);
            return rig;
        }

        private static int LiveCohorts(Rig rig)
        {
            int live = 0;
            var nodes = rig.World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                live += rig.Flow.CohortsAt(nodes[i]).Count;
            }

            return live;
        }

        [Fact]
        public void test_outstanding_per_flight_result_unchanged_by_unrelated_nodes_and_cohorts()
        {
            Rig small = Small();
            Rig large = Large();
            Assert.Equal(1 + (int)ExtraSources * FlightsPerSource, LiveCohorts(large));

            Assert.True(small.Flow.TryGetOutstanding(Probe, out OutstandingPassengers a));
            Assert.True(large.Flow.TryGetOutstanding(Probe, out OutstandingPassengers b));
            Assert.Equal(Probe, a.Flight);
            Assert.Equal(4, a.Count);
            Assert.Equal(new NodeId(1), a.MostHeldAt);
            Assert.Equal(a.Flight, b.Flight);
            Assert.Equal(a.Count, b.Count);
            Assert.Equal(a.MostHeldAt, b.MostHeldAt);

            // A flight present only among the unrelated cohorts is counted alone.
            Assert.True(large.Flow.TryGetOutstanding(new FlightId(1000), out OutstandingPassengers other));
            Assert.Equal(3, other.Count);
            Assert.Equal(new NodeId(100), other.MostHeldAt);
            Assert.False(large.Flow.TryGetOutstanding(new FlightId(999), out _));
        }

        [Fact]
        public void test_outstanding_query_allocates_nothing_among_many_cohorts()
        {
            // §9.7a: "it does not allocate".
            Rig large = Large();
            large.Flow.TryGetOutstanding(Probe, out _);
            large.Flow.TryGetOutstanding(new FlightId(999), out _);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                large.Flow.TryGetOutstanding(Probe, out _);
                large.Flow.TryGetOutstanding(new FlightId(999), out _);
            }

            long after = System.GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0L, after - before);
        }
    }
}
