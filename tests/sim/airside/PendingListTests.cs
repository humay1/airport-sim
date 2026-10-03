using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.11 "How flights are found": the pending list, fed by a day-0
    /// read in CreateSystem and by the FlightPlanPublished handler, bounded
    /// by PENDING_FLIGHTS_CAPACITY (2048, §12.2). Overflow in the handler,
    /// during a tick, throws SimInvariantException; in the day-0 read, with
    /// no tick, ArgumentException for parameter schedule, its message
    /// starting "sim.airside: " and naming the capacity. Removal is exact,
    /// so a max-tier schedule never overflows.
    /// </summary>
    public sealed class PendingListTests
    {
        private static byte[] Arrivals(int count, string day, Func<int, string> sched)
        {
            var rows = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                rows.Add(Csv.Row("A" + i.ToString("D5", CultureInfo.InvariantCulture), "A", sched(i), day: day));
            }

            return Csv.Of(rows.ToArray());
        }

        /// <summary>70 rows per minute from 00:00, so a publication tick carries 140 schedule events.</summary>
        private static string Spread(int i)
        {
            int minute = i / 70;
            return (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("D2", CultureInfo.InvariantCulture);
        }

        [Fact]
        public void test_pending_list_overflow_in_day_zero_read_throws_argument_exception()
        {
            byte[] over = Arrivals(AirConst.PendingCapacity + 1, "0", i => "12:00");
            ArgumentException ex = Assert.Throws<ArgumentException>(() => new HostRig(over, record: false));
            Assert.Equal("schedule", ex.ParamName);
            Assert.StartsWith("sim.airside: ", ex.Message, StringComparison.Ordinal);
            Assert.Contains("2048", ex.Message, StringComparison.Ordinal);

            // Exactly the capacity constructs. It is not stepped: tick 0 alone
            // would publish 4096 schedule events, the per-tick limit.
            var full = new HostRig(Arrivals(AirConst.PendingCapacity, "0", i => "12:00"), record: false);
            Assert.Equal(0UL, full.Host.CurrentTick);
        }

        [Fact]
        public void test_pending_list_overflow_in_publication_handler_throws_sim_invariant()
        {
            // Day-1 arrivals publish at STA - 14400, from tick 0, 70 per minute,
            // and start only at STA - 1200 (tick 13200 on). The 2049th append
            // comes with minute 29's publication, at tick 290.
            HostRig over = new HostRig(Arrivals(AirConst.PendingCapacity + 1, "1", Spread), record: false);
            over.RunTo(290UL);
            SimInvariantException ex = Assert.Throws<SimInvariantException>(() => over.Host.Step(1));
            Assert.Equal(290UL, ex.Tick);
            Assert.IsType<SimInvariantException>(ex.InnerException);

            HostRig full = new HostRig(Arrivals(AirConst.PendingCapacity, "1", Spread), record: false);
            full.RunTo(400UL);
            Assert.Equal(400UL, full.Host.CurrentTick);
        }

        [Fact]
        public void test_pending_list_does_not_overflow_over_three_max_tier_days()
        {
            // 800 repeat_daily movements; without exact removal the list fills by about day 3.
            var rig = new HostRig(ScheduleFixture.MaxTier(), layout: MaxTierLayout.Layout(), record: false);
            rig.RunTo(3UL * AirConst.TicksPerDay);
            Assert.Equal(3UL * AirConst.TicksPerDay, rig.Host.CurrentTick);
            Assert.NotEmpty(rig.Airside.TrackedFlights());
        }
    }
}
