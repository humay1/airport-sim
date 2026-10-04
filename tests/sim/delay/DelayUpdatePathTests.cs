using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 03 "The update path" and "allocation test" (Q-061), 14 §14.13: all of
    /// sim.delay's phase 1-3 code allocates nothing — every event handler, Tick,
    /// and pruning, which "must still not allocate". Metered with the T-037
    /// meter. The drivers publish from pre-built steps and allocate nothing;
    /// no recorder is registered.
    /// </summary>
    public sealed class DelayUpdatePathTests
    {
        /// <summary>
        /// One batch of traffic within 540 ticks of <paramref name="t0"/> that runs
        /// every handler sim.delay registers and every branch of §14.6:
        /// publication, ignored milestones, all five interval families, the
        /// ignored job-dependency wait, the inbound rule, cap, residue,
        /// recovery, finalisation with publication, missed passengers, and
        /// events ignored after finalisation. Ids id+1 (arrival), id+2 (its
        /// departure), id+3 (rotation-less departure).
        /// </summary>
        private static void Batch(Script s, ulong t0, ulong id)
        {
            ulong a = id + 1UL;
            ulong d = id + 2UL;
            ulong x = id + 3UL;
            s.Plan(t0, a, MovementKind.Arrival, d);
            s.Plan(t0, d, MovementKind.Departure, a);
            s.Plan(t0, x, MovementKind.Departure);
            s.Milestone(t0, a, FlightMilestone.PlanPublished, t0);

            s.RunwayHeld(t0 + 10, a, 1, 2);
            s.RunwayReleased(t0 + 40, a, 1);
            s.Milestone(t0 + 50, a, FlightMilestone.Landed, t0 + 20);
            s.TaxiHeld(t0 + 60, a, 3, 9);
            s.TaxiReleased(t0 + 70, a, 3);
            s.StandUnavailable(t0 + 75, a, null, null);
            s.StandAssigned(t0 + 85, a, null);
            s.Milestone(t0 + 100, a, FlightMilestone.OnStand, t0 + 50);
            s.Milestone(t0 + 105, a, FlightMilestone.DoorsOpen, t0 + 105);
            s.JobBlocked(t0 + 106, a, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4);
            s.JobUnblocked(t0 + 120, a, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4);

            s.Milestone(t0 + 130, d, FlightMilestone.OnStand, t0 + 100);
            s.JobBlocked(t0 + 140, d, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.JobBlocked(t0 + 150, d, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.Hold(t0 + 160, d, 4, 7);
            s.JobUnblocked(t0 + 200, d, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            s.HoldReleased(t0 + 220, d);
            s.JobUnblocked(t0 + 225, d, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            s.Missed(t0 + 230, d, 2, 7);
            s.Milestone(t0 + 250, d, FlightMilestone.Pushback, t0 + 150);
            s.TaxiHeld(t0 + 270, d, 4, null);
            s.TaxiReleased(t0 + 280, d, 4);
            s.RunwayHeld(t0 + 290, d, 2, 1);
            s.RunwayReleased(t0 + 300, d, 2);
            s.Milestone(t0 + 320, d, FlightMilestone.Airborne, t0 + 260);

            s.Milestone(t0 + 140, x, FlightMilestone.OnStand, t0 + 100);
            s.JobBlocked(t0 + 150, x, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobUnblocked(t0 + 400, x, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(t0 + 410, x, FlightMilestone.Pushback, t0 + 300);
            s.Milestone(t0 + 500, x, FlightMilestone.Airborne, t0 + 390);
            s.Missed(t0 + 520, x, 1, 3);
            s.RunwayHeld(t0 + 530, x, 3, 1);
            s.RunwayReleased(t0 + 540, x, 3);
        }

        [Fact]
        public void test_delay_update_path_allocates_nothing_including_handlers()
        {
            // Warm-up batches at 700 and 1500; the metered batch at 3610 runs in
            // ticks 3601-4199, which hold no checkpoint (every 600) and no day
            // boundary. Every handler has already run before the window.
            const ulong WindowStart = 3601UL;
            const ulong WindowEnd = 4200UL; // exclusive: tick 4200 is a checkpoint
            var s = new Script();
            Batch(s, 700, 100);
            Batch(s, 1500, 200);
            Batch(s, 3610, 300);
            var rig = new DelayRig(s, record: false);
            rig.RunTo(WindowStart);
            Assert.True(rig.Record(203).Finalised, "warm-up did not finish");

            long start = Allocation.Start();
            rig.Host.Step((uint)(WindowEnd - WindowStart));
            long bytes = Allocation.Since(start);
            Assert.True(bytes == 0L, "the update path allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes in ticks 3601-4199");

            // The window did the work it was meant to meter.
            Assert.True(rig.Record(301).Finalised && rig.Record(302).Finalised && rig.Record(303).Finalised);
            Assert.Equal(2, rig.Record(302).MissedPassengers);
            Assert.Equal(50UL, rig.Record(301).TotalTicks);
            Assert.Equal(30UL, rig.Leaf(302, DelaySource.InboundAircraft).Ticks);
            Assert.Equal(60UL, rig.Record(302).TotalTicks);
            Assert.Equal(110UL, rig.Record(303).TotalTicks);
        }

        [Fact]
        public void test_delay_update_path_pruning_allocates_nothing()
        {
            // Pruning runs in Tick at a day boundary, which is also a checkpoint
            // tick, so it is metered around sim.delay's own Tick at 43200 (day 3:
            // day-1 trees go), after a warm-up prune at 28800 (day 2: day-0 trees).
            var rng = new SplitMix64(0x0024_9409UL);
            var s = new Script();
            for (int day = 0; day <= 3; day++)
            {
                Generator.Day(rng, Generator.SyntheticDay(rng, day), s, 0);
            }

            var rig = new DelayRig(s, record: false, probe: true);
            rig.Probe!.MeterTick = 3UL * DConst.TicksPerDay;
            rig.RunTo(3UL * DConst.TicksPerDay);
            int day1Before = 0;
            foreach (FlightId f in rig.Delay.RetainedFlights())
            {
                if (f.Value >= 100000UL && f.Value < 200000UL)
                {
                    day1Before++;
                }
            }

            rig.RunThrough(3UL * DConst.TicksPerDay);
            int day1After = 0;
            foreach (FlightId f in rig.Delay.RetainedFlights())
            {
                if (f.Value >= 100000UL && f.Value < 200000UL)
                {
                    day1After++;
                }
            }

            Assert.True(day1Before > 10 && day1After < day1Before, "the metered prune removed nothing: " + day1Before + " → " + day1After + " day-1 flights");
            Assert.True(rig.Probe.MeteredBytes == 0L, "pruning allocated " + rig.Probe.MeteredBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
        }
    }
}
