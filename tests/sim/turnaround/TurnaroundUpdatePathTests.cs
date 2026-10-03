using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 03 "The update path" and "allocation test" (Q-061), 13 §13.10: all of
    /// sim.turnaround's phase 1-3 code allocates nothing: Tick (completions,
    /// Boarding's unblock, vehicle assignment, milestones) and the
    /// FlightMilestoneReached handler that creates jobs. Metered with the
    /// T-037 meter over ISimHost.Step, ticks 3601 to 4199, which hold no
    /// checkpoint (every 600) and no schedule day boundary. Every other
    /// registered system is a non-allocating probe, or sim.schedule with
    /// nothing to publish there. The handler, and every Tick path, has already
    /// run in the warm-up.
    ///
    /// 13 §13.11 asks for this with sim.airside registered. The test project
    /// cannot reference sim.airside under 07 L3 (spec gap reported with T-022),
    /// so OnStand comes from the position-3 driver; sim.turnaround learns of a
    /// flight only through that event either way (13 §13.1).
    /// </summary>
    public sealed class TurnaroundUpdatePathTests
    {
        private const ulong WindowStart = 3601UL;
        private const ulong WindowEnd = 4200UL; // exclusive: tick 4200 is a checkpoint

        [Fact]
        public void test_turnaround_update_path_allocates_nothing_including_handlers()
        {
            // One vehicle of each kind (ids 1-5 in VehicleKind order). Each
            // batch is an arrival and two departures: the second departure
            // waits for every vehicle, so Blocked, Unblocked, same-tick
            // reassignment and Boarding's unblock all happen in each batch.
            byte[] csv = Csv.Of(
                Csv.Row("A1", "A", "05:00"), Csv.Row("D1", "D", "06:00"), Csv.Row("D2", "D", "06:00"),
                Csv.Row("A2", "A", "06:00"), Csv.Row("D3", "D", "07:00"), Csv.Row("D4", "D", "07:00"));
            var rig = new Rig(
                csv,
                Setups.Unit(Setups.Plenty(1)),
                onStand: new[]
                {
                    ("A1", 3000UL), ("D1", 3000UL), ("D2", 3010UL),
                    ("A2", 3650UL), ("D3", 3650UL), ("D4", 3660UL),
                },
                record: false);

            rig.RunTo(WindowStart);
            Assert.Equal(JobStatus.Completed, rig.Job("D2", JobKind.Boarding).Status);
            Assert.Equal(JobStatus.Completed, rig.Job("A1", JobKind.Deboard).Status);
            Assert.True(rig.Job("D2", JobKind.CabinClean).StartedAt > 3010UL, "warm-up: D2 never waited for a vehicle");
            Assert.Empty(rig.Turnaround.JobsForFlight(new FlightId(rig.Id("D4"))));

            long start = Allocation.Start();
            rig.Host.Step((uint)(WindowEnd - WindowStart));
            long bytes = Allocation.Since(start);
            Assert.True(bytes == 0L, "the update path allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes in ticks 3601-4199");

            // The window did the work it was meant to meter.
            Assert.Equal(6, rig.Driver.Published);
            Assert.Equal(2, rig.Turnaround.JobsForFlight(new FlightId(rig.Id("A2"))).Count);
            Assert.Equal(JobStatus.Completed, rig.Job("A2", JobKind.Deboard).Status);
            Assert.True(rig.Job("D4", JobKind.CabinClean).StartedAt > 3660UL, "window: D4 never waited for a vehicle");
            Assert.Equal(JobStatus.Completed, rig.Job("D4", JobKind.Boarding).Status);
        }
    }
}
