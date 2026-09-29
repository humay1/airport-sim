using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-039: 09 §9.7 "Exact rules" (Q-033) check 4, added by Q-040 (§9.6):
    /// at Phase 0/1 only Departing cohorts exist, so Inject rejects
    /// key.Direction != Departing with ArgumentException, after the at
    /// (Source) and count checks, and changes nothing on the throw.
    /// </summary>
    public sealed class InjectDirectionRejectionTests
    {
        private static Rig Line()
        {
            return Rig.Create(Graphs.Line(3, 2, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(2))));
        }

        /// <summary>
        /// Every node's Population and CohortsAt must match between the
        /// control rig (which never made the rejected calls) and the test
        /// rig (which attempted them), demonstrating a throw changes
        /// nothing (09 §9.7 "Changes nothing is asserted").
        /// </summary>
        private static void AssertSameNodeState(Rig control, Rig test)
        {
            IReadOnlyList<NodeId> nodes = control.World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeId n = nodes[i];
                Assert.Equal(control.Flow.Population(n), test.Flow.Population(n));
                Assert.Equal(control.Flow.CohortsAt(n), test.Flow.CohortsAt(n));
            }
        }

        /// <summary>
        /// PopulationForFlight takes no NodeId, so the rejected keys' flight
        /// is checked in all three FlowDirection values instead of per node.
        /// </summary>
        private static void AssertSamePopulationForFlight(Rig control, Rig test, FlightId flight)
        {
            Assert.Equal(control.Flow.PopulationForFlight(flight, FlowDirection.Departing), test.Flow.PopulationForFlight(flight, FlowDirection.Departing));
            Assert.Equal(control.Flow.PopulationForFlight(flight, FlowDirection.Arriving), test.Flow.PopulationForFlight(flight, FlowDirection.Arriving));
            Assert.Equal(control.Flow.PopulationForFlight(flight, FlowDirection.Transferring), test.Flow.PopulationForFlight(flight, FlowDirection.Transferring));
        }

        [Fact]
        public void test_inject_rejects_non_departing_direction()
        {
            // Control rig: only ever makes the valid, Departing calls. Test
            // rig: makes the same valid calls, plus the two rejected ones in
            // between. Everything queryable must match, including the
            // CohortId the next valid Inject returns -- proof the
            // IIdAllocator counter was not advanced by a rejected call.
            var control = Line();
            var test = Line();

            CohortKey validA = FlowKit.Key(1);
            CohortId controlFirst = control.Flow.Inject(validA, 5, new NodeId(1));
            CohortId testFirst = test.Flow.Inject(validA, 5, new NodeId(1));
            Assert.Equal(controlFirst, testFirst);
            AssertSameNodeState(control, test);

            // Case 1: Arriving, valid Source, count > 0 -- ArgumentException,
            // never ArgumentOutOfRangeException (checks 1-3 pass first).
            // Both rejected keys share validA's flight, so the Departing
            // PopulationForFlight comparison is over a non-zero count.
            var flight = new FlightId(1);
            CohortKey arriving = FlowKit.Key(1, direction: FlowDirection.Arriving);
            Assert.Throws<ArgumentException>(() => test.Flow.Inject(arriving, 5, new NodeId(1)));
            AssertSameNodeState(control, test);
            AssertSamePopulationForFlight(control, test, flight);

            // Case 2: Transferring, valid Source, count > 0 -- ArgumentException.
            CohortKey transferring = FlowKit.Key(1, direction: FlowDirection.Transferring);
            Assert.Throws<ArgumentException>(() => test.Flow.Inject(transferring, 7, new NodeId(1)));
            AssertSameNodeState(control, test);
            AssertSamePopulationForFlight(control, test, flight);

            // The next valid, Departing Inject must return the same CohortId
            // in both rigs.
            CohortKey validB = FlowKit.Key(4);
            CohortId controlSecond = control.Flow.Inject(validB, 3, new NodeId(1));
            CohortId testSecond = test.Flow.Inject(validB, 3, new NodeId(1));
            Assert.Equal(controlSecond, testSecond);
            AssertSameNodeState(control, test);
            AssertSamePopulationForFlight(control, test, flight);
        }
    }
}
