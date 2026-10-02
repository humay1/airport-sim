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
    /// TRACKED_FLIGHTS_CAPACITY (2048) flights are tracked; S2's arrival
    /// start that would add track 2 049 throws SimInvariantException at that
    /// tick, before it publishes, naming the flight and the tracked flights;
    /// and every earlier Tick allocates nothing.
    /// </summary>
    public sealed class TrackedFlightsTests
    {
        private const int Day0 = 1024;
        private const int Day1 = 1025; // Day0 + Day1 = 2 049 tracks
        private const int PerMinute = 30;

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
            // so no track is ever removed and the count only grows.
            //  - day 0: A0000-A1023, 30 a minute from 02:10 (STA 1300 on), so
            //    InboundAirborne runs at ticks 100-440 and none at tick 0;
            //  - day 1: B0000-B1024, 30 a minute from 03:00, published during
            //    day 0 (STA - 14 400), started at STA - 1 200.
            // Pending peaks at 1 025 (day 0 has all started by 440, before day
            // 1 publishes from 1800), under 2 048.
            var rows = new List<string>(Day0 + Day1);
            for (int i = 0; i < Day0; i++)
            {
                rows.Add(Csv.Row("A" + i.ToString("D4", CultureInfo.InvariantCulture), "A", Hhmm(130 + (i / PerMinute))));
            }

            for (int i = 0; i < Day1; i++)
            {
                rows.Add(Csv.Row("B" + i.ToString("D4", CultureInfo.InvariantCulture), "A", Hhmm(180 + (i / PerMinute)), day: "1"));
            }

            byte[] csv = Csv.Of(rows.ToArray());
            Dictionary<string, ulong> ids = Csv.Ids(csv);
            ulong overflowing = ids["B1024"];
            Assert.Equal(AirConst.DayStride + (ulong)Day0 + (ulong)Day1, overflowing); // RowOrdinal 2 048, day 1
            ulong sta = AirConst.TicksPerDay + AirConst.At(3, 0) + ((ulong)(1024 / PerMinute) * AirConst.TicksPerMinute);
            ulong throwTick = sta - AirConst.CruiseLead;
            Assert.Equal(15340UL, throwTick);

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
            // that hold no checkpoint tick (every 600; tick 14 400 is also
            // sim.schedule's day boundary), as 03 permits. The first window
            // (ticks 1-599) is the warm-up 03 requires before metering.
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
            Assert.Equal(2044, counter.Count); // 1 024 + 30 × 34 day-1 starts before the throwing tick
            Assert.Equal(2044, airside.TrackedFlights().Count);

            // Tick 15 340 starts B1020-B1024 in ascending FlightId; B1024 would
            // be track 2 049. The host wraps the module's throw once (08 §8.5a).
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => host.Step(1));
            Assert.Equal(throwTick, ex.Tick);
            var inner = Assert.IsType<SimInvariantException>(ex.InnerException);
            Assert.Equal(throwTick, inner.Tick);
            Assert.Contains(overflowing.ToString(CultureInfo.InvariantCulture), inner.Message, StringComparison.Ordinal);
            Assert.Contains("track", inner.Message, StringComparison.OrdinalIgnoreCase);

            // Nothing of the throwing tick was dispatched, and B1024's
            // InboundAirborne never reached the bus's subscribers.
            Assert.Equal(2044, counter.Count);
            Assert.NotEqual(overflowing, counter.LastFlight);
        }
    }
}
