using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.5, exact per §9.12 (Q-032): only a Queue has a capacity; it is
    /// full when its start-of-tick population is >= CapacityStanding; cohorts
    /// are admitted whole or refused.
    /// </summary>
    public sealed class SpillbackTests
    {
        // A queue that never serves (rate 0) with room for 3 standing.
        private static Rig Closed(int capacity = 3)
        {
            return Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.Zero, capacity)));
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
        public void test_spillback_uses_start_of_tick_population_and_admits_whole_cohorts()
        {
            var rig = Closed(3);
            rig.InjectNow(1, 2, 1);
            StepUntilSourceEmpty(rig);
            Assert.Equal(2, rig.Pop(2));

            // Two cohorts released in the same tick both see population 2 < 3 in
            // the snapshot, so both are admitted whole: 6 standing, not 3 or 4.
            rig.InjectNow(1, 2, 2);
            rig.InjectNow(1, 2, 3);
            StepUntilSourceEmpty(rig);
            Assert.Equal(0, rig.Pop(1));
            Assert.Equal(6, rig.Pop(2));
            Assert.Empty(rig.Events!.Blocked);
        }

        [Fact]
        public void test_spillback_full_at_capacity_refuses_and_holds_upstream()
        {
            var rig = Closed(3);
            rig.InjectNow(1, 3, 1);
            StepUntilSourceEmpty(rig);
            Assert.Equal(3, rig.Pop(2));

            // Population 3 >= CapacityStanding 3: full. The cohort stays upstream,
            // whole, and nothing vanishes.
            rig.InjectNow(1, 1, 2);
            rig.Step(50);
            Assert.Equal(1, rig.Pop(1));
            Assert.Equal(3, rig.Pop(2));
            Assert.Single(rig.Events!.Blocked);
        }

        [Fact]
        public void test_spillback_never_fills_a_non_queue_node()
        {
            var g = new TestGraph()
                .Node(1, "source").Node(2, "hall").Node(3, "corridor", 600).Node(4, "gate").Node(5, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4).Edge(4, 5);
            var rig = Rig.Create(g, Graphs.Content());
            for (int i = 0; i < 20; i++)
            {
                rig.InjectNow(1, 5000, (ulong)(i + 1));
                rig.Step(1);
            }

            rig.Step(5);
            Assert.Equal(100000, rig.Pop(3));
            Assert.Empty(rig.Events!.Blocked);
        }

        [Fact]
        public void test_spillback_backs_pressure_up_through_a_queue()
        {
            // Downstream queue 3 is full (capacity 2, never serves); upstream queue
            // 2 has credit but serves nobody into it, and its population holds.
            var g = Graphs.TwoQueues(new ContentId("q_up"), 1, new ContentId("q_down"), 1);
            IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.One),
                FlowKit.Queue(new ContentId("q_up"), Fx.FromInt(60), 100000, Fx.FromInt(1000000), Fx.Zero),
                FlowKit.Queue(new ContentId("q_down"), Fx.Zero, 2, Fx.FromInt(1000000), Fx.Zero),
            });
            var rig = Rig.Create(g, content);
            rig.InjectNow(1, 2, 1);
            rig.Step(6);
            Assert.Equal(2, rig.Pop(3));

            rig.InjectNow(1, 9, 2);
            rig.Step(30);
            Assert.Equal(9, rig.Pop(2));
            Assert.Equal(2, rig.Pop(3));
        }
    }
}
