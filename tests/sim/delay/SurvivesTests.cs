using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>
    /// 06 survives_save_load, 14 §14.14: tree, records, intervals and counter
    /// identical after a round trip mid-day, with intervals open across the
    /// save. Until sim.save exists (Q-109) the round trip takes the replay
    /// form the owner approved for determinism_save_load (19 §19.5): the
    /// "save" is the input stream to the save tick, and a fresh run to that
    /// tick must give an equal ComputeStateHash (records, retained intervals,
    /// counter: §14.13), equal answers to every §14.10 query and an equal
    /// DelayEvent sequence; both runs then continue and must stay equal, with
    /// an interval open at the save allocated at a checkpoint after it.
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
            var flights = new List<string>();
            foreach (FlightId f in run.Rig.Delay.RetainedFlights())
            {
                flights.Add(f.Value.ToString(CultureInfo.InvariantCulture));
            }

            lines.Add("retained " + string.Join(",", flights));
            lines.AddRange(Show.Snapshot(run.Rig.Delay));

            // Every §14.10 node query, removed and pruned ids included, up to
            // past the highest id handed out so far.
            ulong max = 0UL;
            foreach (DelayNode n in Invariants.AllNodes(run.Rig.Delay))
            {
                max = n.Id.Value > max ? n.Id.Value : max;
            }

            for (ulong id = 0UL; id <= max + 2UL; id++)
            {
                lines.Add(run.Rig.Delay.TryGetNode(new DelayEventId(id), out DelayNode node) ? Show.Node(node) : "no node " + id.ToString(CultureInfo.InvariantCulture));
            }

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

            // Q-109: both continue past the save, staying equal at every sim-hour
            // to the end of day 2, and the interval open across the save is
            // allocated at a checkpoint after it.
            for (ulong t = Save + 600UL; t <= 3UL * DConst.TicksPerDay; t += 600UL)
            {
                original.Rig.RunTo(t);
                replay.Rig.RunTo(t);
                original.Fail();
                replay.Fail();
                Assert.Equal(State(original), State(replay));
            }

            DelayNode leaf = original.Rig.Leaf(Overlay, DelaySource.TurnaroundJobWait);
            Assert.Equal(400UL, leaf.Ticks);
            Assert.Equal(opener.Ref.Id, leaf.SourceEvent.Id);
            Assert.True(leaf.SourceEvent.Id.Tick < Save && leaf.CreatedAt > Save);
        }
    }
}
