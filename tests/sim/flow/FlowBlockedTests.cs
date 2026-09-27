using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// FlowBlocked / FlowUnblocked (09 §9.5, §9.12 "Blocking episodes", 10 §10.3
    /// rules 1-3): once per episode, paired with identical fields, emitted at the
    /// transition tick, Held = the cohort's node, BlockedBy = the immediate target.
    /// </summary>
    public sealed class FlowBlockedTests
    {
        private static readonly ContentId Up = new ContentId("q_up");
        private static readonly ContentId Down = new ContentId("q_down");

        // Source 1 -> Queue 2 (capacity 3, 60 pax/min per lane, closed) -> Gate 3.
        private static Rig Gated()
        {
            return Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60), 3)));
        }

        private static void StepUntilSourceEmpty(Rig rig)
        {
            int guard = 0;
            while (rig.Pop(1) > 0 && guard++ < 6)
            {
                rig.Step(1);
            }
        }

        [Fact]
        public void test_flow_blocked_once_per_episode_then_unblocked_with_same_fields()
        {
            var rig = Gated();
            rig.InjectNow(1, 3, 1);
            StepUntilSourceEmpty(rig);
            Assert.Equal(3, rig.Pop(2));

            CohortId held = rig.InjectNow(1, 2, 2);
            ulong refusedAt = ulong.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                ulong t = rig.NextTick;
                rig.Step(1);
                if (refusedAt == ulong.MaxValue && rig.Events!.Blocked.Count > 0)
                {
                    refusedAt = t;
                }
            }

            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Blocked);
            Assert.Empty(ev.Unblocked);
            Assert.Equal(held, ev.Blocked[0].E.Cohort);
            Assert.Equal(new NodeId(1), ev.Blocked[0].E.Held);
            Assert.Equal(new NodeId(2), ev.Blocked[0].E.BlockedBy);
            Assert.Equal(refusedAt, ev.Blocked[0].Tick);

            // Open a lane: 6 pax a tick. The queue drains below capacity, and the
            // held cohort is released on the first tick whose snapshot shows room.
            Assert.True(rig.Submit(2, 1, out _));
            ulong releasedAt = ulong.MaxValue;
            for (int i = 0; i < 10 && releasedAt == ulong.MaxValue; i++)
            {
                ulong t = rig.NextTick;
                rig.Step(1);
                if (rig.Pop(1) == 0)
                {
                    releasedAt = t;
                }
            }

            Assert.NotEqual(ulong.MaxValue, releasedAt);
            Assert.Single(ev.Unblocked);
            Assert.Equal(releasedAt, ev.Unblocked[0].Tick);
            Assert.Equal(ev.Blocked[0].E.Cohort, ev.Unblocked[0].E.Cohort);
            Assert.Equal(ev.Blocked[0].E.Held, ev.Unblocked[0].E.Held);
            Assert.Equal(ev.Blocked[0].E.BlockedBy, ev.Unblocked[0].E.BlockedBy);
            Assert.Single(ev.Blocked);
        }

        [Fact]
        public void test_flow_blocked_cohorts_do_not_merge()
        {
            // Q-032: a cohort in an open blocking episode does not merge. Two
            // cohorts of one key held at the source stay two cohorts, each with
            // its own episode.
            var rig = Gated();
            rig.InjectNow(1, 3, 1);
            StepUntilSourceEmpty(rig);
            CohortId first = rig.InjectNow(1, 1, 9);
            rig.Step(3);
            CohortId second = rig.InjectNow(1, 1, 9);
            rig.Step(3);

            List<PassengerCohort> atSource = rig.CohortsOn(1);
            Assert.Equal(2, atSource.Count);
            Assert.Equal(atSource[0].Key, atSource[1].Key);
            Assert.Equal(2, rig.Events!.Blocked.Count);
            Assert.Equal(first, rig.Events.Blocked[0].E.Cohort);
            Assert.Equal(second, rig.Events.Blocked[1].E.Cohort);
        }

        [Fact]
        public void test_flow_blocked_held_on_queue_names_downstream_queue()
        {
            // A served part refused by a full downstream queue: Held is the
            // upstream queue, BlockedBy the immediate target, never further on.
            var g = Graphs.TwoQueues(Up, 1, Down, 0);
            IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.One),
                FlowKit.Queue(Up, Fx.FromInt(60), 100000, Fx.FromInt(1000000), Fx.Zero),
                FlowKit.Queue(Down, Fx.FromInt(60), 2, Fx.FromInt(1000000), Fx.Zero),
            });
            var rig = Rig.Create(g, content);
            rig.InjectNow(1, 2, 1);
            rig.Step(6);
            Assert.Equal(2, rig.Pop(3));
            rig.InjectNow(1, 4, 2);
            rig.Step(10);

            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Blocked);
            Assert.Equal(new NodeId(2), ev.Blocked[0].E.Held);
            Assert.Equal(new NodeId(3), ev.Blocked[0].E.BlockedBy);
            Assert.Equal(4, rig.Pop(2));
        }

        [Fact]
        public void test_flow_blocked_on_corridor_past_due_holds_the_corridor()
        {
            // Source 1 -> Corridor 2 (6 m: one tick) -> Queue 3 (full) -> Gate 4.
            var g = new TestGraph()
                .Node(1, "source").Node(2, "corridor", 6).Queue(3, 1, 0, FlowKit.Lane).Node(4, "gate").Node(5, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4).Edge(4, 5);
            var rig = Rig.Create(g, Graphs.Content(Graphs.Lane(Fx.FromInt(60), 1)));
            rig.InjectNow(1, 1, 1);
            rig.Step(6);
            Assert.Equal(1, rig.Pop(3));
            rig.InjectNow(1, 2, 2);
            rig.Step(10);

            FlowEvents ev = rig.Events!;
            Assert.Equal(2, rig.Pop(2));
            Assert.Single(ev.Blocked);
            Assert.Equal(new NodeId(2), ev.Blocked[0].E.Held);
            Assert.Equal(new NodeId(3), ev.Blocked[0].E.BlockedBy);
        }

        [Fact]
        public void test_flow_blocked_unblocked_precedes_later_nodes_events_in_the_tick()
        {
            // Event order (Q-032): movement events by ascending node, then per
            // cohort Unblocked, Blocked, ArrivedAtGate; threshold events last.
            // Here the release of the cohort held at node 1 (Unblocked) happens in
            // the same tick as queue 2 serving passengers into gate 3 (Arrived):
            // one lane at 10 pax/min serves one a tick, so the queue is below
            // capacity at the start of the release tick yet still serving.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(10), 3)));
            rig.InjectNow(1, 3, 1);
            StepUntilSourceEmpty(rig);
            rig.InjectNow(1, 2, 2);
            rig.Step(4);
            Assert.True(rig.Submit(2, 1, out _));
            rig.Step(6);

            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Unblocked);
            ulong t = ev.Unblocked[0].Tick;
            int unblockedAt = ev.Log.FindIndex(l => l.StartsWith(t + " unblocked"));
            int arrivedAt = ev.Log.FindIndex(l => l.StartsWith(t + " arrived"));
            Assert.True(arrivedAt >= 0, "expected a gate arrival in the release tick " + t + ":\n" + string.Join("\n", ev.Log));
            Assert.True(unblockedAt < arrivedAt, string.Join("\n", ev.Log));
        }
    }
}
