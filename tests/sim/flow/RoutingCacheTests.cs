using System;
using System.Collections.Generic;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.6 "Routing cache" (Q-036): the system, with whatever routing cache
    /// it keeps, is compared after every tick of a scripted sim-day with the
    /// uncached reference model (<see cref="FlowReference"/>).
    ///
    /// The fixture is sim.flow-local (§9.10, Q-037), pools three Gate nodes and
    /// uses only Departing cohorts (Q-040):
    ///
    ///   1 Source ─e1─> 2 Corridor 36 m ─e6──────────────┐
    ///            └e2─> 3 Hall ─e3─> 4 Hall ─e4─> 5 Hall ─e5─> 6 Hall
    ///   6 ─e7─> 8 Queue B      6 ─e8─> 7 Queue A
    ///   50 Source ─e21─> 51 Corridor 30 m ─e22─> 7, ─e23─> 8
    ///   7 ─e9─> 9 Hall 6 m     8 ─e10─> 9, ─e11─> 12 Hall
    ///   9 ─e12─> 10 Hall ─e14─> 21 Gate      9 ─e13─> 11 Hall ─e15─> 20 Gate
    ///   12 ─e16─> 22 Gate      12 ─e17─> 21 Gate
    ///   20, 21, 22 ─> 30 Sink
    ///
    /// Why each case is certain to occur:
    /// - Walk speed: at 1, the corridor costs Ceil(36 / 6v) ticks against the
    ///   three halls' 3, and both routes share the suffix from 6. The fast walker
    ///   (3 m/s: 2 ticks) takes e1, the slow one (1 m/s: 6 ticks) takes e2. Show-up
    ///   spikes release all three profiles from 1 in one tick.
    /// - Edge tie-break: the medium walker (2 m/s: exactly 3 ticks) ties e1 and e2
    ///   to the same gate; ascending EdgeId picks e1.
    /// - Gate tie-break: at 9, e12 reaches only gate 21 and e13 only gate 20, at
    ///   equal cost; gate 20 wins, through the higher EdgeId. At 12, e16 reaches
    ///   only 22 and e17 only 21; gate 21 wins through e17.
    /// - Lane changes: at 6 and 51 the choice is wait(A) + 4 against wait(B) + 3
    ///   ticks; random SetServersOpen commands (closed lanes included) flip it.
    /// - Blocked re-route: both queues have a small CapacityStanding and inflow
    ///   near the open capacity, so 6 and 51 are refused, then routed elsewhere.
    /// The reference counts each case, and the test asserts every count is
    /// positive, so a script that stops reaching one fails here, not silently.
    /// </summary>
    public sealed class RoutingCacheTests
    {
        private const uint Sink = 30;
        private const uint QueueA = 7;
        private const uint QueueB = 8;
        private const int ServerCount = 5;
        private const int Flights = 33;

        private static readonly ContentId SecA = new ContentId("queue_cache_sec_a");
        private static readonly ContentId SecB = new ContentId("queue_cache_sec_b");
        private static readonly ContentId Fast = new ContentId("pax_cache_fast");
        private static readonly ContentId Medium = new ContentId("pax_cache_medium");
        private static readonly ContentId Slow = new ContentId("pax_cache_slow");
        private static readonly ContentId[] Profiles = { Fast, Medium, Slow };

        // Test-local values (never data/ balance values), sized so the queues
        // run near saturation: see the class comment.
        private static readonly Dictionary<string, RefQueueProfile> QueueProfiles = new Dictionary<string, RefQueueProfile>
        {
            [SecA.Value] = new RefQueueProfile(Fx.FromInt(3), 30, Fx.FromInt(8), Fx.FromInt(3)),
            [SecB.Value] = new RefQueueProfile(Fx.FromInt(4), 25, Fx.FromInt(5), Fx.Zero),
        };

        private static readonly Dictionary<string, Fx> WalkSpeeds = new Dictionary<string, Fx>
        {
            [Fast.Value] = Fx.FromInt(3),
            [Medium.Value] = Fx.FromInt(2),
            [Slow.Value] = Fx.One,
        };

        private static TestGraph Graph()
        {
            return new TestGraph()
                .Node(1, "source")
                .Node(2, "corridor", 36)
                .Node(3, "hall").Node(4, "hall").Node(5, "hall").Node(6, "hall")
                .Queue(QueueA, ServerCount, 3, SecA)
                .Queue(QueueB, ServerCount, 3, SecB)
                .Node(9, "hall", 6)
                .Node(10, "hall").Node(11, "hall").Node(12, "hall")
                .Node(20, "gate").Node(21, "gate").Node(22, "gate")
                .Node(Sink, "sink")
                .Node(50, "source")
                .Node(51, "corridor", 30)
                .Edge(1, 1, 2).Edge(2, 1, 3).Edge(3, 3, 4).Edge(4, 4, 5).Edge(5, 5, 6).Edge(6, 2, 6)
                .Edge(7, 6, QueueB).Edge(8, 6, QueueA)
                .Edge(9, QueueA, 9).Edge(10, QueueB, 9).Edge(11, QueueB, 12)
                .Edge(12, 9, 10).Edge(13, 9, 11)
                .Edge(14, 10, 21).Edge(15, 11, 20)
                .Edge(16, 12, 22).Edge(17, 12, 21)
                .Edge(18, 20, Sink).Edge(19, 21, Sink).Edge(20, 22, Sink)
                .Edge(21, 50, 51).Edge(22, 51, QueueA).Edge(23, 51, QueueB);
        }

        private static IContentIndex Content()
        {
            var defs = new List<IContentDefinition>();
            foreach (ContentId p in Profiles)
            {
                defs.Add(FlowKit.Pax(p, WalkSpeeds[p.Value]));
            }

            foreach (ContentId q in new[] { SecA, SecB })
            {
                RefQueueProfile v = QueueProfiles[q.Value];
                defs.Add(FlowKit.Queue(q, v.Rate, v.Capacity, v.Threshold, v.Hysteresis));
            }

            return ContentIndexFactory.Create(defs);
        }

        // ---- the script ----

        private readonly struct Call
        {
            public Call(bool absorb, CohortKey key, int count, uint at)
            {
                Absorb = absorb;
                Key = key;
                Count = count;
                At = at;
            }

            public bool Absorb { get; }

            public CohortKey Key { get; }

            public int Count { get; }

            public uint At { get; }
        }

        private sealed class Script
        {
            public readonly List<(ulong Tick, uint Node, int Count)> Commands = new List<(ulong, uint, int)>();
            public readonly Dictionary<ulong, List<Call>> Calls = new Dictionary<ulong, List<Call>>();
        }

        /// <summary>
        /// Flight f shows up over [400f − 300, 400f + 600) and is absorbed at
        /// 400f + 900, so ~2.5 flights are open at a time and the last closes at
        /// tick 14 100, inside day 0. Inflow averages about 1.6 pax a tick
        /// (2.5 x (1/3 + 1/4) plus 12 per 97-tick spike) against 2.1 a tick
        /// with the initial 3 + 3 lanes open. Random lane changes, closures
        /// included, push it over and back, so waits swing and targets fill.
        /// Measured over the day: every case below occurs dozens of times or more.
        /// </summary>
        private static Script BuildScript(ulong ticks)
        {
            var rng = new SplitMix64(0x36CAC4E0UL);
            var s = new Script();
            var open = new List<ulong>();
            for (ulong t = 0; t < ticks; t++)
            {
                var calls = new List<Call>();
                open.Clear();
                for (ulong f = 1; f <= Flights; f++)
                {
                    if (400 * f + 900 == t)
                    {
                        calls.Add(new Call(true, FlowKit.Key(f), 0, Sink));
                    }

                    if (400 * f - 300 <= t && t < 400 * f + 600)
                    {
                        open.Add(f);
                    }
                }

                if (open.Count > 0)
                {
                    if (rng.Range(0, 2) == 0)
                    {
                        calls.Add(RandomInject(rng, open, 1));
                    }

                    if (rng.Range(0, 3) == 0)
                    {
                        calls.Add(RandomInject(rng, open, 50));
                    }

                    // Show-up spike: every profile released from 1 in the same tick.
                    if (t % 97 == 13)
                    {
                        foreach (ContentId p in Profiles)
                        {
                            ulong f = open[rng.Range(0, open.Count - 1)];
                            calls.Add(new Call(false, FlowKit.Key(f, p, bags: rng.Range(0, 1) == 0), rng.Range(2, 6), 1));
                        }
                    }
                }

                if (calls.Count > 0)
                {
                    s.Calls.Add(t, calls);
                }

                // Lane changes; out-of-range counts exercise the clamp (§9.8).
                if (t > 0 && rng.Range(0, 119) == 0)
                {
                    s.Commands.Add((t, rng.Range(0, 1) == 0 ? QueueA : QueueB, rng.Range(-1, ServerCount + 1)));
                }
            }

            return s;
        }

        private static Call RandomInject(SplitMix64 rng, List<ulong> open, uint at)
        {
            ulong f = open[rng.Range(0, open.Count - 1)];
            ContentId p = Profiles[rng.Range(0, Profiles.Length - 1)];
            return new Call(false, FlowKit.Key(f, p, bags: rng.Range(0, 1) == 0, assist: rng.Range(0, 7) == 0), rng.Range(1, 4), at);
        }

        // ---- the system side ----

        /// <summary>
        /// Rig: sim.world at 1, the scripted caller at 2, sim.flow at 4 and the
        /// event recorder at 7, which calls nothing (09 §9.6 "The script").
        /// </summary>
        private sealed class SystemRun
        {
            public readonly Rig Rig;
            private readonly Script _script;
            private readonly List<string> _absorbed = new List<string>();
            private readonly Dictionary<ulong, string> _blockedKey = new Dictionary<ulong, string>();
            private int _blockedSeen;
            private int _unblockedSeen;
            private int _exceededSeen;
            private int _clearedSeen;
            private int _arrivedSeen;
            private int _missedSeen;

            public SystemRun(TestGraph graph, IContentIndex content, Script script)
            {
                Rig = Rig.Create(graph, content);
                _script = script;
                foreach ((ulong tick, uint node, int count) in script.Commands)
                {
                    var cmd = new Command(tick, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, FlowKit.SetServersOpenPayload(node, count));
                    Assert.True(Rig.Host.TrySubmit(cmd, out CommandRejection reason), "command at " + tick + " rejected: " + reason);
                }

                Rig.Inject = (in TickContext ctx) =>
                {
                    if (!_script.Calls.TryGetValue(ctx.Tick, out List<Call>? calls))
                    {
                        return;
                    }

                    foreach (Call c in calls)
                    {
                        if (c.Absorb)
                        {
                            int boarded = Rig.Flow.Absorb(new NodeId(c.At), c.Key.Flight);
                            _absorbed.Add("absorb f" + c.Key.Flight.Value + " boarded " + boarded);
                        }
                        else
                        {
                            Rig.Flow.Inject(c.Key, c.Count, new NodeId(c.At));
                        }
                    }
                };
            }

            /// <summary>Steps tick t and returns the canonical observation (09 §9.6 "Assertion").</summary>
            public List<string> Step(ulong t, FlowReference model)
            {
                Assert.Equal(t, Rig.Host.CurrentTick);
                Rig.Step(1);
                var lines = new List<string>();
                IFlowSystem flow = Rig.Flow;
                foreach (uint n in model.Nodes)
                {
                    var node = new NodeId(n);
                    lines.Add("n" + n + " pop " + flow.Population(node));
                    var byKey = new SortedDictionary<string, int>(StringComparer.Ordinal);
                    IReadOnlyList<CohortId> ids = flow.CohortsAt(node);
                    for (int i = 0; i < ids.Count; i++)
                    {
                        Assert.True(flow.TryGetCohort(ids[i], out PassengerCohort c), "tick " + t + ": CohortsAt lists unknown cohort " + ids[i].Value);
                        string k = FlowReference.KeyText(c.Key);
                        byKey[k] = (byKey.TryGetValue(k, out int v) ? v : 0) + c.Count;
                    }

                    foreach (KeyValuePair<string, int> kv in byKey)
                    {
                        lines.Add("n" + n + " k" + kv.Key + " x" + kv.Value);
                    }

                    if (model.IsQueue(n))
                    {
                        lines.Add("n" + n + " wait " + flow.PredictedWaitMinutes(node).Raw);
                    }
                }

                FlowEvents ev = Rig.Events!;

                // Unblocked before Blocked, so a same-tick Unblocked/Blocked pair
                // for one cohort reads the old pair's remembered Key first.
                for (; _unblockedSeen < ev.Unblocked.Count; _unblockedSeen++)
                {
                    (ulong tick, FlowUnblocked e) = ev.Unblocked[_unblockedSeen];
                    Assert.Equal(t, tick);
                    Assert.True(_blockedKey.TryGetValue(e.Cohort.Value, out string? key), "tick " + t + ": FlowUnblocked for cohort " + e.Cohort.Value + " with no open FlowBlocked");
                    _blockedKey.Remove(e.Cohort.Value);
                    lines.Add("unblocked h" + e.Held.Value + " b" + e.BlockedBy.Value + " k" + key);
                }

                for (; _blockedSeen < ev.Blocked.Count; _blockedSeen++)
                {
                    (ulong tick, FlowBlocked e) = ev.Blocked[_blockedSeen];
                    Assert.Equal(t, tick);
                    Assert.True(flow.TryGetCohort(e.Cohort, out PassengerCohort c), "tick " + t + ": FlowBlocked cohort " + e.Cohort.Value + " is gone after the tick");
                    string key = FlowReference.KeyText(c.Key);
                    _blockedKey[e.Cohort.Value] = key;
                    lines.Add("blocked h" + e.Held.Value + " b" + e.BlockedBy.Value + " k" + key);
                }

                for (; _exceededSeen < ev.Exceeded.Count; _exceededSeen++)
                {
                    QueueThresholdExceeded e = ev.Exceeded[_exceededSeen].E;
                    lines.Add("exceeded n" + e.Node.Value + " w" + e.WaitMinutes.Raw + " " + e.ServersOpen + "/" + e.ServerCount);
                }

                for (; _clearedSeen < ev.Cleared.Count; _clearedSeen++)
                {
                    QueueThresholdCleared e = ev.Cleared[_clearedSeen].E;
                    lines.Add("cleared n" + e.Node.Value + " w" + e.WaitMinutes.Raw + " " + e.ServersOpen + "/" + e.ServerCount);
                }

                for (; _arrivedSeen < ev.Arrived.Count; _arrivedSeen++)
                {
                    PassengersArrivedAtGate e = ev.Arrived[_arrivedSeen].E;
                    lines.Add("arrived f" + e.Flight.Value + " x" + e.Count);
                }

                for (; _missedSeen < ev.Missed.Count; _missedSeen++)
                {
                    PassengersMissedFlight e = ev.Missed[_missedSeen].E;
                    lines.Add("missed f" + e.Flight.Value + " x" + e.Count + " at n" + e.LastBlockedAt.Value);
                }

                lines.AddRange(_absorbed);
                _absorbed.Clear();
                return lines;
            }
        }

        /// <summary>The reference's order for tick t: commands, then the caller's calls, then Tick.</summary>
        private static List<string> StepReference(FlowReference model, Script script, ulong t)
        {
            var extra = new List<string>();
            foreach ((ulong tick, uint node, int count) in script.Commands)
            {
                if (tick == t)
                {
                    model.ApplyServersOpen(node, count);
                }
            }

            if (script.Calls.TryGetValue(t, out List<Call>? calls))
            {
                foreach (Call c in calls)
                {
                    if (c.Absorb)
                    {
                        int boarded = model.Absorb(c.At, c.Key.Flight);
                        extra.Add("absorb f" + c.Key.Flight.Value + " boarded " + boarded);
                    }
                    else
                    {
                        model.Inject(c.Key, c.Count, c.At);
                    }
                }
            }

            model.Tick(t);
            var lines = new List<string>();
            model.Observe(lines);
            lines.AddRange(extra);
            return lines;
        }

        /// <summary>
        /// Head counts, populations and waits are compared in node order; the
        /// events of the tick as a multiset (09 §9.6: CohortIds never compared).
        /// </summary>
        private static string Canonical(List<string> lines)
        {
            var state = new List<string>();
            var events = new List<string>();
            foreach (string l in lines)
            {
                (l.StartsWith("n", StringComparison.Ordinal) ? state : events).Add(l);
            }

            events.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string l in state)
            {
                sb.Append(l).Append('\n');
            }

            foreach (string l in events)
            {
                sb.Append(l).Append('\n');
            }

            return sb.ToString();
        }

        private static void AssertSame(string expected, string actual, ulong t, string what)
        {
            if (expected == actual)
            {
                return;
            }

            string[] e = expected.Split('\n');
            string[] a = actual.Split('\n');
            var diff = new StringBuilder();
            var inA = new HashSet<string>(a);
            var inE = new HashSet<string>(e);
            foreach (string l in e)
            {
                if (!inA.Contains(l))
                {
                    diff.Append("\n  reference only: ").Append(l);
                }
            }

            foreach (string l in a)
            {
                if (!inE.Contains(l))
                {
                    diff.Append("\n  system only:    ").Append(l);
                }
            }

            if (diff.Length == 0)
            {
                diff.Append("\n  (same lines, different multiplicity)");
            }

            throw new Xunit.Sdk.XunitException(what + " diverges from the uncached reference after tick " + t + ":" + diff);
        }

        [Fact]
        public void test_flow_routing_cache_matches_uncached_reference()
        {
            ulong day = SimConstants.TICKS_PER_SIM_DAY;
            ulong restartAt = day / 2 + 37;
            TestGraph graph = Graph();
            IContentIndex content = Content();
            Script script = BuildScript(day);

            var first = new SystemRun(graph, content, script);
            var model = new FlowReference(first.Rig.World, Fixtures.Utf8(graph.FlowJson()), QueueProfiles, WalkSpeeds);

            var trace = new List<string>();
            for (ulong t = 0; t < restartAt; t++)
            {
                string expected = Canonical(StepReference(model, script, t));
                string actual = Canonical(first.Step(t, model));
                AssertSame(expected, actual, t, "the system");
                trace.Add(actual);
            }

            // Restart (09 §9.6 "What the restart case proves"): until sim.save
            // exists, a restart is a replay from seed from tick 0 into a fresh
            // system with a cold cache (19 §19.5). The replay must reproduce the
            // first run tick for tick; the reference then continues from its own
            // state, unrestarted.
            var second = new SystemRun(graph, content, script);
            for (ulong t = 0; t < restartAt; t++)
            {
                AssertSame(trace[(int)t], Canonical(second.Step(t, model)), t, "the replayed system");
            }

            for (ulong t = restartAt; t < day; t++)
            {
                string expected = Canonical(StepReference(model, script, t));
                string actual = Canonical(second.Step(t, model));
                AssertSame(expected, actual, t, "the restarted system");
            }

            // Every case 09 §9.6 requires the script to contain, counted by the
            // reference over the whole day.
            string coverage = "walk-speed " + model.WalkSpeedSplits + ", gate tie " + model.GateTieBreaks + ", edge tie " + model.EdgeTieBreaks
                + ", lane flip " + model.LaneChangeFlips + ", blocked re-route moved " + model.BlockedRerouteMoved
                + ", re-blocked " + model.BlockedRerouteReblocked + ", show-up spike " + model.ShowUpSpikes
                + ", missed while blocked " + model.MissedWhileBlocked + ", refusals " + model.Refusals;
            Assert.True(model.WalkSpeedSplits > 0, "no walk-speed case: " + coverage);
            Assert.True(model.GateTieBreaks > 0, "no gate tie-break case: " + coverage);
            Assert.True(model.EdgeTieBreaks > 0, "no edge tie-break case: " + coverage);
            Assert.True(model.LaneChangeFlips > 0, "no lane-change flip: " + coverage);
            Assert.True(model.BlockedRerouteMoved + model.BlockedRerouteReblocked > 0, "no blocked re-route: " + coverage);
            Assert.True(model.ShowUpSpikes > 0, "no show-up spike: " + coverage);
            Assert.True(model.MissedWhileBlocked > 0, "no Absorb of a blocked cohort: " + coverage);
        }
    }
}
