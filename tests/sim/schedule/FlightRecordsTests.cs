using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>11 §11.3/§11.4/§11.5: each FlightRecord field from its fixture row.</summary>
    public sealed class FlightRecordsTests
    {
        [Fact]
        public void test_flight_records_match_fixture_rows_on_days_zero_and_one()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            Assert.Equal(400, oracle.Flights.Count);
            foreach (OracleFlight f in oracle.Flights)
            {
                Assert.Equal(f.Describe(), Describe.Record(rig.Flight(f.Id)));
            }
        }

        [Fact]
        public void test_flight_records_publish_tick_is_scheduled_minus_lead_clamped()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            for (ulong id = 1; id <= 200; id++)
            {
                FlightRecord d0 = rig.Flight(id);
                Assert.True(d0.ScheduledTick < SchedConst.TicksPerDay);
                Assert.Equal(0UL, d0.PublishTick);
                FlightRecord d1 = rig.Flight(SchedConst.DayStride + id);
                Assert.Equal(d1.ScheduledTick - SchedConst.PublishLead, d1.PublishTick);
            }
        }

        [Fact]
        public void test_flight_records_min_turnaround_is_whole_minutes_in_fx()
        {
            var rig = new HostRig(Csv.Of(Csv.Pair("AAA1", "AAA2", "10:00", "11:00")), record: false);
            rig.RunTo(1);
            Assert.Equal(Fx.FromInt(35), rig.Flight(1).MinTurnaround);
            Assert.Equal(Fx.FromInt(35), rig.Flight(2).MinTurnaround);
        }

        [Fact]
        public void test_flight_records_airline_is_fnv1a32_of_code()
        {
            var rig = new HostRig(Csv.Of(Csv.Row("X1", "D", "12:00", airline: "NVA"), Csv.Row("X2", "D", "12:00", airline: "BRW")), record: false);
            rig.RunTo(1);
            Assert.Equal(Fnv.Airline32("NVA"), rig.Flight(1).Airline.Value);
            Assert.Equal(Fnv.Airline32("BRW"), rig.Flight(2).Airline.Value);
            Assert.Equal(0x4CB5598CU, Fnv.Airline32("NVA"));
        }

        [Fact]
        public void test_flight_records_arrivals_carry_zero_pax()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            int arrivals = 0;
            for (ulong id = 1; id <= 200; id++)
            {
                FlightRecord r = rig.Flight(id);
                if (r.Kind == MovementKind.Arrival)
                {
                    arrivals++;
                    Assert.Equal(0, r.PaxCount);
                }
                else
                {
                    Assert.True(r.PaxCount > 0);
                    Assert.True(r.EntryNode.Value > 0U);
                }
            }

            Assert.Equal(100, arrivals);
        }
    }
}
