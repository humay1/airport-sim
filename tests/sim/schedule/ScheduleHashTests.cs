using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9: hashed state, in declared order, is FixtureHash, the highest
    /// day materialised, published flights (Id, PublishTick, ScheduledTick)
    /// and pending injections (due tick, count, class index). §11.6: the
    /// hash is identical with and without sim.flow.
    /// </summary>
    public sealed class ScheduleHashTests
    {
        [Fact]
        public void test_schedule_hash_identical_with_and_without_flow_registered()
        {
            var without = new HostRig(Fixture.Bytes(), record: false);
            var with = new HostRig(Fixture.Bytes(), withFlow: true, record: false);
            Assert.Equal(without.Schedule.ComputeStateHash(), with.Schedule.ComputeStateHash());
            for (ulong t = 0; t < 2UL * SchedConst.TicksPerDay; t++)
            {
                without.Host.Step(1);
                with.Host.Step(1);
                ulong a = without.Schedule.ComputeStateHash();
                ulong b = with.Schedule.ComputeStateHash();
                Assert.True(a == b, "schedule hash differs after tick " + t.ToString(CultureInfo.InvariantCulture));
            }

            Assert.True(with.Flow!.InjectCalls > 0, "the flow-registered run must actually inject");
            Assert.Equal(without.Sink.Recorded.Count, with.Sink.Recorded.Count);
            for (int i = 0; i < without.Sink.Recorded.Count; i++)
            {
                // SystemHashes[0] is sim.schedule in both builds (08 §8.9).
                Assert.Equal(without.Sink.Recorded[i].SystemHashes[0], with.Sink.Recorded[i].SystemHashes[0]);
            }

            for (ulong id = 1; id <= 200; id++)
            {
                Assert.Equal(without.Schedule.PendingInjectionCount(new FlightId(id)), with.Schedule.PendingInjectionCount(new FlightId(id)));
            }
        }

        [Fact]
        public void test_schedule_hash_follows_declared_order()
        {
            byte[] bytes = Fixture.Bytes();
            var oracle = new ScheduleOracle(Fixture.Text(), 4);
            ulong fixtureHash = Fnv.Raw64(bytes);
            var rig = new DirectRig(bytes);
            ulong?[] samples = { null, 0UL, 1UL, 150UL, 299UL, 300UL, 850UL, 5000UL, 14399UL, 14400UL, 14401UL, 20000UL, 28799UL };
            foreach (ulong? last in samples)
            {
                if (last != null)
                {
                    rig.RunTo(last.Value + 1UL);
                }

                ulong actual = rig.Schedule.ComputeStateHash();
                ulong maxPublishedDay = 0;
                foreach (OracleFlight f in oracle.PublishedAfter(last))
                {
                    maxPublishedDay = f.Day > maxPublishedDay ? f.Day : maxPublishedDay;
                }

                // Item 2's timing and whether lists carry a length prefix are not
                // pinned by §11.9; every other byte of the stream is.
                var candidates = new List<string>();
                bool match = false;
                for (ulong day = maxPublishedDay; day <= 3UL; day++)
                {
                    foreach (bool prefix in new[] { false, true })
                    {
                        ulong expected = oracle.ExpectedHash(fixtureHash, last, day, prefix);
                        candidates.Add(expected.ToString("X16", CultureInfo.InvariantCulture));
                        match |= expected == actual;
                    }
                }

                Assert.True(match, string.Format(
                    CultureInfo.InvariantCulture,
                    "after tick {0}: hash {1:X16} matches no §11.9 layout ({2})",
                    last?.ToString(CultureInfo.InvariantCulture) ?? "none",
                    actual,
                    string.Join(",", candidates)));
            }
        }

        [Fact]
        public void test_schedule_hash_includes_fixture_bytes()
        {
            var a = new HostRig(Fixture.Bytes(), record: false);
            var b = new HostRig(Fixture.Permuted(0x0008_0004UL), record: false);
            Assert.NotEqual(a.Schedule.ComputeStateHash(), b.Schedule.ComputeStateHash());
            a.RunTo(SchedConst.TicksPerDay);
            b.RunTo(SchedConst.TicksPerDay);
            Assert.Equal(Describe.Ids(a.Schedule.PublishedFlights()), Describe.Ids(b.Schedule.PublishedFlights()));
            Assert.NotEqual(a.Schedule.ComputeStateHash(), b.Schedule.ComputeStateHash());
        }

        [Fact]
        public void test_schedule_hash_changes_only_when_state_changes()
        {
            byte[] bytes = Fixture.Bytes();
            var oracle = new ScheduleOracle(Fixture.Text(), 3);
            var busy = new HashSet<ulong>();
            foreach (OracleFlight f in oracle.Flights)
            {
                busy.Add(f.Publish);
            }

            foreach (OracleInjection inj in oracle.Injections)
            {
                busy.Add(inj.Due);
            }

            var rig = new DirectRig(bytes);
            rig.RunTo(1);
            ulong previous = rig.Schedule.ComputeStateHash();
            var sb = new StringBuilder();
            for (ulong t = 1; t < 2UL * SchedConst.TicksPerDay; t++)
            {
                rig.TickOnce();
                ulong now = rig.Schedule.ComputeStateHash();
                bool dayBoundary = t % SchedConst.TicksPerDay == 0UL;
                if (busy.Contains(t) && now == previous && sb.Length < 2000)
                {
                    sb.Append("tick ").Append(t).Append(" publishes or drains but the hash did not change\n");
                }

                if (!busy.Contains(t) && !dayBoundary && now != previous && sb.Length < 2000)
                {
                    sb.Append("tick ").Append(t).Append(" is idle but the hash changed\n");
                }

                previous = now;
            }

            Assert.True(sb.Length == 0, sb.ToString());
        }

        [Fact]
        public void test_schedule_hash_unaffected_by_queries()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            rig.RunTo(5000);
            Assert.True(rig.Schedule.PublishedFlights().Count > 200);            ulong before = rig.Schedule.ComputeStateHash();
            for (ulong id = 1; id <= 200; id++)
            {
                rig.Schedule.TryGetFlight(new FlightId(id), out _);
                rig.Schedule.TryGetRotation(new FlightId(id), out _);
                rig.Schedule.PendingInjectionCount(new FlightId(id));
            }

            rig.Schedule.MovementsBetween(0UL, 3UL * SchedConst.TicksPerDay, MovementKind.Departure);
            rig.Schedule.PublishedFlights();
            Assert.Equal(before, rig.Schedule.ComputeStateHash());
            Assert.Equal(before, rig.Schedule.ComputeStateHash());
        }

        [Fact]
        public void test_schedule_hash_independent_of_master_seed()
        {
            var a = new HostRig(Fixture.Bytes(), record: false, seed: 1UL);
            var b = new HostRig(Fixture.Bytes(), record: false, seed: 0xFFFF_FFFF_FFFF_FFFFUL);
            ulong initial = a.Schedule.ComputeStateHash();
            a.RunTo(2UL * SchedConst.TicksPerDay);
            Assert.NotEqual(initial, a.Schedule.ComputeStateHash());
            Assert.Equal(600, a.Schedule.PublishedFlights().Count);
            b.RunTo(2UL * SchedConst.TicksPerDay);
            Assert.Equal(48, a.Sink.Recorded.Count);
            for (int i = 0; i < a.Sink.Recorded.Count; i++)
            {
                Assert.Equal(a.Sink.Recorded[i].SystemHashes[0], b.Sink.Recorded[i].SystemHashes[0]);
            }

            Assert.Equal(a.Schedule.ComputeStateHash(), b.Schedule.ComputeStateHash());
        }
    }
}
