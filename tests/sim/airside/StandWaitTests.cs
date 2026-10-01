using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.2 and §12.12 "Hard bounds": the stand-wait queue holds at most
    /// STAND_WAIT_CAPACITY (1024) entries, preallocated; an append during a
    /// tick that would exceed it throws SimInvariantException (08 §8.5a),
    /// which the host wraps once with the tick. N rotation-less departures
    /// due at the same tick on the four-stand layout: four get stands in
    /// S5, the other N - 4 join the queue in that same step.
    /// </summary>
    public sealed class StandWaitTests
    {
        private const ulong Due = 3600UL; // STD 06:35 - 35 min

        private static HostRig Rig(int departures)
        {
            var rows = new string[departures];
            for (int i = 0; i < departures; i++)
            {
                rows[i] = Csv.Row("D" + i.ToString("D4", CultureInfo.InvariantCulture), "D", "06:35");
            }

            return new HostRig(Csv.Of(rows), record: false);
        }

        [Fact]
        public void test_stand_wait_queue_overflow_throws_sim_invariant()
        {
            // 4 + 1025 waiters: the 1025th append overflows.
            HostRig over = Rig(4 + AirConst.StandWaitCapacity + 1);
            over.RunTo(Due);
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => over.Host.Step(1));
            Assert.Equal(Due, ex.Tick);
            Assert.IsType<SimInvariantException>(ex.InnerException);
            Assert.Equal(Due, ((SimInvariantException)ex.InnerException!).Tick);
        }

        [Fact]
        public void test_stand_wait_queue_holds_exactly_its_capacity()
        {
            // 4 + 1024 waiters fit, and the queue drains without throwing.
            HostRig full = Rig(4 + AirConst.StandWaitCapacity);
            full.RunTo(Due + 10UL);
            Assert.Equal(Due + 10UL, full.Host.CurrentTick);
        }
    }
}
