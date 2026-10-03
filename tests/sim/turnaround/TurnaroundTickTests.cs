using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>13 §13.10: no RNG at Phase 0/1, in Tick or in any handler.</summary>
    public sealed class TurnaroundTickTests
    {
        [Fact]
        public void test_turnaround_tick_consumes_no_rng()
        {
            // The probe at position 5 hands Tick a trap RNG, and the shimmed bus
            // hands every sim.turnaround handler the same trap. A day of the
            // 13 §13.11 setup exercises creation, waits, completions and all
            // three milestones.
            var trap = new TrapRandomService();
            var rig = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1, trapRng: trap);
            rig.RunTo(TConst.TicksPerDay);

            Assert.NotEmpty(rig.Rec.Of<TurnaroundJobBlocked>());
            Assert.NotEmpty(rig.Rec.Milestones(FlightMilestone.DeboardComplete));
            Assert.NotEmpty(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            Assert.Equal(0, trap.StreamCalls);
            Assert.Equal(0, trap.Draws);
        }

        [Fact]
        public void test_turnaround_tick_outcome_is_independent_of_master_seed()
        {
            // The same day under two master seeds: sim.turnaround's events and
            // hash cannot differ if no stream reaches it.
            var a = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1, seed: 1UL);
            var b = new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1, seed: 0xDEAD_BEEF_0022UL);
            a.RunTo(TConst.TicksPerDay);
            b.RunTo(TConst.TicksPerDay);
            Assert.NotEmpty(a.Rec.Of<TurnaroundJobStarted>());
            Assert.Equal(a.Rec.Trace(), b.Rec.Trace());
            Assert.Equal(a.Turnaround.ComputeStateHash(), b.Turnaround.ComputeStateHash());
        }
    }
}
