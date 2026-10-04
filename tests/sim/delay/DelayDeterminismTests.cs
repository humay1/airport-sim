using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 02-determinism and 14 §14.13: same input, same trees, publications and
    /// hashes; chunking is invisible (08 §8.2); the hash covers the counter,
    /// records, retained intervals and nodes.
    /// </summary>
    public sealed class DelayDeterminismTests
    {
        private static (List<string> PerDay, List<string> Published, List<string> Checkpoints) Run(ulong seed)
        {
            var perDay = new List<string>();
            var run = new GeneratedRun(seed, Generator.SyntheticDay, model: false);
            run.RunDays(2, day =>
            {
                perDay.Add("hash " + run.Rig.Delay.ComputeStateHash().ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
                perDay.AddRange(Show.Snapshot(run.Rig.Delay));
            });
            var published = new List<string>();
            foreach ((EventEnvelope env, DelayEvent evt) in run.Rig.R.Delays)
            {
                published.Add(DelayModel.PublishedText(env.Tick, env.Cause.Id, evt.Node));
            }

            return (perDay, published, run.Rig.Sink.Describe());
        }

        [Fact]
        public void test_delay_determinism_same_seed_same_trees_and_hashes()
        {
            var a = Run(0x0024_0D0DUL);
            var b = Run(0x0024_0D0DUL);
            Assert.True(a.Published.Count > 100, "too little was published to compare: " + a.Published.Count);
            Assert.Equal(a.PerDay, b.PerDay);
            Assert.Equal(a.Published, b.Published);
            Assert.Equal(a.Checkpoints, b.Checkpoints);

            // A different stream gives a different state.
            var c = Run(0x0024_0D0EUL);
            Assert.NotEqual(a.PerDay[0], c.PerDay[0]);
        }

        [Fact]
        public void test_delay_determinism_chunking_is_invisible()
        {
            var rng = new SplitMix64(0x0024_C4C4UL);
            var s1 = new Script();
            var s2 = new Script();
            for (int day = 0; day <= 2; day++)
            {
                List<Move> moves = Generator.SyntheticDay(rng, day);
                ulong state = rng.Next();
                Generator.Day(new SplitMix64(state), moves, s1, 40);
                Generator.Day(new SplitMix64(state), moves, s2, 40);
            }

            var stepped = new DelayRig(s1);
            var chunked = new DelayRig(s2);
            const ulong End = 2UL * DConst.TicksPerDay;
            while (stepped.Host.CurrentTick < End)
            {
                stepped.Host.Step(1);
            }

            chunked.Host.Step((uint)End);
            Assert.Equal(Show.Snapshot(stepped.Delay), Show.Snapshot(chunked.Delay));
            Assert.Equal(stepped.Delay.ComputeStateHash(), chunked.Delay.ComputeStateHash());
            Assert.Equal(stepped.Sink.Describe(), chunked.Sink.Describe());
            Assert.True(stepped.R.Delays.Count > 100);
        }

        [Fact]
        public void test_delay_determinism_hash_tracks_records_intervals_and_nodes()
        {
            // Four streams, identical up to tick 1100 except for one event each.
            // At tick 1150 none of the extra events has changed a tree yet.
            static Script Base()
            {
                var s = new Script();
                s.Plan(0, 12, MovementKind.Departure);
                s.Milestone(1000, 12, FlightMilestone.OnStand, 1000);
                return s;
            }

            Script same = Base();
            Script withInterval = Base();
            withInterval.JobBlocked(1100, 12, JobKind.Fuel, ResourceKind.Vehicle, DelayCategory.Fuel, 3);
            Script withMissed = Base();
            withMissed.Missed(1100, 12, 2, 9);
            Script withFlight = Base();
            withFlight.Plan(1100, 13, MovementKind.Arrival);

            ulong Hash(Script s)
            {
                var rig = new DelayRig(s);
                rig.RunTo(1150);
                return rig.Delay.ComputeStateHash();
            }

            ulong baseHash = Hash(Base());
            Assert.Equal(baseHash, Hash(same));
            Assert.NotEqual(baseHash, Hash(withInterval));
            Assert.NotEqual(baseHash, Hash(withMissed));
            Assert.NotEqual(baseHash, Hash(withFlight));

            // A leaf changes it too: the same flight before and after a late Pushback.
            Script late = Base();
            late.Milestone(1300, 12, FlightMilestone.Pushback, 1200);
            var r = new DelayRig(late);
            r.RunTo(1300);
            ulong before = r.Delay.ComputeStateHash();
            r.RunThrough(1300);
            Assert.NotEqual(before, r.Delay.ComputeStateHash());
        }
    }
}
