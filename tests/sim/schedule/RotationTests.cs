using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>11 §11.3/§11.4/§11.5/§11.7: rotation links and TryGetRotation.</summary>
    public sealed class RotationTests
    {
        [Fact]
        public void test_rotation_links_are_mutual_same_day_and_arrival_first()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            int linked = 0, lone = 0;
            foreach (FlightId fid in rig.Schedule.PublishedFlights())
            {
                FlightRecord r = rig.Flight(fid.Value);
                if (!r.HasRotation)
                {
                    lone++;
                    Assert.Equal(r.Id, r.Rotation);
                    Assert.False(rig.Schedule.TryGetRotation(r.Id, out _));
                    continue;
                }

                linked++;
                Assert.True(rig.Schedule.TryGetRotation(r.Id, out FlightId other));
                Assert.Equal(r.Rotation, other);
                FlightRecord o = rig.Flight(other.Value);
                Assert.True(o.HasRotation);
                Assert.Equal(r.Id, o.Rotation);
                Assert.NotEqual(r.Kind, o.Kind);
                Assert.Equal(r.DayIndex, o.DayIndex);
                FlightRecord arr = r.Kind == MovementKind.Arrival ? r : o;
                FlightRecord dep = r.Kind == MovementKind.Arrival ? o : r;
                Assert.True(arr.ScheduledTick < dep.ScheduledTick);
            }

            Assert.Equal(2 * 198, linked);
            Assert.Equal(2 * 2, lone);
        }

        [Fact]
        public void test_rotation_resolves_within_own_day_for_repeat_daily_rows()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            OracleFlight dep1 = oracle.ByRef("NVA201", 1);
            FlightRecord r = rig.Flight(dep1.Id);
            Assert.Equal(oracle.ByRef("NVA200", 1).Id, r.Rotation.Value);
            Assert.True(r.Rotation.Value > SchedConst.DayStride && r.Rotation.Value < 2UL * SchedConst.DayStride);
        }

        [Fact]
        public void test_rotation_lone_flights_have_no_counterpart()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            foreach (string lone in new[] { "BRW100", "CTX199" })
            {
                FlightRecord r = rig.Flight(oracle.ByRef(lone, 0).Id);
                Assert.False(r.HasRotation);
                Assert.Equal(r.Id, r.Rotation);
                Assert.False(rig.Schedule.TryGetRotation(r.Id, out _));
            }
        }

        [Fact]
        public void test_rotation_unknown_flight_returns_false()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(1);
            Assert.False(rig.Schedule.TryGetRotation(new FlightId(999), out _));
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            Assert.True(rig.Schedule.TryGetRotation(new FlightId(oracle.ByRef("NVA201", 0).Id), out FlightId counterpart));
            Assert.Equal(oracle.ByRef("NVA200", 0).Id, counterpart.Value);
        }
    }
}
