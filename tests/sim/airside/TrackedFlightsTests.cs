using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.2 and §12.12 "Tracked flights" (Q-085): at most
    /// TRACKED_FLIGHTS_CAPACITY (4096) flights are tracked. S2's arrival
    /// start that would add track 4 097 throws SimInvariantException at that
    /// tick, before it publishes, naming the flight and the tracked flights,
    /// and every earlier Tick allocates nothing. A run that §12.2 "Why 4096
    /// tracks hold" covers never throws.
    /// </summary>
    public sealed class TrackedFlightsTests
    {
        private const int PerMinute = 30;

        // Rows per calendar day: 4 097 in all, so day 2's last row is track 4 097.
        private static readonly int[] RowsPerDay = { 1365, 1366, 1366 };

        /// <summary>Counts sim.airside's InboundAirborne without allocating.</summary>
        private sealed class InboundCounter
        {
            public int Count;
            public ulong LastFlight;
        }

        private static string Hhmm(int minute)
        {
            return (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        [Fact]
        public void test_tracked_flights_overflow_throws_sim_invariant()
        {
            // Rotation-less arrivals only: they never leave tracked state
            // (§12.7), and one runway at 1 per hour lands one every 600 ticks,
            // so no track is removed and the count only grows. Each day's rows
            // run 30 a minute: day 0 (A) from 02:10, so InboundAirborne falls
            // in ticks 100-550 and none at tick 0; days 1 (B) and 2 (C) from
            // 03:00, each published on the previous day (STA - 14 400). Every
            // day's starts end by its 03:45 - 1 200, before the next day's
            // publications begin at 03:00, so the pending list holds at most
            // one day's rows (1 366), under 2 048.
            var rows = new List<string>();
            string[] prefix = { "A", "B", "C" };
            int[] firstMinute = { 130, 180, 180 };
            for (int day = 0; day < RowsPerDay.Length; day++)
            {
                for (int i = 0; i < RowsPerDay[day]; i++)
                {
                    rows.Add(Csv.Row(prefix[day] + i.ToString("D4", CultureInfo.InvariantCulture), "A", Hhmm(firstMinute[day] + (i / PerMinute)), day: day.ToString(CultureInfo.InvariantCulture)));
                }
            }

            byte[] csv = Csv.Of(rows.ToArray());
            Dictionary<string, ulong> ids = Csv.Ids(csv);

            // Track 4 097 is day 2's last row, C1365: RowOrdinal 4 096 on day 2
            // (11 §11.3). Its STA is day 2 03:45 (minute 1365 / 30 = 45), so its
            // InboundAirborne is due at 28 800 + 2 250 - 1 200 = 29 850. The
            // earlier tracks are days 0 and 1 (2 731) plus day 2's minutes 0-44
            // (45 x 30 = 1 350): 4 081. That tick then starts C1350-C1365,
            // tracks 4 082 to 4 097.
            ulong overflowing = ids["C1365"];
            Assert.Equal((2UL * AirConst.DayStride) + 4097UL, overflowing);
            ulong sta = (2UL * AirConst.TicksPerDay) + AirConst.At(3, 45);
            ulong throwTick = sta - AirConst.CruiseLead;
            Assert.Equal(29850UL, throwTick);
            const int before = 1365 + 1366 + (45 * PerMinute);
            Assert.Equal(4081, before);

            // A host with sim.schedule, sim.airside and a non-allocating counter.
            var counter = new InboundCounter();
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(0x5EED_0021UL, AirsideContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(csv, "overflow.csv");
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(b.Services, table, null);
            IAirsideSystem airside = AirsideFactory.CreateSystem(
                b.Services, FixtureLayout.Layout(1), new AirsideRules(10U, AirConst.FixtureDoorDelayMinutes), schedule, null, false);
            b.Services.Events.Subscribe<FlightMilestoneReached>(new SystemId(AirConst.RecorderSystemId), (in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx) =>
            {
                if (env.Source.Value == AirConst.AirsideSystemId && evt.Milestone == FlightMilestone.InboundAirborne)
                {
                    counter.Count++;
                    counter.LastFlight = evt.Flight.Value;
                }
            });
            b.Register(schedule);
            b.Register(airside);
            b.Register(new ProbeSystem(AirConst.RecorderSystemId));
            ISimHost host = b.Build();

            // Every earlier Tick: metered with the T-037 meter over Step windows
            // that hold no checkpoint tick (every 600; ticks 14 400 and 28 800
            // are also sim.schedule's day boundaries), as 03 permits. The first
            // 600 ticks are the warm-up 03 requires before metering.
            const ulong checkpoint = 600UL;
            host.Step((uint)checkpoint);
            while (host.CurrentTick < throwTick)
            {
                Assert.Equal(0UL, host.CurrentTick % checkpoint);
                host.Step(1); // the checkpoint tick, unmetered
                ulong first = host.CurrentTick;
                ulong end = Math.Min(first + checkpoint - 1UL, throwTick);
                long start = Allocation.Start();
                host.Step((uint)(end - first));
                long bytes = Allocation.Since(start);
                Assert.True(bytes == 0L, string.Format(CultureInfo.InvariantCulture, "ticks {0}-{1} allocated {2} bytes", first, end - 1UL, bytes));
            }

            Assert.Equal(throwTick, host.CurrentTick);
            Assert.Equal(before, counter.Count);
            Assert.Equal(before, airside.TrackedFlights().Count);

            // The host wraps the module's throw once (08 §8.5a).
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => host.Step(1));
            Assert.Equal(throwTick, ex.Tick);
            var inner = Assert.IsType<SimInvariantException>(ex.InnerException);
            Assert.Equal(throwTick, inner.Tick);
            Assert.Contains(overflowing.ToString(CultureInfo.InvariantCulture), inner.Message, StringComparison.Ordinal);
            Assert.Contains("track", inner.Message, StringComparison.OrdinalIgnoreCase);

            // Nothing of the throwing tick reached a subscriber, C1365's
            // InboundAirborne included.
            Assert.Equal(before, counter.Count);
            Assert.NotEqual(overflowing, counter.LastFlight);
        }

        [Fact]
        public void test_tracked_flights_bound_not_reached_by_covered_max_tier_run()
        {
            // §12.13's second case: a run §12.2 says stays under the bound. Max
            // tier, 800 rotation-less departures a day repeating from day 1
            // (none on day 0, so tick 0 carries no burst), MinTurnaround 1 440
            // minutes. Each is due at STD - 14 400 = its PublishTick and so
            // starts at PublishTick + 1 (§12.11). 800 movements a calendar day,
            // 60 stands, and every flight leaves well within 3 sim-days, so by
            // "Why 4096 tracks hold" the run never reaches 4 096. Three days.
            byte[] fixture = ScheduleFixture.Bytes();
            string[] lines = System.Text.Encoding.UTF8.GetString(fixture).Split('\n');
            var rows = new List<string>(800);
            int n = 0;
            foreach (string suffix in new[] { "a", "b", "c", "d" })
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0)
                    {
                        continue;
                    }

                    // Keep each fixture row's time; make it a rotation-less departure.
                    string[] f = lines[i].Split(',');
                    rows.Add(Csv.Row("P" + n.ToString("D3", CultureInfo.InvariantCulture) + suffix, "D", f[6], aircraft: f[5], minTurn: "1440", repeat: "1", day: "1"));
                    n++;
                }
            }

            Assert.Equal(800, rows.Count);
            var rig = new HostRig(Csv.Of(rows.ToArray()), layout: MaxTierLayout.Layout());
            int peak = 0;
            rig.StepEach(3UL * AirConst.TicksPerDay, t => peak = Math.Max(peak, rig.Airside.TrackedFlights().Count));

            Assert.Equal(3UL * AirConst.TicksPerDay, rig.Host.CurrentTick);
            Assert.True(peak <= 4096, "tracked " + peak.ToString(CultureInfo.InvariantCulture));

            // The run is the one described: departures start at PublishTick + 1
            // with PlannedTick = the due tick, and they leave (Airborne).
            ulong first = rig.Id("P000a");
            FlightRecord fr = rig.Flight(first);
            Rec onStand = rig.Rec.Milestone(first, FlightMilestone.OnStand);
            Assert.Equal(fr.ScheduledTick - AirConst.TicksPerDay, onStand.Milestone.PlannedTick);
            Assert.Equal(fr.PublishTick + 1UL, onStand.Milestone.ActualTick);
            Assert.Contains(rig.Rec.All, r => r.FromAirside && r.IsMilestone(FlightMilestone.Airborne));
        }
    }
}
