using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.4 step 3: served FIFO by (EnteredNodeAt, CohortId), and per-cohort
    /// service never depends on cohort size; §9.3 split keeps the id on the
    /// remainder.
    /// </summary>
    public sealed class QueueFifoTests
    {
        private static bool ReachedGate(Rig rig, ulong flight)
        {
            foreach (PassengerCohort c in rig.CohortsOn(3))
            {
                if (c.Key.Flight.Value == flight)
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void test_queue_fifo_serves_earlier_arrival_first_regardless_of_size()
        {
            // Flight 1 (20 pax) reaches the closed queue a tick before flight 2
            // (1 pax). Opening one lane at 5 pax/min serves all of flight 1 before
            // any of flight 2, although flight 2 would fit in the first tick.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(5))));
            rig.InjectNow(1, 20, 1);
            rig.Step(3);
            rig.InjectNow(1, 1, 2);
            rig.Step(3);
            Assert.Equal(21, rig.Pop(2));

            Assert.True(rig.Submit(2, 1, out _));
            int guard = 0;
            while (rig.Pop(2) > 0 && guard++ < 200)
            {
                rig.Step(1);
                if (ReachedGate(rig, 2))
                {
                    Assert.False(rig.Flow.TryGetOutstanding(new FlightId(1), out _), "flight 2 served while flight 1 still queued");
                }
            }

            Assert.Equal(21, rig.Pop(3));
        }

        [Fact]
        public void test_queue_fifo_same_tick_arrivals_served_by_ascending_cohort_id()
        {
            // Two flights arrive at the queue in the same tick; the one injected
            // first has the smaller CohortId and is served first.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(5))));
            CohortId a = rig.InjectNow(1, 4, 7);
            CohortId b = rig.InjectNow(1, 4, 3);
            Assert.True(a.Value < b.Value);
            rig.Step(4);
            Assert.Equal(8, rig.Pop(2));
            Assert.True(rig.Submit(2, 1, out _));
            int guard = 0;
            while (rig.Pop(2) > 0 && guard++ < 200)
            {
                rig.Step(1);
                if (ReachedGate(rig, 3))
                {
                    Assert.False(rig.Flow.TryGetOutstanding(new FlightId(7), out _), "flight 3 (larger id) served before flight 7");
                }
            }
        }

        [Fact]
        public void test_queue_fifo_split_cohort_changes_no_outcome()
        {
            // Property (07 L4): the same passengers injected as one cohort or as
            // several cohorts of one key in the same tick give identical gate
            // arrivals at every tick, since service never depends on size.
            const ulong seed = 0xF1F0_0007UL;
            var rng = new SplitMix64(seed);
            string[] rates = { "0.7", "2.5", "4", "13.5" };
            for (int iteration = 0; iteration < 30; iteration++)
            {
                Fx rate = Fx.Parse(rates[rng.Range(0, rates.Length - 1)]);
                int total = rng.Range(2, 80);
                var parts = new List<int>();
                int left = total;
                while (left > 0)
                {
                    int p = rng.Range(1, left);
                    parts.Add(p);
                    left -= p;
                }

                var whole = Rig.Create(Graphs.Line(2, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)));
                var split = Rig.Create(Graphs.Line(2, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)));
                ulong at = (ulong)rng.Range(0, 30);
                whole.Step((uint)at);
                split.Step((uint)at);
                whole.InjectNow(1, total, 1);
                foreach (int p in parts)
                {
                    split.InjectNow(1, p, 1);
                }

                for (int t = 0; t < 400; t++)
                {
                    whole.Step(1);
                    split.Step(1);
                    Assert.True(whole.Pop(3) == split.Pop(3), "seed 0x" + seed.ToString("X") + ", iteration " + iteration + ", tick " + t + ": gate " + whole.Pop(3) + " vs " + split.Pop(3) + " (parts " + string.Join("+", parts) + ")");
                    Assert.True(whole.Pop(2) == split.Pop(2), "seed 0x" + seed.ToString("X") + ", iteration " + iteration + ", tick " + t + ": queue");
                }
            }
        }

        [Fact]
        public void test_queue_fifo_partial_service_keeps_id_on_remainder()
        {
            // §9.3: the served part moves under a new CohortId; the remainder
            // waiting in the queue keeps the original.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(10))));
            rig.InjectNow(1, 30, 1);
            rig.Step(4);
            List<PassengerCohort> waiting = rig.CohortsOn(2);
            Assert.Single(waiting);
            CohortId original = waiting[0].Id;

            // One lane at 10 pax/min serves exactly one passenger a tick.
            Assert.True(rig.Submit(2, 1, out _));
            rig.Step(3);
            List<PassengerCohort> after = rig.CohortsOn(2);
            Assert.Single(after);
            Assert.Equal(original, after[0].Id);
            Assert.True(after[0].Count < 30);
            foreach (PassengerCohort served in rig.CohortsOn(3))
            {
                Assert.NotEqual(original, served.Id);
            }
        }
    }
}
