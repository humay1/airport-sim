using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 03 "The update path" and "allocation test" (Q-061), 13 §13.10/§13.11:
    /// with sim.airside registered (Q-087), all of sim.turnaround's phase 1-3
    /// code allocates nothing: Tick (completions, vehicle reassignment,
    /// Boarding's unblock, milestones) and the FlightMilestoneReached handler
    /// that creates jobs. Metered with the T-037 meter over ISimHost.Step,
    /// ticks 3601 to 4199, which hold no checkpoint (every 600) and no
    /// schedule day boundary; every row is day 0 and not repeated, so all
    /// publication happens at tick 0. sim.airside's own update path allocates
    /// nothing either (12 §12.12), so the whole Step is metered. Each path
    /// has already run in the warm-up, with the same shape of traffic.
    /// </summary>
    public sealed class TurnaroundUpdatePathTests
    {
        private const ulong WindowStart = 3601UL;
        private const ulong WindowEnd = 4200UL; // exclusive: tick 4200 is a checkpoint

        [Fact]
        public void test_turnaround_update_path_allocates_nothing_including_handlers()
        {
            // Two rotations landing a minute apart in each batch, on the 13
            // §13.11 setup (one vehicle of each kind): the second rotation's
            // departure reaches its stand while the first's still holds the
            // fuel truck (120 ticks), and its arrival's BaggageUnload contends
            // with the first departure's BaggageLoad for the one tractor.
            byte[] csv = Csv.Of(
                Csv.Row("W1A", "A", "03:00", "W1D"), Csv.Row("W1D", "D", "05:00", "W1A"),
                Csv.Row("W2A", "A", "03:01", "W2D"), Csv.Row("W2D", "D", "05:00", "W2A"),
                Csv.Row("R1A", "A", "06:20", "R1D"), Csv.Row("R1D", "D", "07:30", "R1A"),
                Csv.Row("R2A", "A", "06:21", "R2D"), Csv.Row("R2D", "D", "07:30", "R2A"));
            var rig = new AirsideRig(csv, Phase1Fixture.Setup(), record: false);

            rig.RunTo(WindowStart);
            Assert.Equal(JobStatus.Completed, rig.Job("W1D", JobKind.Boarding).Status);
            Assert.Equal(JobStatus.Completed, rig.Job("W2D", JobKind.Boarding).Status);
            TurnaroundJob w2Fuel = rig.Job("W2D", JobKind.Fuel);
            Assert.True(w2Fuel.StartedAt > w2Fuel.CreatedAt, "warm-up: W2D's Fuel never waited for the truck");
            Assert.Empty(rig.Turnaround.JobsForFlight(new FlightId(rig.Id("R1A"))));

            long start = Allocation.Start();
            rig.Host.Step((uint)(WindowEnd - WindowStart));
            long bytes = Allocation.Since(start);
            Assert.True(bytes == 0L, "the update path allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes in ticks 3601-4199");

            // The window did the work it was meant to meter: both rotations
            // were created and handed off, R1D boarded, R2D waited for the truck.
            Assert.Equal(JobStatus.Completed, rig.Job("R1A", JobKind.Deboard).Status);
            Assert.Equal(JobStatus.Completed, rig.Job("R2A", JobKind.Deboard).Status);
            Assert.Equal(6, rig.Turnaround.JobsForFlight(new FlightId(rig.Id("R2D"))).Count);
            Assert.Equal(JobStatus.Completed, rig.Job("R1D", JobKind.Boarding).Status);
            TurnaroundJob r2Fuel = rig.Job("R2D", JobKind.Fuel);
            Assert.True(r2Fuel.StartedAt > r2Fuel.CreatedAt && r2Fuel.StartedAt != TConst.TickUnscheduled, "window: R2D's Fuel never waited for, or never got, the truck");
        }
    }
}
