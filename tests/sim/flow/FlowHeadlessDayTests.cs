using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 07 "Testing": one full simulated day, headless, on the Phase 0 fixture,
    /// asserting 09's invariants after every tick.
    /// </summary>
    public sealed class FlowHeadlessDayTests
    {
        [Fact]
        public void test_flow_headless_day_keeps_every_invariant()
        {
            var rig = Rig.Fixture(DayScenario.Content());
            var day = new DayScenario();
            var open = new HashSet<ulong>();
            long missed = 0;
            int missedSeen = 0;
            int nodes = rig.World.Nodes().Count;
            day.Run(rig, 0xDA7_DA7UL, (int)SimConstants.TICKS_PER_SIM_DAY, afterTick: t =>
            {
                FlowEvents ev = rig.Events!;
                CohortTests.TrackEpisodes(ev, open);
                int total = rig.CheckInvariants("tick " + t, open);
                for (; missedSeen < ev.Missed.Count; missedSeen++)
                {
                    missed += ev.Missed[missedSeen].E.Count;
                }

                int sink = rig.Pop(Landside.Departed);
                Assert.True(total - sink + day.Boarded + missed == day.Injected, "tick " + t + ": head count not conserved");

                // Merge is mandatory (§9.3): live cohorts stay bounded by the
                // occupied (node, key) pairs plus open episodes, never growing
                // with elapsed time. At most 3 flights are live at once here.
                int live = 0;
                foreach (NodeId n in rig.World.Nodes())
                {
                    live += rig.Flow.CohortsAt(n).Count;
                }

                Assert.True(live <= nodes * 3 + open.Count, "tick " + t + ": " + live + " live cohorts");
            });

            FlowEvents events = rig.Events!;
            Assert.Equal(24, rig.Checkpoints.Checkpoints.Count);
            Assert.NotEmpty(events.Arrived);
            Assert.True(day.Boarded > 0);

            // Blocking episodes pair up (10 §10.3 rule 2): every Unblocked closes
            // an earlier Blocked for the same cohort with the same fields.
            var openPairs = new Dictionary<ulong, (NodeId Held, NodeId By)>();

            var byTick = new List<(ulong Tick, int Order, string Kind, ulong Cohort, NodeId Held, NodeId By)>();
            foreach (var b in events.Blocked)
            {
                byTick.Add((b.Tick, events.Log.IndexOf(b.Tick + " blocked c" + b.E.Cohort.Value + " n" + b.E.Held.Value + " by n" + b.E.BlockedBy.Value), "b", b.E.Cohort.Value, b.E.Held, b.E.BlockedBy));
            }

            foreach (var u in events.Unblocked)
            {
                byTick.Add((u.Tick, events.Log.IndexOf(u.Tick + " unblocked c" + u.E.Cohort.Value + " n" + u.E.Held.Value + " by n" + u.E.BlockedBy.Value), "u", u.E.Cohort.Value, u.E.Held, u.E.BlockedBy));
            }

            byTick.Sort((x, y) => x.Order.CompareTo(y.Order));
            foreach (var e in byTick)
            {
                if (e.Kind == "b")
                {
                    Assert.False(openPairs.ContainsKey(e.Cohort), "cohort " + e.Cohort + " blocked twice without an Unblocked");
                    openPairs[e.Cohort] = (e.Held, e.By);
                }
                else
                {
                    Assert.True(openPairs.TryGetValue(e.Cohort, out var p), "Unblocked without Blocked for cohort " + e.Cohort);
                    Assert.Equal(p.Held, e.Held);
                    Assert.Equal(p.By, e.By);
                    openPairs.Remove(e.Cohort);
                }
            }

            // Threshold events alternate per node, starting with Exceeded.
            var flag = new Dictionary<uint, bool>();
            var thresholds = new List<(int Order, uint Node, bool Up)>();
            foreach (var x in events.Exceeded)
            {
                thresholds.Add((events.Log.IndexOf(x.Tick + " exceeded n" + x.E.Node.Value + " w" + x.E.WaitMinutes.Raw + " " + x.E.ServersOpen + "/" + x.E.ServerCount), x.E.Node.Value, true));
            }

            foreach (var x in events.Cleared)
            {
                thresholds.Add((events.Log.IndexOf(x.Tick + " cleared n" + x.E.Node.Value + " w" + x.E.WaitMinutes.Raw + " " + x.E.ServersOpen + "/" + x.E.ServerCount), x.E.Node.Value, false));
            }

            thresholds.Sort((x, y) => x.Order.CompareTo(y.Order));
            foreach (var x in thresholds)
            {
                flag.TryGetValue(x.Node, out bool set);
                Assert.NotEqual(set, x.Up);
                flag[x.Node] = x.Up;
            }

            // Within each tick, threshold events come after every movement event.
            ulong lastTick = ulong.MaxValue;
            bool thresholdSeen = false;
            foreach (string line in events.Log)
            {
                string[] parts = line.Split(' ');
                ulong tick = ulong.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                if (tick != lastTick)
                {
                    lastTick = tick;
                    thresholdSeen = false;
                }

                bool isThreshold = parts[1] == "exceeded" || parts[1] == "cleared";
                bool isMovement = parts[1] == "blocked" || parts[1] == "unblocked" || parts[1] == "arrived";
                Assert.False(isMovement && thresholdSeen, "movement event after a threshold event in tick " + tick);
                thresholdSeen |= isThreshold;
            }
        }
    }
}
