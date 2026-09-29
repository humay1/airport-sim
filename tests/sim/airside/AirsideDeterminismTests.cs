using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 02-determinism: same fixture and seed, same events, checkpoints and
    /// hashes; chunking is invisible (08 §8.2); log volume never changes an
    /// outcome (08 §8.10); the master seed cannot reach sim.airside, which
    /// has no stream (12 §12.12). The save/load round trip takes the replay
    /// form (19 §19.5, Q-027): there is no save seam yet, so the "save" is
    /// fixture plus seed plus command log, and a fresh run must reproduce
    /// the saved state exactly.
    /// </summary>
    public sealed class AirsideDeterminismTests
    {
        private const int AirsideIndex = 1; // registered: schedule, airside, flow, recorder

        private static HostRig Rig(ulong seed = 0x5EED_0021UL, ISimLog? log = null)
        {
            return new HostRig(ScheduleFixture.Bytes(), flow: new RuleFlow(), seed: seed, log: log);
        }

        /// <summary>Everything IAirsideSystem exposes, as text.</summary>
        internal static List<string> Snapshot(IAirsideSystem a)
        {
            var s = new List<string>();
            s.Add("hash=" + a.ComputeStateHash().ToString("X16", CultureInfo.InvariantCulture));
            s.Add("free=" + Show.Ids(a.FreeStands()));
            foreach (RunwayDef r in a.Layout().Runways)
            {
                s.Add("rwy" + r.Id.Value.ToString(CultureInfo.InvariantCulture) + " q=" + a.RunwayQueueLength(r.Id).ToString(CultureInfo.InvariantCulture));
            }

            foreach (StandDef d in a.Layout().Stands)
            {
                a.TryGetStand(d.Id, out StandState st);
                s.Add("stand" + d.Id.Value.ToString(CultureInfo.InvariantCulture) + "=" + Show.Opt(st.Occupant, x => x.Value));
            }

            foreach (FlightId f in a.TrackedFlights())
            {
                a.TryGetTrack(f, out AircraftTrack t);
                s.Add(Show.Track(t));
            }

            return s;
        }

        [Fact]
        public void test_airside_determinism_same_fixture_same_events_checkpoints_and_hash()
        {
            HostRig a = Rig();
            HostRig b = Rig();
            a.RunTo(AirConst.TicksPerDay);
            b.RunTo(AirConst.TicksPerDay);

            Assert.NotEmpty(a.Rec.Of<AircraftHeldForRunway>());
            Assert.NotEmpty(a.Rec.Of<DepartureHeldForPassengers>());
            Assert.Equal(24, a.Sink.Recorded.Count);
            Assert.Equal(a.Rec.Trace(), b.Rec.Trace());
            Assert.Equal(a.Sink.Describe(), b.Sink.Describe());
            Assert.Equal(Snapshot(a.Airside), Snapshot(b.Airside));
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());
        }

        [Fact]
        public void test_airside_determinism_chunked_stepping_matches_one_step()
        {
            HostRig one = Rig();
            HostRig chunked = Rig();
            one.Host.Step((uint)AirConst.TicksPerDay);
            uint[] sizes = { 1U, 7U, 599U, 1000U, 13U, 2U, 3571U };
            int i = 0;
            while (chunked.Host.CurrentTick < AirConst.TicksPerDay)
            {
                ulong left = AirConst.TicksPerDay - chunked.Host.CurrentTick;
                uint n = sizes[i++ % sizes.Length];
                chunked.Host.Step(n < left ? n : (uint)left);
            }

            Assert.Equal(one.Rec.Trace(), chunked.Rec.Trace());
            Assert.Equal(one.Sink.Describe(), chunked.Sink.Describe());
            Assert.Equal(Snapshot(one.Airside), Snapshot(chunked.Airside));
        }

        [Fact]
        public void test_airside_determinism_replay_reproduces_saved_state()
        {
            const ulong saveAt = 7000UL;
            HostRig original = Rig();
            original.Host.TrySubmit(Payload.ReassignCommand(5000UL, 1UL, FixtureLayout.S4), out _);
            original.RunTo(saveAt);
            List<string> saved = Snapshot(original.Airside);
            ulong savedWorld = original.Host.WorldStateHash();
            IReadOnlyList<Command> log = original.Host.CommandLogSince(0UL);
            Assert.NotEmpty(original.Airside.TrackedFlights());
            original.RunTo(AirConst.TicksPerDay);

            HostRig replay = Rig();
            foreach (Command c in log)
            {
                Assert.True(replay.Host.TrySubmit(new Command(c.Tick, c.Issuer, c.Kind, c.Payload), out _));
            }

            replay.RunTo(saveAt);
            Assert.Equal(saved, Snapshot(replay.Airside));
            Assert.Equal(savedWorld, replay.Host.WorldStateHash());
            replay.RunTo(AirConst.TicksPerDay);
            Assert.Equal(original.Sink.Describe(), replay.Sink.Describe());
            Assert.Equal(Snapshot(original.Airside), Snapshot(replay.Airside));
        }

        [Fact]
        public void test_airside_determinism_log_sink_does_not_change_outcome()
        {
            var capture = new CapturingLog();
            HostRig quiet = Rig();
            HostRig loud = Rig(log: capture);

            // A no-op command (unknown flight) gives the handler something to log.
            quiet.Host.TrySubmit(Payload.ReassignCommand(5000UL, 999UL, FixtureLayout.S4), out _);
            loud.Host.TrySubmit(Payload.ReassignCommand(5000UL, 999UL, FixtureLayout.S4), out _);
            quiet.RunTo(AirConst.TicksPerDay);
            loud.RunTo(AirConst.TicksPerDay);

            Assert.NotEmpty(capture.Lines);
            Assert.Equal(quiet.Rec.Trace(), loud.Rec.Trace());
            Assert.Equal(quiet.Sink.Describe(), loud.Sink.Describe());
        }

        [Fact]
        public void test_airside_determinism_master_seed_does_not_reach_airside_hash()
        {
            HostRig a = Rig(seed: 1UL);
            HostRig b = Rig(seed: 0xFFFF_FFFF_FFFF_FFFFUL);
            a.RunTo(AirConst.TicksPerDay);
            b.RunTo(AirConst.TicksPerDay);

            Assert.Equal(a.Sink.Recorded.Count, b.Sink.Recorded.Count);
            for (int i = 0; i < a.Sink.Recorded.Count; i++)
            {
                Assert.Equal(a.Sink.Recorded[i].SystemHashes[AirsideIndex], b.Sink.Recorded[i].SystemHashes[AirsideIndex]);
            }

            Assert.Equal(a.Airside.ComputeStateHash(), b.Airside.ComputeStateHash());
            Assert.Equal(a.Rec.Trace(), b.Rec.Trace());
        }
    }
}
