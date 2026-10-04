using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 06 survives_save_load, 14 §14.14: tree, records, intervals and counter
    /// identical after a round trip mid-day, with intervals open across the
    /// save. There is no save seam yet (08 §8.8 "Save seam", 19 §19.5,
    /// Q-027), so the round trip takes the replay form the owner approved for
    /// determinism_save_load: the "save" is the input stream, and a fresh run
    /// to the save tick must reproduce the saved state exactly, then continue
    /// identically. Retained intervals and the counter are not queryable, so
    /// they are compared through the hash (§14.13 hashes both) and through
    /// what they produce after the save.
    /// </summary>
    public sealed class SurvivesTests
    {
        private const ulong Save = DConst.TicksPerDay + 6000UL;
        private const ulong Overlay = 990001UL;

        private static (GeneratedRun Run, Step Opener) Build()
        {
            var run = new GeneratedRun(0x0024_5A7EUL, Generator.SyntheticDay, model: false);
            run.GenerateThrough(2);

            // A departure whose Catering wait is open across the save tick and is
            // allocated only after it: [20000, 20600) against Pushback planned
            // 20300, actual 20700: late 400, owned 600, capped to 400.
            var s = new Script();
            s.Plan(19000, Overlay, MovementKind.Departure);
            s.Milestone(19500, Overlay, FlightMilestone.OnStand, 19500);
            Step opener = s.JobBlocked(20000, Overlay, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.JobUnblocked(20600, Overlay, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering, 2);
            s.Milestone(20700, Overlay, FlightMilestone.Pushback, 20300);
            run.Rig.Append(s);
            return (run, opener);
        }

        private static List<string> State(GeneratedRun run)
        {
            var lines = new List<string> { "hash " + run.Rig.Delay.ComputeStateHash().ToString("X16", CultureInfo.InvariantCulture) };
            lines.AddRange(Show.Snapshot(run.Rig.Delay));
            foreach ((EventEnvelope env, DelayEvent evt) in run.Rig.R.Delays)
            {
                lines.Add(DelayModel.PublishedText(env.Tick, env.Cause.Id, evt.Node));
            }

            return lines;
        }

        [Fact]
        public void test_survives_save_load()
        {
            (GeneratedRun original, Step opener) = Build();
            original.Rig.RunTo(Save);
            List<string> saved = State(original);
            FlightDelay atSave = original.Rig.Record(Overlay);
            Assert.False(atSave.Finalised);
            Assert.Equal(1, atSave.CheckpointsReached);
            Assert.Empty(original.Rig.Leaves(Overlay));

            (GeneratedRun replay, Step _) = Build();
            replay.Rig.RunTo(Save);
            Assert.Equal(saved, State(replay));

            // Both continue to the end of day 2, identically, and the interval
            // open across the save is allocated after it.
            original.RunDays(2);
            replay.RunDays(2);
            Assert.Equal(State(original), State(replay));
            DelayNode leaf = original.Rig.Leaf(Overlay, DelaySource.TurnaroundJobWait);
            Assert.Equal(400UL, leaf.Ticks);
            Assert.Equal(opener.Ref.Id, leaf.SourceEvent.Id);
            Assert.True(leaf.SourceEvent.Id.Tick < Save && leaf.CreatedAt > Save);
        }
    }
}
