using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// QueueThresholdExceeded / Cleared (09 §9.9, §9.12 "Thresholds", 10 §10.3
    /// rule 4): exceeded when w > T, cleared when w < T - h, evaluated after
    /// movement and merge; strict on both sides so a constant wait never flaps.
    /// The threshold flag is hashed (§9.10). The property test in
    /// QueueThroughputTests checks every event tick against QueueModel.
    /// </summary>
    public sealed class QueueThresholdTests
    {
        private static Rig Lane(Fx rate, int open, Fx threshold, Fx hysteresis)
        {
            return Rig.Create(Graphs.Line(2, open, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate, threshold, hysteresis)));
        }

        private static void Arrive(Rig rig, int count)
        {
            rig.InjectNow(1, count, 1);
            int guard = 0;
            while (rig.Pop(1) > 0 && guard++ < 6)
            {
                rig.Step(1);
            }
        }

        [Fact]
        public void test_queue_threshold_exceeded_then_cleared_with_hysteresis()
        {
            // One lane at 8 pax/min: w = pop / 8, exact in Fx. T = 0.5 and
            // h = 0.125, so T - h = 0.375 = w(3): exceeded from pop 5 up, and
            // cleared strictly below 0.375, i.e. at pop 2, not at pop 3.
            Fx rate = Fx.FromInt(8);
            var rig = Lane(rate, 1, Fx.FromRatio(1, 2), Fx.FromRatio(1, 8));
            Arrive(rig, 6);
            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Exceeded);
            QueueThresholdExceeded up = ev.Exceeded[0].E;
            Assert.Equal(new NodeId(2), up.Node);
            Assert.Equal(Graphs.Wait(6, 1, rate), up.WaitMinutes);
            Assert.Equal(1, up.ServersOpen);
            Assert.Equal(2, up.ServerCount);
            Assert.Equal(rig.Host.CurrentTick - 1, ev.Exceeded[0].Tick);

            bool sawThree = false;
            ulong firstAtTwo = ulong.MaxValue;
            for (int i = 0; i < 100; i++)
            {
                ulong t = rig.NextTick;
                rig.Step(1);
                if (rig.Pop(2) == 3)
                {
                    sawThree = true;
                    Assert.Empty(ev.Cleared);
                }

                if (firstAtTwo == ulong.MaxValue && rig.Pop(2) <= 2)
                {
                    firstAtTwo = t;
                }
            }

            Assert.True(sawThree);
            Assert.Single(ev.Exceeded);
            Assert.Single(ev.Cleared);
            Assert.Equal(firstAtTwo, ev.Cleared[0].Tick);
            Assert.Equal(Graphs.Wait(2, 1, rate), ev.Cleared[0].E.WaitMinutes);
            Assert.Equal(new NodeId(2), ev.Cleared[0].E.Node);
            Assert.Equal(1, ev.Cleared[0].E.ServersOpen);
            Assert.Equal(2, ev.Cleared[0].E.ServerCount);
        }

        [Fact]
        public void test_queue_threshold_constant_wait_never_flaps_even_with_zero_hysteresis()
        {
            // Lanes closed: nobody is served and w is constant. With T exactly
            // w(5), five standing never fire (w > T is strict); six fire once and
            // never clear, h = 0 included.
            Fx rate = Fx.One;
            Fx threshold = Graphs.Wait(5, 0, rate);
            var atThreshold = Lane(rate, 0, threshold, Fx.Zero);
            Arrive(atThreshold, 5);
            atThreshold.Step(300);
            Assert.Equal(threshold, atThreshold.Flow.PredictedWaitMinutes(new NodeId(2)));
            Assert.Empty(atThreshold.Events!.Exceeded);
            Assert.Empty(atThreshold.Events.Cleared);

            var above = Lane(rate, 0, threshold, Fx.Zero);
            Arrive(above, 6);
            above.Step(300);
            Assert.Single(above.Events!.Exceeded);
            Assert.Empty(above.Events.Cleared);
        }

        [Fact]
        public void test_queue_threshold_evaluated_after_movement_in_the_same_tick()
        {
            // The arrival tick's own population counts: the event fires in the
            // tick the passengers enter, not the tick after.
            Fx rate = Fx.FromInt(8);
            var rig = Lane(rate, 1, Fx.Zero, Fx.Zero);
            rig.InjectNow(1, 1, 1);
            int guard = 0;
            ulong entered = ulong.MaxValue;
            while (entered == ulong.MaxValue && guard++ < 6)
            {
                ulong t = rig.NextTick;
                rig.Step(1);
                if (rig.Pop(2) > 0)
                {
                    entered = t;
                }
            }

            Assert.Single(rig.Events!.Exceeded);
            Assert.Equal(entered, rig.Events.Exceeded[0].Tick);
        }

        [Fact]
        public void test_queue_threshold_flag_is_hashed()
        {
            // §9.10: the threshold flag is state. Same population, same lanes,
            // same tick; only the flag differs, so the hashes differ.
            var set = Lane(Fx.One, 0, Fx.Zero, Fx.Zero);
            var clear = Lane(Fx.One, 0, Fx.FromInt(1000000), Fx.Zero);
            Arrive(set, 1);
            Arrive(clear, 1);
            Assert.Single(set.Events!.Exceeded);
            Assert.Empty(clear.Events!.Exceeded);
            Assert.Equal(set.Host.CurrentTick, clear.Host.CurrentTick);
            Assert.NotEqual(set.Flow.ComputeStateHash(), clear.Flow.ComputeStateHash());
        }
    }
}
