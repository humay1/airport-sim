using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 03 "The update path" and "allocation test" (Q-061), 12 §12.12: all of
    /// sim.airside's phase 1-3 code allocates nothing — Tick, the
    /// FlightPlanPublished and FlightMilestoneReached handlers, and the
    /// ReassignStand Apply. Metered with the T-037 meter over ISimHost.Step,
    /// ticks 3601 to 4199, which hold no checkpoint (every 600) and no
    /// schedule day boundary. Every registered system is a non-allocating
    /// probe or fake. turnaroundRegistered is true, so the
    /// FlightMilestoneReached handler records DeboardComplete and
    /// BoardingComplete from a probe at position 5. Each handler, and Apply
    /// applied and as a no-op, has already run once in the warm-up.
    /// </summary>
    public sealed class AirsideUpdatePathTests
    {
        private const ulong WindowStart = 3601UL;
        private const ulong WindowEnd = 4200UL; // exclusive: tick 4200 is a checkpoint

        [Fact]
        public void test_airside_update_path_allocates_nothing_including_handlers()
        {
            // All rows repeat daily, so each one's day-1 occurrence publishes
            // during day 0, at its day-0 time, and the handler appends the
            // arrivals and rotation-less departures among them.
            var rows = new System.Collections.Generic.List<string>();
            rows.AddRange(new[]
            {
                Csv.Row("W_A", "A", "03:00", "W_D", repeat: "1"), Csv.Row("W_D", "D", "05:00", "W_A", repeat: "1"),
                Csv.Row("X1", "A", "05:00", repeat: "1"),
                Csv.Row("R_A", "A", "06:30", "R_D", repeat: "1"), Csv.Row("R_D", "D", "08:00", "R_A", repeat: "1"),
            });
            var probe = new TurnaroundProbe();
            var log = new CountingLog();
            var rig = new HostRig(Csv.Of(rows.ToArray()), flow: new RuleFlow(), turnaroundRegistered: true, turnaround: probe, record: false, log: log);
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            ulong wa = rig.Id("W_A");
            ulong wd = rig.Id("W_D");
            ulong x1 = rig.Id("X1");

            // Warm-up. W_A is on S1 from 1860 with doors open at 1880. Its
            // handoff and doors-close come from the probe; one ReassignStand
            // is applied (W_A to S4) and one is a no-op (unknown flight). W_A's
            // and X1's day-1 occurrences are published at 1800 and 3000.
            probe.Script.Add((1890UL, wa, FlightMilestone.DeboardComplete));
            probe.Script.Add((1950UL, wd, FlightMilestone.BoardingComplete));
            // Window: R_A is on S1 from 3960, doors open 3980; its day-1
            // occurrence is published at 3900.
            probe.Script.Add((4000UL, ra, FlightMilestone.DeboardComplete));
            probe.Script.Add((4100UL, rd, FlightMilestone.BoardingComplete));

            // After W_A's DoorsOpen (1880): before it, 12 §12.10 (Q-083) makes it a reason-1 no-op.
            Assert.True(rig.Submit(Payload.ReassignCommand(1885UL, wa, FixtureLayout.S4), out _));
            Assert.True(rig.Submit(Payload.ReassignCommand(1875UL, 999UL, FixtureLayout.S3), out _));
            Assert.True(rig.Submit(Payload.ReassignCommand(3700UL, x1, FixtureLayout.S3), out _));
            Assert.True(rig.Submit(Payload.ReassignCommand(3710UL, 999UL, FixtureLayout.S2), out _));

            rig.RunTo(WindowStart);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(wa), out _), "W_A was not handed off in the warm-up");
            Assert.Equal(1, log.ReassignNoOps); // the unknown flight; W_A's move applied
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            int published = probe.Published.Count;

            long start = Allocation.Start();
            rig.Host.Step((uint)(WindowEnd - WindowStart));
            long bytes = Allocation.Since(start);
            Assert.True(bytes == 0L, "the update path allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes in ticks 3601-4199");

            // The window did the work it was meant to meter.
            Assert.Equal(published + 2, probe.Published.Count);
            Assert.Equal(2, log.ReassignNoOps);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S3)!.Value.Value);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(ra), out _), "R_A was not handed off in the window");
            if (rig.Airside.TryGetTrack(new FlightId(rd), out AircraftTrack dep))
            {
                // Recorded at 4100, cleared at the doors-close point at 4101.
                Assert.False(dep.RecordedCause.HasValue, "R_D's BoardingComplete was not acted on: " + Show.Track(dep));
            }

            // R_A's day-1 occurrence, published at 3900 inside the window,
            // reached the pending list: it starts at its InboundAirborne.
            ulong raDay1 = AirConst.DayStride + ra;
            ulong inbound = AirConst.TicksPerDay + AirConst.At(6, 30) - AirConst.CruiseLead;
            rig.RunTo(inbound + 1UL);
            Assert.True(rig.Airside.TryGetTrack(new FlightId(raDay1), out _), "R_A's day-1 occurrence never started");
        }
    }
}
