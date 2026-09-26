using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.7: MovementsBetween filters on ScheduledTick in
    /// [fromInclusive, toExclusive), by kind, ascending ScheduledTick then
    /// FlightId, and returns published and unpublished flights alike.
    /// </summary>
    public sealed class MovementsBetweenTests
    {
        private static List<ulong> Expected(ScheduleOracle oracle, ulong from, ulong to, MovementKind kind)
        {
            var list = new List<OracleFlight>();
            foreach (OracleFlight f in oracle.Flights)
            {
                if (f.Row.Kind == kind && f.Sched >= from && f.Sched < to)
                {
                    list.Add(f);
                }
            }

            list.Sort((a, b) => a.Sched != b.Sched ? a.Sched.CompareTo(b.Sched) : a.Id.CompareTo(b.Id));
            return list.ConvertAll(f => f.Id);
        }

        [Fact]
        public void test_movements_between_returns_unpublished_day_zero_flights_before_first_tick()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            Assert.Empty(rig.Schedule.PublishedFlights());
            List<ulong> deps = Describe.Ids(rig.Schedule.MovementsBetween(0UL, SchedConst.TicksPerDay, MovementKind.Departure));
            List<ulong> arrs = Describe.Ids(rig.Schedule.MovementsBetween(0UL, SchedConst.TicksPerDay, MovementKind.Arrival));
            Assert.Equal(100, deps.Count);
            Assert.Equal(100, arrs.Count);
            Assert.Equal(Expected(oracle, 0UL, SchedConst.TicksPerDay, MovementKind.Departure), deps);
            Assert.Equal(Expected(oracle, 0UL, SchedConst.TicksPerDay, MovementKind.Arrival), arrs);
        }

        [Fact]
        public void test_movements_between_orders_equal_ticks_by_flight_id()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            // CTX249 and CTX259 both depart at 11:09; CTX253 and NVA273 both at 11:20.
            ulong from = 669UL * SchedConst.TicksPerMinute;
            ulong to = (680UL * SchedConst.TicksPerMinute) + 1UL;
            List<ulong> got = Describe.Ids(rig.Schedule.MovementsBetween(from, to, MovementKind.Departure));
            Assert.Equal(Expected(oracle, from, to, MovementKind.Departure), got);
            Assert.Equal(5, got.Count);
            Assert.True(oracle.ByRef("CTX249", 0).Id < oracle.ByRef("CTX259", 0).Id);
            Assert.Equal(oracle.ByRef("CTX249", 0).Id, got[0]);
            Assert.Equal(oracle.ByRef("CTX259", 0).Id, got[1]);
        }

        [Fact]
        public void test_movements_between_is_half_open_on_scheduled_tick()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            OracleFlight f = oracle.ByRef("NVA201", 0);
            Assert.Equal(new List<ulong> { f.Id }, Describe.Ids(rig.Schedule.MovementsBetween(f.Sched, f.Sched + 1UL, MovementKind.Departure)));
            Assert.Empty(rig.Schedule.MovementsBetween(f.Sched - 1UL, f.Sched, MovementKind.Departure));
            Assert.Empty(rig.Schedule.MovementsBetween(f.Sched + 1UL, f.Sched + 2UL, MovementKind.Departure));
            Assert.Empty(rig.Schedule.MovementsBetween(f.Sched, f.Sched + 1UL, MovementKind.Arrival));
            Assert.Empty(rig.Schedule.MovementsBetween(f.Sched, f.Sched, MovementKind.Departure));
        }

        [Fact]
        public void test_movements_between_filters_on_scheduled_not_publish_tick()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            // Every day-0 flight published at tick 0, but none is scheduled then.
            Assert.Empty(rig.Schedule.MovementsBetween(0UL, 1UL, MovementKind.Departure));
            Assert.Empty(rig.Schedule.MovementsBetween(0UL, 1UL, MovementKind.Arrival));
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            Assert.Equal(Expected(oracle, 0UL, 3600UL, MovementKind.Arrival), Describe.Ids(rig.Schedule.MovementsBetween(0UL, 3600UL, MovementKind.Arrival)));
        }

        [Fact]
        public void test_movements_between_spans_repeat_daily_days()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            ulong from = SchedConst.TicksPerDay - 3000UL;
            ulong to = SchedConst.TicksPerDay + 9000UL;
            Assert.Equal(Expected(oracle, from, to, MovementKind.Departure), Describe.Ids(rig.Schedule.MovementsBetween(from, to, MovementKind.Departure)));
            Assert.Equal(Expected(oracle, from, to, MovementKind.Arrival), Describe.Ids(rig.Schedule.MovementsBetween(from, to, MovementKind.Arrival)));
        }
    }
}
