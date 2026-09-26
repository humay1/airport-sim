using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.3: FlightId.Value = DayIndex * 100000 + RowOrdinal + 1, where
    /// RowOrdinal is the index after an ordinal sort of flight_ref.
    /// </summary>
    public sealed class FlightIdsTests
    {
        [Fact]
        public void test_flight_ids_are_independent_of_row_order()
        {
            var a = new HostRig(Fixture.Bytes(), record: false);
            var b = new HostRig(Fixture.Permuted(0x0008_0002UL), record: false);
            var c = new HostRig(Fixture.Permuted(0x0008_0003UL), record: false);
            a.RunTo(1);
            b.RunTo(1);
            c.RunTo(1);

            Assert.Equal(Describe.Ids(a.Schedule.PublishedFlights()), Describe.Ids(b.Schedule.PublishedFlights()));
            Assert.Equal(Describe.Ids(a.Schedule.PublishedFlights()), Describe.Ids(c.Schedule.PublishedFlights()));
            for (ulong id = 1; id <= 200; id++)
            {
                string expected = Describe.Record(a.Flight(id));
                Assert.Equal(expected, Describe.Record(b.Flight(id)));
                Assert.Equal(expected, Describe.Record(c.Flight(id)));
            }
        }

        [Fact]
        public void test_flight_ids_day_zero_are_1_to_200_in_ordinal_ref_order()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            var expected = new List<ulong>();
            for (ulong id = 1; id <= 200; id++)
            {
                expected.Add(id);
            }

            Assert.Equal(expected, Describe.Ids(rig.Schedule.PublishedFlights()));
            foreach (OracleFlight f in oracle.Flights)
            {
                Assert.Equal(f.Sched, rig.Flight(f.Id).ScheduledTick);
            }
        }

        [Fact]
        public void test_flight_ids_follow_ordinal_not_culture_order_of_flight_ref()
        {
            // Ordinal order: "A10" < "A9" < "B2" < "_x" < "a3" < "b1".
            byte[] csv = Csv.Of(
                Csv.Row("b1", "D", "10:00"),
                Csv.Row("B2", "D", "10:10"),
                Csv.Row("a3", "D", "10:20"),
                Csv.Row("_x", "D", "10:30"),
                Csv.Row("A10", "D", "10:40"),
                Csv.Row("A9", "D", "10:50"));
            var rig = new HostRig(csv, record: false);
            rig.RunTo(1);
            ulong[] expectedMinute = { 640, 650, 610, 630, 620, 600 };
            for (int i = 0; i < expectedMinute.Length; i++)
            {
                FlightRecord r = rig.Flight((ulong)i + 1UL);
                Assert.Equal(expectedMinute[i] * SchedConst.TicksPerMinute, r.ScheduledTick);
                Assert.Equal(0U, r.DayIndex);
            }

            Assert.False(rig.Schedule.TryGetFlight(new FlightId(7), out _));
        }

        [Fact]
        public void test_flight_ids_add_day_stride_for_each_repeat_daily_occurrence()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            var expected = new List<ulong>();
            for (ulong id = 1; id <= 200; id++)
            {
                expected.Add(id);
            }

            for (ulong id = 1; id <= 200; id++)
            {
                expected.Add(SchedConst.DayStride + id);
            }

            Assert.Equal(expected, Describe.Ids(rig.Schedule.PublishedFlights()));
            for (ulong id = 1; id <= 200; id++)
            {
                FlightRecord d0 = rig.Flight(id);
                FlightRecord d1 = rig.Flight(SchedConst.DayStride + id);
                Assert.Equal(1U, d1.DayIndex);
                Assert.Equal(d0.ScheduledTick + SchedConst.TicksPerDay, d1.ScheduledTick);
                Assert.Equal(d0.Kind, d1.Kind);
                Assert.Equal(d0.PaxCount, d1.PaxCount);
            }
        }

        [Fact]
        public void test_flight_ids_of_single_day_row_on_later_day_use_its_day()
        {
            byte[] csv = Csv.Of(
                Csv.Row("R2", "D", "12:00", day: "2"),
                Csv.Row("R1", "D", "12:00", day: "0"));
            var rig = new HostRig(csv, record: false);
            ulong std = (2UL * SchedConst.TicksPerDay) + 7200UL;
            rig.RunTo(std - SchedConst.PublishLead + 1UL);
            Assert.Equal(new List<ulong> { 1UL, (2UL * SchedConst.DayStride) + 2UL }, Describe.Ids(rig.Schedule.PublishedFlights()));
            FlightRecord r = rig.Flight((2UL * SchedConst.DayStride) + 2UL);
            Assert.Equal(2U, r.DayIndex);
            Assert.Equal(std, r.ScheduledTick);
            Assert.False(rig.Schedule.TryGetFlight(new FlightId(SchedConst.DayStride + 2UL), out _));
        }

        [Fact]
        public void test_flight_ids_unknown_id_is_not_found()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            Assert.True(rig.Schedule.TryGetFlight(new FlightId(1), out _));
            Assert.True(rig.Schedule.TryGetFlight(new FlightId(200), out _));
            Assert.False(rig.Schedule.TryGetFlight(new FlightId(0), out _));
            Assert.False(rig.Schedule.TryGetFlight(new FlightId(201), out _));
            Assert.False(rig.Schedule.TryGetFlight(new FlightId(SchedConst.DayStride), out _));
        }
    }
}
