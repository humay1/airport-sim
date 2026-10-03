using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Schedule;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.2 and §12.12 "Tracked flights" (Q-085), in §12.13's pinned
    /// shape. TRACKED_FLIGHTS_CAPACITY is 4096. One runway at 1 per hour,
    /// only rotation-less arrivals, N a day with repeat_daily: no track is
    /// ever removed. Rank the arrivals by InboundAirborne tick
    /// max(0, STA - 1 200), then FlightId (the S2 order); A_k has rank k and
    /// tick t_k. At the end of every Tick before t_4097 the track count is
    /// exactly the number of arrivals started, each A_k with t_k < t_4097 has
    /// its InboundAirborne recorded at t_k, nothing throws and no Tick
    /// allocates. Step for t_4097 throws SimInvariantException naming A_4097;
    /// the partial state it leaves (08 §8.5a) has exactly 4 096 tracked and
    /// A_4097 not. N is 400, within §12.13's
    /// [100, 800], and its schedule starts one arrival per tick, so A_4096's
    /// tick is strictly before A_4097's.
    /// </summary>
    public sealed class TrackedFlightsTests
    {
        private const int PerDay = 400; // N <= 800
        private const int Capacity = 4096;

        /// <summary>
        /// Row i lands at 02:10 + 3i minutes (02:10 to 22:07), so its
        /// InboundAirborne is at 100 + 30i in its day: one start every 30 ticks,
        /// none at tick 0, every day's starts inside that day.
        /// </summary>
        private static byte[] Schedule()
        {
            var rows = new string[PerDay];
            for (int i = 0; i < PerDay; i++)
            {
                int minute = 130 + (3 * i);
                string hhmm = (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("D2", CultureInfo.InvariantCulture);
                rows[i] = Csv.Row("A" + i.ToString("D3", CultureInfo.InvariantCulture), "A", hhmm, repeat: "1");
            }

            return Csv.Of(rows);
        }

        /// <summary>A_k, k from 1: day (k-1)/400, row (k-1)%400; FlightId day·100 000 + row + 1 (11 §11.3, rows sort as A000..A399).</summary>
        private static (ulong Flight, ulong Tick) Rank(int k)
        {
            ulong day = (ulong)((k - 1) / PerDay);
            ulong row = (ulong)((k - 1) % PerDay);
            return ((day * AirConst.DayStride) + row + 1UL, (day * AirConst.TicksPerDay) + 100UL + (30UL * row));
        }

        /// <summary>The number of arrivals whose InboundAirborne tick is ≤ t.</summary>
        private static int StartedBy(ulong t)
        {
            ulong day = t / AirConst.TicksPerDay;
            ulong r = t % AirConst.TicksPerDay;
            int today = r < 100UL ? 0 : Math.Min(PerDay, (int)((r - 100UL) / 30UL) + 1);
            return ((int)day * PerDay) + today;
        }

        private static (ISimHost Host, IAirsideSystem Airside, List<(ulong Flight, ulong Tick)> Inbound) Build(bool record)
        {
            var inbound = new List<(ulong Flight, ulong Tick)>(record ? Capacity + 8 : 0);
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(0x5EED_0021UL, AirsideContent.Index(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = ScheduleFactory.CreateLoader().Load(Schedule(), "overflow.csv");
            IScheduleSystem schedule = ScheduleFactory.CreateSystem(b.Services, table, null);
            IAirsideSystem airside = AirsideFactory.CreateSystem(
                b.Services, FixtureLayout.Layout(1), new AirsideRules(10U, AirConst.FixtureDoorDelayMinutes), schedule, null, false);
            if (record)
            {
                b.Services.Events.Subscribe<FlightMilestoneReached>(new SystemId(AirConst.RecorderSystemId), (in EventEnvelope env, in FlightMilestoneReached evt, in TickContext ctx) =>
                {
                    if (env.Source.Value == AirConst.AirsideSystemId && evt.Milestone == FlightMilestone.InboundAirborne)
                    {
                        inbound.Add((evt.Flight.Value, ctx.Tick));
                    }
                });
            }

            b.Register(schedule);
            b.Register(airside);
            if (record)
            {
                b.Register(new ProbeSystem(AirConst.RecorderSystemId));
            }

            return (b.Build(), airside, inbound);
        }

        private static void AssertThrowAt(ISimHost host, IAirsideSystem airside, ulong tick, ulong flight)
        {
            Assert.Equal(tick, host.CurrentTick);
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => host.Step(1));
            Assert.Equal(tick, ex.Tick);
            var inner = Assert.IsType<SimInvariantException>(ex.InnerException); // the host wraps once, 08 §8.5a
            Assert.Equal(tick, inner.Tick);
            Assert.Contains(flight.ToString(CultureInfo.InvariantCulture), inner.Message, StringComparison.Ordinal);
            Assert.Contains("track", inner.Message, StringComparison.OrdinalIgnoreCase);

            // The partial state 08 §8.5a leaves, read through the queries: exactly
            // 4 096 tracked, A_4097 not. Events of the throwing tick are never
            // dispatched, so none is looked for.
            Assert.Equal(Capacity, airside.TrackedFlights().Count);
            Assert.False(airside.TryGetTrack(new FlightId(flight), out _), "A_4097 was tracked");
        }

        [Fact]
        [Trait("Category", "Slow")] // about 10.2 sim-days per run, twice: over L11a rule (a)'s 144 000 ticks
        public void test_tracked_flights_overflow_throws_sim_invariant()
        {
            (_, ulong t4096) = Rank(Capacity);
            (ulong a4097, ulong t4097) = Rank(Capacity + 1);
            Assert.Equal(1000097UL, a4097);             // day 10, row 96
            Assert.Equal(146980UL, t4097);              // 10 · 14 400 + 100 + 30 · 96
            Assert.Equal(146950UL, t4096);
            Assert.Equal(Capacity, StartedBy(t4097 - 1UL));
            Assert.Equal(Capacity + 1, StartedBy(t4097));

            // Run 1, the precondition at the end of every Tick before t_4097, and the throw.
            var (host, airside, inbound) = Build(record: true);
            while (host.CurrentTick < t4097)
            {
                ulong t = host.CurrentTick;
                host.Step(1);
                int count = airside.TrackedFlights().Count;
                Assert.True(count == StartedBy(t), string.Format(CultureInfo.InvariantCulture, "t={0}: {1} tracked, {2} started", t, count, StartedBy(t)));
            }

            // Every A_k with t_k < t_4097 has its InboundAirborne recorded at t_k.
            // With one start per tick here that is A_1..A_4096, in rank order.
            int recorded = 0;
            for (int k = 1; Rank(k).Tick < t4097; k++)
            {
                (ulong flight, ulong tick) = Rank(k);
                Assert.True(recorded < inbound.Count && inbound[recorded] == (flight, tick), string.Format(CultureInfo.InvariantCulture, "A_{0}: expected InboundAirborne of {1} at {2}", k, flight, tick));
                recorded++;
            }

            Assert.Equal(Capacity, recorded);
            Assert.Equal(recorded, inbound.Count);

            AssertThrowAt(host, airside, t4097, a4097);

            // Run 2, the same run unqueried: no Tick before t_4097 allocates. The
            // T-037 meter over Step windows holding no checkpoint tick (every
            // 600; day boundaries, where sim.schedule materialises, are among
            // them), after the first 600 ticks as warm-up (03).
            var (metered, meteredAirside, _) = Build(record: false);
            const ulong checkpoint = 600UL;
            metered.Step((uint)checkpoint);
            while (metered.CurrentTick < t4097)
            {
                metered.Step(1); // the checkpoint tick, unmetered
                ulong first = metered.CurrentTick;
                ulong end = Math.Min(first + checkpoint - 1UL, t4097);
                long start = Allocation.Start();
                metered.Step((uint)(end - first));
                long bytes = Allocation.Since(start);
                Assert.True(bytes == 0L, string.Format(CultureInfo.InvariantCulture, "ticks {0}-{1} allocated {2} bytes", first, end - 1UL, bytes));
            }

            AssertThrowAt(metered, meteredAirside, t4097, a4097);
        }
    }
}
