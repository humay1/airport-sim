using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// Absorb's event order over several blocked missed cohorts (09 §9.7
    /// "Exact rules", Q-033): "For each missed cohort in an open blocking
    /// episode, in ascending CohortId, it publishes FlowUnblocked. Then, if
    /// the missed count is above 0, it publishes one PassengersMissedFlight".
    /// The order is ascending CohortId across every missed cohort, whatever
    /// node each is on. The fixture makes the CohortIds interleave against
    /// NodeId order and puts two blocked cohorts on each of two nodes, so any
    /// per-node order (ascending or descending within a node) differs from it.
    /// </summary>
    public sealed class AbsorbOrderTests
    {
        private const uint SourceA = 1;
        private const uint SourceB = 2;
        private const uint Lane = 3;
        private const uint Gate = 4;
        private const uint Sink = 5;

        /// <summary>Sources 1 and 2 -> Queue 3 (closed, capacity 1) -> Gate 4 -> Sink 5.</summary>
        private static Rig Create()
        {
            TestGraph g = new TestGraph()
                .Node(SourceA, "source").Node(SourceB, "source").Queue(Lane, 1, 0, FlowKit.Lane)
                .Node(Gate, "gate").Node(Sink, "sink")
                .Edge(SourceA, Lane).Edge(SourceB, Lane).Edge(Lane, Gate).Edge(Gate, Sink);
            return Rig.Create(g, Graphs.Content(Graphs.Lane(Fx.FromInt(60), 1)));
        }

        /// <summary>
        /// Flight 1: one passenger fills the closed lane, then four cohorts of
        /// distinct keys (so none may merge, §9.3) are injected in the order
        /// SourceB, SourceA, SourceB, SourceA, each blocked by the full lane.
        /// Held counts: SourceA 2 + 3 = 5, SourceB 4 + 1 = 5, Lane 1: 11
        /// missed, with a tie between SourceA and SourceB.
        /// </summary>
        private static List<CohortId> Arrange(Rig rig)
        {
            rig.InjectNow(SourceA, 1, 1);
            rig.Step(3);
            Assert.Equal(1, rig.Pop(Lane));

            var plan = new (uint Source, int Count, bool Bags, bool Assist)[]
            {
                (SourceB, 4, false, false),
                (SourceA, 2, true, false),
                (SourceB, 1, false, true),
                (SourceA, 3, true, true),
            };
            var ids = new List<CohortId>();
            foreach (var p in plan)
            {
                ids.Add(rig.Flow.Inject(FlowKit.Key(1, bags: p.Bags, assist: p.Assist), p.Count, new NodeId(p.Source)));
                rig.Step(2);
            }

            Assert.Equal(5, rig.Pop(SourceA));
            Assert.Equal(5, rig.Pop(SourceB));
            Assert.Equal(1, rig.Pop(Lane));

            // Each injected cohort is in its own open episode, held on its
            // source by the lane.
            FlowEvents ev = rig.Events!;
            Assert.Empty(ev.Unblocked);
            Assert.Equal(4, ev.Blocked.Count);
            var blocked = new HashSet<CohortId>();
            foreach (var b in ev.Blocked)
            {
                Assert.Equal(new NodeId(Lane), b.E.BlockedBy);
                blocked.Add(b.E.Cohort);
            }

            Assert.True(blocked.SetEquals(ids), "the blocked cohorts are not the injected ones");
            for (int i = 0; i < ids.Count; i++)
            {
                Assert.True(rig.Flow.TryGetCohort(ids[i], out PassengerCohort c));
                Assert.Equal(new NodeId(plan[i].Source), c.Node);
            }

            // Precondition: the ids interleave against NodeId order, so neither
            // per-node order equals ascending CohortId.
            var ascending = new List<CohortId>(ids);
            ascending.Sort((x, y) => x.Value.CompareTo(y.Value));
            var byNodeAsc = new List<CohortId>();
            var byNodeDesc = new List<CohortId>();
            foreach (uint node in new[] { SourceA, SourceB })
            {
                var onNode = new List<CohortId>();
                foreach (CohortId id in ascending)
                {
                    rig.Flow.TryGetCohort(id, out PassengerCohort c);
                    if (c.Node == new NodeId(node))
                    {
                        onNode.Add(id);
                    }
                }

                Assert.Equal(2, onNode.Count);
                byNodeAsc.AddRange(onNode);
                onNode.Reverse();
                byNodeDesc.AddRange(onNode);
            }

            Assert.NotEqual(ascending, byNodeAsc);
            Assert.NotEqual(ascending, byNodeDesc);
            return ascending;
        }

        [Fact]
        public void test_absorb_unblocks_missed_cohorts_in_ascending_cohort_id_across_nodes()
        {
            Rig rig = Create();
            List<CohortId> ascending = Arrange(rig);
            FlowEvents ev = rig.Events!;
            var episodes = new Dictionary<CohortId, (NodeId Held, NodeId By)>();
            foreach (var b in ev.Blocked)
            {
                episodes[b.E.Cohort] = (b.E.Held, b.E.BlockedBy);
            }

            int logBefore = ev.Log.Count;
            ulong t = rig.NextTick;
            int boarded = -1;
            rig.Inject = (in TickContext ctx) => boarded = rig.Flow.Absorb(new NodeId(Sink), new FlightId(1));
            rig.Step(1);
            rig.Inject = null;

            Assert.Equal(0, boarded);

            // One FlowUnblocked per blocked missed cohort, strictly ascending
            // CohortId, each closing that cohort's episode with its fields.
            Assert.Equal(ascending.Count, ev.Unblocked.Count);
            var order = new List<ulong>();
            foreach (var u in ev.Unblocked)
            {
                order.Add(u.E.Cohort.Value);
            }

            string seen = string.Join(", ", order);
            for (int i = 0; i < ascending.Count; i++)
            {
                var u = ev.Unblocked[i];
                Assert.True(u.E.Cohort == ascending[i], "FlowUnblocked order " + seen + ", expected ascending CohortId");
                Assert.Equal(t, u.Tick);
                Assert.Equal(episodes[u.E.Cohort].Held, u.E.Held);
                Assert.Equal(episodes[u.E.Cohort].By, u.E.BlockedBy);
            }

            // Then exactly one PassengersMissedFlight, last of Absorb's events,
            // with the missed total. LastBlockedAt: SourceA and SourceB hold 5
            // each, a tie broken by ascending NodeId (§9.7a's MostHeldAt rule).
            Assert.Single(ev.Missed);
            Assert.Equal(t, ev.Missed[0].Tick);
            Assert.Equal(new FlightId(1), ev.Missed[0].E.Flight);
            Assert.Equal(11, ev.Missed[0].E.Count);
            Assert.Equal(new NodeId(SourceA), ev.Missed[0].E.LastBlockedAt);

            List<string> absorbLog = ev.Log.GetRange(logBefore, ev.Log.Count - logBefore);
            string log = string.Join("\n", absorbLog);
            Assert.Equal(ascending.Count + 1, absorbLog.Count);
            for (int i = 0; i < ascending.Count; i++)
            {
                Assert.True(absorbLog[i].StartsWith(t + " unblocked c" + ascending[i].Value + " ", StringComparison.Ordinal), log);
            }

            Assert.True(absorbLog[ascending.Count].StartsWith(t + " missed f1 x11 ", StringComparison.Ordinal), log);

            // The missed cohorts are gone.
            foreach (CohortId id in ascending)
            {
                Assert.False(rig.Flow.TryGetCohort(id, out _));
            }

            Assert.Equal(0, FlowKit.TotalPopulation(rig.Flow, rig.World));
            Assert.False(rig.Flow.TryGetOutstanding(new FlightId(1), out _));
        }

        [Fact]
        public void test_absorb_outstanding_tie_matches_last_blocked_at()
        {
            // §9.7's LastBlockedAt is §9.7a's MostHeldAt rule: before Absorb,
            // TryGetOutstanding names the node Absorb then reports.
            Rig rig = Create();
            Arrange(rig);
            Assert.True(rig.Flow.TryGetOutstanding(new FlightId(1), out OutstandingPassengers o));
            Assert.Equal(11, o.Count);
            Assert.Equal(new NodeId(SourceA), o.MostHeldAt);

            rig.Inject = (in TickContext ctx) => rig.Flow.Absorb(new NodeId(Sink), new FlightId(1));
            rig.Step(1);
            rig.Inject = null;
            Assert.Single(rig.Events!.Missed);
            Assert.Equal(o.MostHeldAt, rig.Events.Missed[0].E.LastBlockedAt);
        }

        [Fact]
        public void test_absorb_outside_a_tick_with_blocked_missed_cohorts_changes_nothing()
        {
            // Q-033: Absorb publishes before it changes any state, so outside
            // phases 1-3 its first Publish (a FlowUnblocked here) throws
            // InvalidOperationException and nothing has changed: every episode
            // stays open and every cohort stays where it was.
            Rig rig = Create();
            List<CohortId> ascending = Arrange(rig);
            ulong hash = rig.Flow.ComputeStateHash();
            int events = rig.Events!.Log.Count;

            Assert.Throws<InvalidOperationException>(() => rig.Flow.Absorb(new NodeId(Sink), new FlightId(1)));
            Assert.Equal(hash, rig.Flow.ComputeStateHash());
            foreach (CohortId id in ascending)
            {
                Assert.True(rig.Flow.TryGetCohort(id, out _), "cohort " + id.Value + " removed by a failed Absorb");
            }

            Assert.Equal(5, rig.Pop(SourceA));
            Assert.Equal(5, rig.Pop(SourceB));
            Assert.Equal(1, rig.Pop(Lane));
            Assert.True(rig.Flow.TryGetOutstanding(new FlightId(1), out OutstandingPassengers o));
            Assert.Equal(11, o.Count);

            // The episodes are still open: the next tick emits nothing.
            rig.Step(1);
            Assert.Equal(events, rig.Events.Log.Count);
            Assert.Empty(rig.Events.Unblocked);
        }
    }
}
