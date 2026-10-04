using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 07 "Testing": the headless day. The movements, FlightIds, rotations,
    /// STA/STD and MinTurnaround are tests/fixtures/schedule/phase0-200.csv's
    /// (11 §11.3–§11.5); their lateness and blocking come from the seeded
    /// generator, since sim.delay is driven by events only (14 §14.14).
    /// 14 §14.14's integrated day with the real sim.schedule, sim.flow,
    /// sim.airside and sim.turnaround is not here: see the T-024 spec-gap report.
    /// </summary>
    public sealed class DelayHeadlessDayTests
    {
        [Fact]
        public void test_delay_headless_day_phase0_schedule_holds_every_invariant()
        {
            byte[] csv = ScheduleFixture.Bytes();
            var run = new GeneratedRun(
                0x0024_DA70UL,
                (rng, day) => ScheduleFixture.Day(csv, day),
                model: true,
                perEvent: (d, flight, tick, context) => Invariants.CheckFlight(d, flight, context));
            Assert.Equal(200, ScheduleFixture.Day(csv, 0).Count);

            // Day 0, then day 1 so that day 0's late departures finish and day 1's
            // first prune (nothing old enough yet) runs.
            run.RunDays(1, day =>
            {
                run.Model!.PruneThrough(run.Rig.Host.CurrentTick - 1UL);
                Assert.Equal(run.Model.Snapshot(), Show.Snapshot(run.Rig.Delay));
                Invariants.CheckAll(run.Rig.Delay, run.Context(day));
            });

            int finalisedDay0 = 0;
            int late = 0;
            foreach (FlightId f in run.Rig.Delay.RetainedFlights())
            {
                Assert.True(run.Rig.Delay.TryGetFlightDelay(f, out FlightDelay r));
                if (f.Value < 100000UL && r.Finalised)
                {
                    finalisedDay0++;
                    if (r.TotalTicks > 0UL)
                    {
                        late++;
                    }
                }
            }

            Assert.True(finalisedDay0 >= 180, "only " + finalisedDay0 + " of day 0's 200 flights finalised");
            Assert.True(late >= 50, "only " + late + " day-0 flights were late");
        }
    }
}
