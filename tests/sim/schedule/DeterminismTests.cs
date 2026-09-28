using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 02-determinism: same fixture and seed, same checkpoints, events and
    /// injections; chunking is invisible (08 §8.2). The save/load round trip
    /// takes the replay form of 19 §19.5 (Q-027): sim.schedule has no save
    /// seam, so the "save" is the fixture and seed, and a fresh run must match.
    /// </summary>
    public sealed class DeterminismTests
    {
        private static List<string> Injections(HostRig rig)
        {
            return rig.Flow!.Injections.ConvertAll(i => i.ToString());
        }

        [Fact]
        public void test_determinism_same_fixture_same_checkpoints_events_and_injections()
        {
            var a = new HostRig(Fixture.Bytes(), withFlow: true);
            var b = new HostRig(Fixture.Bytes(), withFlow: true);
            a.RunTo(2UL * SchedConst.TicksPerDay);
            b.RunTo(2UL * SchedConst.TicksPerDay);
            Assert.Equal(48, a.Sink.Recorded.Count);
            Assert.Equal(1200, a.Events!.Events.Count);
            Assert.NotEmpty(a.Flow!.Injections);
            Assert.Equal(a.Sink.Describe(), b.Sink.Describe());
            Assert.Equal(a.Events!.Trace(), b.Events!.Trace());
            Assert.Equal(Injections(a), Injections(b));
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());
        }

        [Fact]
        public void test_determinism_chunked_stepping_matches_one_step()
        {
            var one = new HostRig(Fixture.Bytes(), withFlow: true);
            var chunked = new HostRig(Fixture.Bytes(), withFlow: true);
            one.Host.Step((uint)(2UL * SchedConst.TicksPerDay));
            var rng = new SplitMix64(0x0008_C4A7UL);
            while (chunked.Host.CurrentTick < 2UL * SchedConst.TicksPerDay)
            {
                ulong left = (2UL * SchedConst.TicksPerDay) - chunked.Host.CurrentTick;
                ulong n = 1UL + (rng.Next() % 997UL);
                chunked.Host.Step((uint)(n < left ? n : left));
            }

            Assert.Equal(1200, one.Events!.Events.Count);
            Assert.NotEmpty(one.Flow!.Injections);
            Assert.Equal(one.Sink.Describe(), chunked.Sink.Describe());
            Assert.Equal(one.Events!.Trace(), chunked.Events!.Trace());
            Assert.Equal(Injections(one), Injections(chunked));
            Assert.Equal(one.Schedule.ComputeStateHash(), chunked.Schedule.ComputeStateHash());
        }

        [Fact]
        public void test_determinism_replay_from_fixture_reproduces_saved_state()
        {
            const ulong saveAt = 10000UL;
            const ulong end = 2UL * SchedConst.TicksPerDay;
            var original = new HostRig(Fixture.Bytes(), withFlow: true);
            ulong initialHash = original.Schedule.ComputeStateHash();
            original.RunTo(saveAt);
            ulong savedHash = original.Schedule.ComputeStateHash();
            Assert.NotEqual(initialHash, savedHash);
            Assert.True(original.Schedule.PublishedFlights().Count > 200);
            ulong savedWorld = original.Host.WorldStateHash();
            var savedPending = new List<int>();
            for (ulong id = 1; id <= 200; id++)
            {
                savedPending.Add(original.Schedule.PendingInjectionCount(new FlightId(SchedConst.DayStride + id)));
            }

            original.RunTo(end);

            var replay = new HostRig(Fixture.Bytes(), withFlow: true);
            replay.RunTo(saveAt);
            Assert.Equal(savedHash, replay.Schedule.ComputeStateHash());
            Assert.Equal(savedWorld, replay.Host.WorldStateHash());
            for (ulong id = 1; id <= 200; id++)
            {
                Assert.Equal(savedPending[(int)id - 1], replay.Schedule.PendingInjectionCount(new FlightId(SchedConst.DayStride + id)));
            }

            replay.RunTo(end);
            Assert.Equal(original.Sink.Describe(), replay.Sink.Describe());
            Assert.Equal(original.Schedule.ComputeStateHash(), replay.Schedule.ComputeStateHash());
        }

        [Fact]
        public void test_determinism_hash_differs_between_distinct_fixtures()
        {
            var a = new HostRig(Csv.Of(Csv.Row("X1", "D", "12:00", pax: "100")), record: false);
            var b = new HostRig(Csv.Of(Csv.Row("X1", "D", "12:00", pax: "101")), record: false);
            a.RunTo(1);
            b.RunTo(1);
            Assert.NotEqual(a.Schedule.ComputeStateHash(), b.Schedule.ComputeStateHash());
        }
    }
}
