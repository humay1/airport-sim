using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 02-determinism: same fixture and seed, same events, checkpoints and
    /// hashes; chunking is invisible (08 §8.2). The save/load round trip takes
    /// the replay form (19 §19.5, Q-027): there is no save seam yet, so the
    /// "save" is fixture plus seed, and a fresh run must reproduce the saved
    /// state exactly. 13 §13.10: the hash covers vehicles and jobs.
    /// </summary>
    public sealed class TurnaroundDeterminismTests
    {
        private const int TurnaroundIndex = 2; // registered: schedule, driver, turnaround, recorder

        private static Rig Rig()
        {
            return new Rig(ScheduleFixture.Bytes(), Phase1Fixture.Setup(), driveDays: 1);
        }

        /// <summary>Everything ITurnaroundSystem exposes, as text.</summary>
        internal static List<string> Snapshot(Rig rig)
        {
            ITurnaroundSystem t = rig.Turnaround;
            var s = new List<string>();
            s.Add("hash=" + t.ComputeStateHash().ToString("X16", CultureInfo.InvariantCulture));
            foreach (VehicleDef d in Phase1Fixture.Fleet())
            {
                t.TryGetVehicle(d.Id, out VehicleState v);
                s.Add("vehicle " + d.Id.Value + " " + v.Kind + " " + (v.Assignment.HasValue ? v.Assignment.Value.Value.ToString(CultureInfo.InvariantCulture) : "-"));
            }

            var flights = new List<ulong>(rig.Moves.Keys);
            flights.Sort();
            foreach (ulong f in flights)
            {
                foreach (JobId id in t.JobsForFlight(new FlightId(f)))
                {
                    t.TryGetJob(id, out TurnaroundJob j);
                    s.Add(Show.Job(j));
                }
            }

            return s;
        }

        [Fact]
        public void test_turnaround_determinism_same_fixture_same_events_checkpoints_and_hash()
        {
            Rig a = Rig();
            Rig b = Rig();
            a.RunTo(TConst.TicksPerDay);
            b.RunTo(TConst.TicksPerDay);

            Assert.NotEmpty(a.Rec.Of<TurnaroundJobBlocked>());
            Assert.Equal(24, a.Sink.Recorded.Count);
            Assert.Equal(a.Rec.Trace(), b.Rec.Trace());
            Assert.Equal(a.Sink.Describe(), b.Sink.Describe());
            Assert.Equal(Snapshot(a), Snapshot(b));
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());
        }

        [Fact]
        public void test_turnaround_determinism_chunked_stepping_matches_one_step()
        {
            Rig one = Rig();
            Rig chunked = Rig();
            one.Host.Step((uint)TConst.TicksPerDay);
            uint[] sizes = { 1U, 7U, 599U, 1000U, 13U, 2U, 3571U };
            int i = 0;
            while (chunked.Host.CurrentTick < TConst.TicksPerDay)
            {
                ulong left = TConst.TicksPerDay - chunked.Host.CurrentTick;
                uint n = sizes[i++ % sizes.Length];
                chunked.Host.Step(n < left ? n : (uint)left);
            }

            Assert.NotEmpty(one.Rec.Of<TurnaroundJobStarted>());
            Assert.Equal(one.Rec.Trace(), chunked.Rec.Trace());
            Assert.Equal(one.Sink.Describe(), chunked.Sink.Describe());
            Assert.Equal(Snapshot(one), Snapshot(chunked));
        }

        [Fact]
        public void test_turnaround_determinism_replay_reproduces_saved_state_mid_day()
        {
            // "Save" at 07:30, mid-bank, with jobs blocked and vehicles busy.
            const ulong save = 4500UL;
            Rig original = Rig();
            original.RunTo(save);
            List<string> saved = Snapshot(original);
            ulong savedWorld = original.Host.WorldStateHash();
            Assert.Contains(saved, line => line.Contains(" Blocked "));

            Rig replay = Rig();
            replay.RunTo(save);
            Assert.Equal(saved, Snapshot(replay));
            Assert.Equal(savedWorld, replay.Host.WorldStateHash());

            original.RunTo(TConst.TicksPerDay);
            replay.RunTo(TConst.TicksPerDay);
            Assert.Equal(Snapshot(original), Snapshot(replay));
            Assert.Equal(original.Sink.Describe(), replay.Sink.Describe());
        }

        [Fact]
        public void test_turnaround_determinism_hash_tracks_jobs_and_vehicles()
        {
            // 13 §13.10: vehicles and jobs are hashed. Nothing on stand yet and
            // the same fleet: equal. A job created: different. Same jobs, a
            // different vehicle holding one: different.
            var csv = Csv.Of(Csv.Row("D1", "D", "06:00"));
            var a = new Rig(csv, Setups.Unit(Setups.Plenty(1)), onStand: new[] { ("D1", 10UL) });
            var b = new Rig(csv, Setups.Unit(Setups.Plenty(1)), onStand: new[] { ("D1", 20UL) });
            a.RunTo(10UL);
            b.RunTo(10UL);
            ulong before = a.Turnaround.ComputeStateHash();
            Assert.Equal(before, b.Turnaround.ComputeStateHash());
            Assert.Equal(a.Sink.Recorded[0].SystemHashes[TurnaroundIndex], b.Sink.Recorded[0].SystemHashes[TurnaroundIndex]);

            a.RunTo(11UL);
            Assert.NotEqual(before, a.Turnaround.ComputeStateHash());

            // Two crews, ids swapped between kinds: D1's CabinClean holds a
            // different VehicleId, everything else is equal.
            JobDef[] jobs = Setups.UnitJobs();
            var c = new Rig(csv, Setups.Of(jobs, Setups.Fleet((1, VehicleKind.CleaningCrew), (2, VehicleKind.CateringTruck), (3, VehicleKind.FuelTruck), (4, VehicleKind.BaggageTractor), (5, VehicleKind.PushbackTug))), onStand: new[] { ("D1", 10UL) });
            var d = new Rig(csv, Setups.Of(jobs, Setups.Fleet((2, VehicleKind.CleaningCrew), (1, VehicleKind.CateringTruck), (3, VehicleKind.FuelTruck), (4, VehicleKind.BaggageTractor), (5, VehicleKind.PushbackTug))), onStand: new[] { ("D1", 10UL) });
            c.RunTo(11UL);
            d.RunTo(11UL);
            Assert.Equal(JobStatus.Active, c.Job("D1", JobKind.CabinClean).Status);
            Assert.NotEqual(c.Job("D1", JobKind.CabinClean).Vehicle, d.Job("D1", JobKind.CabinClean).Vehicle);
            Assert.NotEqual(c.Turnaround.ComputeStateHash(), d.Turnaround.ComputeStateHash());
        }
    }
}
