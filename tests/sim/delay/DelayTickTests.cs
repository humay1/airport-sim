using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>14 §14.13 "RNG: none", and 14 §14.8: pruning is the only work Tick does.</summary>
    public sealed class DelayTickTests
    {
        [Fact]
        public void test_delay_tick_consumes_no_rng()
        {
            // Every Tick and every handler gets a trap RNG that counts any use,
            // over three generated days: two prunings, every handler.
            var rng = new SplitMix64(0x0024_71C4UL);
            var s = new Script();
            for (int day = 0; day <= 3; day++)
            {
                Generator.Day(rng, Generator.SyntheticDay(rng, day), s, 40);
            }

            var trap = new TrapRandomService();
            var rig = new DelayRig(s, trapRng: trap);
            rig.RunThrough(3UL * DConst.TicksPerDay);

            Assert.Equal(0, trap.StreamCalls);
            Assert.Equal(0, trap.Draws);

            // The run did the work: some day-0 flights were pruned (Tick ran its
            // pruning with the trap in place), and trees with leaves exist.
            int publishedDay0 = rig.R.All.FindAll(r => r.Payload is FlightPlanPublished && r.Flight < 100000UL).Count;
            int retainedDay0 = 0;
            int leaves = 0;
            foreach (FlightId f in rig.Delay.RetainedFlights())
            {
                leaves += rig.Delay.LeavesOf(f).Count;
                if (f.Value < 100000UL)
                {
                    retainedDay0++;
                }
            }

            Assert.True(publishedDay0 > 10 && retainedDay0 < publishedDay0, "no day-0 flight was pruned: " + retainedDay0 + " of " + publishedDay0 + " retained");

            Assert.True(leaves > 20, "too few leaves to have exercised the handlers: " + leaves);
            Assert.True(rig.R.Delays.Count > 100, "too few DelayEvents: " + rig.R.Delays.Count);
        }
    }
}
