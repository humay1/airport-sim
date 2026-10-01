using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// T-023: security lanes opened and closed live, and visible queues
    /// (09 §9.4, §9.7a, §9.7b, §9.8, §9.9, §9.12). SetServersOpen is applied in
    /// phase 1 at the command's tick (08 §8.7) and clamped to [0, ServerCount]
    /// at Apply, never at admission. These tests observe the lane from inside
    /// the tick as well as between Steps: a probe at registry position 2 runs
    /// after command application and before sim.flow's Tick, and one at 7 runs
    /// after it. The between-Steps clamp and validation cases already on main
    /// are in QueueThroughputTests, and TryGetOutstanding's tie-break in
    /// AbsorbTests.
    /// </summary>
    public sealed class SecurityLaneLiveTests
    {
        private static int Open(Rig rig, uint node)
        {
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(node), out LaneState lanes));
            return lanes.ServersOpen;
        }

        /// <summary>
        /// Rig.Create's composition, plus a probe at position 7 (sim.delay's,
        /// absent here) that runs after sim.flow's Tick in every tick.
        /// </summary>
        private static Rig WithAfterFlowProbe(TestGraph graph, IContentIndex content, ProbeTick afterFlow)
        {
            var rig = new Rig { Checkpoints = new RecordingSink() };
            ISimHostBuilder b = FlowKit.Builder(content, 1, rig.Checkpoints);
            rig.World = graph.World(b);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(Fixtures.Utf8(graph.FlowJson()), "test.flow.json", rig.World);
            rig.Flow = FlowFactory.CreateSystem(b.Services, flowGraph, rig.World);
            rig.Injector = new ProbeSystem(2) { OnTick = (in TickContext ctx) => rig.Inject?.Invoke(ctx) };
            b.Register(rig.World);
            b.Register(rig.Injector);
            b.Register(rig.Flow);
            rig.Events = new FlowEvents(b.Services.Events);
            b.Register(new ProbeSystem(7) { OnTick = afterFlow });
            rig.Host = b.Build();
            rig.Corridors = graph.Corridors();
            return rig;
        }

        private static bool SubmitAt(Rig rig, ulong tick, uint node, int count, out CommandRejection reason)
        {
            return rig.Host.TrySubmit(new Command(tick, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, FlowKit.SetServersOpenPayload(node, count)), out reason);
        }

        [Fact]
        public void test_security_lane_mid_day_command_changes_servers_open_only_at_its_tick_boundary()
        {
            // 09 §9.8: "takes effect at the next tick boundary". Mid-day, with
            // a loaded queue, lane state seen before and after sim.flow's Tick
            // is the same in every tick (never a mid-tick change), is the old
            // value through the tick before the command's tick and the new one
            // from that tick on. At 10 pax/min one server is exactly one
            // passenger a tick (§9.12), so the served count follows it too.
            Fx rate = Fx.FromInt(10);
            var before = new Dictionary<ulong, int>();
            var after = new Dictionary<ulong, int>();
            Rig rig = null!;
            rig = WithAfterFlowProbe(Graphs.Line(3, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)), (in TickContext ctx) => after[ctx.Tick] = Open(rig, 2));
            rig.Inject = (in TickContext ctx) => before[ctx.Tick] = Open(rig, 2);

            rig.Step((uint)(5 * SimConstants.TICKS_PER_SIM_HOUR));
            rig.InjectNow(1, 200, 1);
            rig.Step(3);
            Assert.True(rig.Pop(2) > 150, "the queue never loaded");

            ulong s = rig.NextTick;
            var moved = new Dictionary<ulong, int>();
            for (int i = 0; i < 12; i++)
            {
                ulong t = rig.NextTick;
                if (i == 2)
                {
                    // Due at t + 1 (COMMAND_MIN_LEAD_TICKS), and one due at t + 6.
                    Assert.True(rig.Submit(2, 3, out CommandRejection r1));
                    Assert.Equal(CommandRejection.None, r1);
                    Assert.True(SubmitAt(rig, t + 6, 2, 2, out CommandRejection r2));
                    Assert.Equal(CommandRejection.None, r2);
                    Assert.Equal(1, Open(rig, 2));
                }

                int gate = rig.Pop(3);
                rig.Step(1);
                moved[t] = rig.Pop(3) - gate;
            }

            ulong firstDue = s + 3;
            ulong secondDue = s + 8;
            for (ulong t = s; t < s + 12; t++)
            {
                int expected = t < firstDue ? 1 : t < secondDue ? 3 : 2;
                Assert.True(before[t] == expected, "tick " + t + ": before sim.flow's Tick ServersOpen " + before[t] + ", expected " + expected);
                Assert.True(after[t] == expected, "tick " + t + ": after sim.flow's Tick ServersOpen " + after[t] + ", expected " + expected);
                Assert.True(moved[t] == expected, "tick " + t + ": served " + moved[t] + ", expected " + expected);
            }
        }

        [Fact]
        public void test_security_lane_validate_admits_any_count_and_apply_clamps_extremes()
        {
            // 09 §9.8: admission is a function of the payload and load-time data
            // only; the length and the node are checked there, never the count.
            var rig = Rig.Create(Graphs.Line(3, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(2))));
            ulong due = rig.Host.CurrentTick + SimConstants.COMMAND_MIN_LEAD_TICKS;

            // Nine bytes whose first eight are a valid payload, and none at all.
            var nine = new byte[9];
            Array.Copy(FlowKit.SetServersOpenPayload(2, 1), nine, 8);
            Assert.False(rig.Host.TrySubmit(new Command(due, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, nine), out CommandRejection longPayload));
            Assert.Equal(CommandRejection.MalformedPayload, longPayload);
            Assert.False(rig.Host.TrySubmit(new Command(due, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, new byte[0]), out CommandRejection empty));
            Assert.Equal(CommandRejection.MalformedPayload, empty);

            // Unknown nodes, the top bit of the uint32 id included.
            Assert.False(rig.Submit(0, 1, out CommandRejection zero));
            Assert.Equal(CommandRejection.MalformedPayload, zero);
            Assert.False(rig.Submit(uint.MaxValue, 1, out CommandRejection top));
            Assert.Equal(CommandRejection.MalformedPayload, top);
            Assert.Empty(rig.Host.CommandLogSince(0));

            // Any count is admitted; Apply clamps it, at the extremes of int32.
            Assert.True(rig.Submit(2, int.MaxValue, out CommandRejection max));
            Assert.Equal(CommandRejection.None, max);
            rig.Step(2);
            Assert.Equal(3, Open(rig, 2));

            Assert.True(rig.Submit(2, int.MinValue, out CommandRejection min));
            Assert.Equal(CommandRejection.None, min);
            rig.Step(2);
            Assert.Equal(0, Open(rig, 2));

            Assert.True(rig.Submit(2, 3, out _));
            rig.Step(2);
            Assert.Equal(3, Open(rig, 2));

            Assert.True(rig.Submit(2, -1, out _));
            rig.Step(2);
            Assert.Equal(0, Open(rig, 2));
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(2), out LaneState lanes));
            Assert.Equal(3, lanes.ServerCount);
            Assert.Equal(4, rig.Host.CommandLogSince(0).Count);
        }

        [Fact]
        public void test_security_lane_validate_classifies_every_fixture_node_the_same_whatever_the_lanes()
        {
            // 09 §9.8 on the Phase 0 fixture: its two security queues are
            // admitted; every other kind (source, hall, corridor, gate, sink)
            // is NotPermitted; an id outside the fixture is MalformedPayload.
            // Admission reads load-time data only, so closing and opening lanes
            // changes none of it.
            var rig = Rig.Fixture(DayScenario.Content());
            var expected = new Dictionary<uint, CommandRejection>
            {
                { Landside.Kerb, CommandRejection.NotPermitted },
                { Landside.RailBox, CommandRejection.NotPermitted },
                { Landside.CheckInHall, CommandRejection.NotPermitted },
                { Landside.LandsideCorridor, CommandRejection.NotPermitted },
                { Landside.SecurityA, CommandRejection.None },
                { Landside.SecurityB, CommandRejection.None },
                { Landside.AirsideCorridor, CommandRejection.NotPermitted },
                { Landside.Gate, CommandRejection.NotPermitted },
                { Landside.Departed, CommandRejection.NotPermitted },
                { 10, CommandRejection.MalformedPayload },
            };

            void Check(string when, int count)
            {
                foreach (KeyValuePair<uint, CommandRejection> e in expected)
                {
                    bool admitted = rig.Submit(e.Key, count, out CommandRejection reason);
                    Assert.True(reason == e.Value, when + ": node " + e.Key + " gave " + reason + ", expected " + e.Value);
                    Assert.Equal(e.Value == CommandRejection.None, admitted);
                }
            }

            Check("at load", 0);
            rig.Step(2);
            Assert.Equal(0, Open(rig, Landside.SecurityA));
            Assert.Equal(0, Open(rig, Landside.SecurityB));
            Check("lanes closed", 3);
            rig.Step(2);
            Assert.Equal(3, Open(rig, Landside.SecurityA));
            Assert.Equal(3, Open(rig, Landside.SecurityB));
            Check("lanes open", 7);
        }

        [Fact]
        public void test_security_lane_commands_apply_in_total_order_and_set_the_value()
        {
            // 08 §8.7: due commands apply in (Tick, Issuer, Sequence) order, and
            // 09 §9.8's Apply sets ServersOpen (it does not add to it). Each
            // lane is its own node: a command on one never moves the other.
            var rig = Rig.Fixture(DayScenario.Content());
            Assert.Equal(1, Open(rig, Landside.SecurityA));
            Assert.Equal(1, Open(rig, Landside.SecurityB));

            // Two commands due at one tick: the later admitted wins.
            Assert.True(rig.Submit(Landside.SecurityA, 3, out _));
            Assert.True(rig.Submit(Landside.SecurityA, 2, out _));
            rig.Step(2);
            Assert.Equal(2, Open(rig, Landside.SecurityA));
            Assert.Equal(1, Open(rig, Landside.SecurityB));

            Assert.True(rig.Submit(Landside.SecurityB, 0, out _));
            Assert.True(rig.Submit(Landside.SecurityB, 3, out _));
            rig.Step(2);
            Assert.Equal(2, Open(rig, Landside.SecurityA));
            Assert.Equal(3, Open(rig, Landside.SecurityB));

            // Admitted in reverse tick order: tick order still decides.
            ulong t0 = rig.NextTick;
            Assert.True(SubmitAt(rig, t0 + 3, Landside.SecurityA, 0, out _));
            Assert.True(SubmitAt(rig, t0 + 1, Landside.SecurityA, 3, out _));
            var seen = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                rig.Step(1);
                seen.Add(Open(rig, Landside.SecurityA));
                Assert.Equal(3, Open(rig, Landside.SecurityB));
            }

            Assert.Equal(new[] { 2, 3, 3, 0, 0 }, seen);
        }

        [Fact]
        public void test_security_lane_population_and_wait_reflect_the_change_immediately_after_apply()
        {
            // T-023 "visible queues": right after phase 1 applies the command,
            // before sim.flow's Tick of that tick runs, Population is unchanged
            // (a command moves nobody) and PredictedWaitMinutes is already
            // §9.12's population / max(ServersOpen x rate, EPSILON) with the new
            // ServersOpen. Closing every lane raises the wait past the threshold,
            // and the event carries the new ServersOpen (§9.12 "Thresholds").
            Fx rate = Fx.FromInt(2);
            var rig = Rig.Create(Graphs.Line(3, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate, 100000, 1000, 0)));
            rig.InjectNow(1, 40, 1);
            rig.Step(4);
            Assert.True(rig.Pop(2) > 30, "the queue never loaded");
            Assert.Equal(Graphs.Wait(rig.Pop(2), 1, rate), rig.Flow.PredictedWaitMinutes(new NodeId(2)));

            var node = new NodeId(2);
            int inTickPop = -1;
            Fx inTickWait = Fx.Zero;
            int inTickOpen = -1;
            ulong due = ulong.MaxValue;
            rig.Inject = (in TickContext ctx) =>
            {
                if (ctx.Tick == due)
                {
                    inTickPop = rig.Flow.Population(node);
                    inTickWait = rig.Flow.PredictedWaitMinutes(node);
                    inTickOpen = Open(rig, 2);
                }
            };

            foreach (int open in new[] { 3, 0 })
            {
                due = rig.NextTick + SimConstants.COMMAND_MIN_LEAD_TICKS;
                Assert.True(rig.Submit(2, open, out _));
                rig.Step(1);
                int popBefore = rig.Pop(2);
                Assert.Equal(Graphs.Wait(popBefore, open == 3 ? 1 : 3, rate), rig.Flow.PredictedWaitMinutes(node));
                rig.Step(1);

                Assert.Equal(open, inTickOpen);
                Assert.Equal(popBefore, inTickPop);
                Fx expected = Graphs.Wait(popBefore, open, rate);
                Assert.True(expected == inTickWait, "ServersOpen " + open + ": wait after apply " + inTickWait.Raw + ", expected " + expected.Raw);
                Assert.Equal(Graphs.Wait(rig.Pop(2), open, rate), rig.Flow.PredictedWaitMinutes(node));
            }

            // 30-odd waiting with no lane open read as tens of thousands of
            // minutes, above the 1000-minute threshold, at the command's tick.
            FlowEvents ev = rig.Events!;
            Assert.Single(ev.Exceeded);
            Assert.Equal(due, ev.Exceeded[0].Tick);
            Assert.Equal(node, ev.Exceeded[0].E.Node);
            Assert.Equal(0, ev.Exceeded[0].E.ServersOpen);
            Assert.Equal(3, ev.Exceeded[0].E.ServerCount);
            Assert.Equal(Graphs.Wait(rig.Pop(2), 0, rate), ev.Exceeded[0].E.WaitMinutes);
            Assert.Empty(ev.Cleared);
        }

        /// <summary>
        /// 200 passengers on a 2-server lane at 1 pax/min per server, the lane
        /// switched between 1 and 2 open every 10 ticks for 300 ticks. At most
        /// 0.2 are served a tick, so the population stays in [136, 200]: the
        /// wait is at least 136 with one lane and at most 100 with two, and at
        /// least 68 with two, so it swings across T = 100 at every switch.
        /// Returns the ticks at which the lane became 1 open and 2 open.
        /// </summary>
        private static Rig Oscillate(long hysteresis, out List<ulong> toOne, out List<ulong> toTwo)
        {
            var rig = Rig.Create(Graphs.Line(2, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One, 100000, 100, hysteresis)));
            rig.InjectNow(1, 200, 1);
            rig.Step(20);
            Assert.Equal(0, rig.Pop(1));
            toOne = new List<ulong>();
            toTwo = new List<ulong>();
            for (int i = 0; i < 300; i++)
            {
                if (i % 10 == 0)
                {
                    int open = (i / 10) % 2 == 0 ? 2 : 1;
                    (open == 1 ? toOne : toTwo).Add(rig.NextTick + SimConstants.COMMAND_MIN_LEAD_TICKS);
                    Assert.True(rig.Submit(2, open, out _));
                }

                rig.Step(1);
                int pop = rig.Pop(2);
                Assert.InRange(pop, 136, 200);
            }

            rig.Step(5);
            return rig;
        }

        [Fact]
        public void test_security_lane_oscillating_wait_within_hysteresis_does_not_flood_threshold_events()
        {
            // 10 §10.3 rule 4, dedup at source: with h = 40 the wait swings
            // above T = 100 and down to no lower than 68, never below T - h =
            // 60. One QueueThresholdExceeded, when the queue first loaded, and
            // nothing else across 30 lane switches.
            Rig rig = Oscillate(40, out _, out _);
            FlowEvents ev = rig.Events!;
            Assert.True(ev.Exceeded.Count == 1, "exceeded " + ev.Exceeded.Count + " times:\n" + string.Join("\n", ev.Log));
            Assert.True(ev.Exceeded[0].Tick < 20, "the first exceed came at tick " + ev.Exceeded[0].Tick);
            Assert.Empty(ev.Cleared);
        }

        [Fact]
        public void test_security_lane_oscillating_wait_emits_once_per_crossing_never_per_tick()
        {
            // With h = 0 every switch is a real crossing (w >= 136 > 100 with one
            // lane, w < 100 < ... with two once anyone is served). Each crossing
            // emits exactly one event, at the switch's own tick and carrying the
            // new ServersOpen; the nine ticks between switches emit nothing.
            Rig rig = Oscillate(0, out List<ulong> toOne, out List<ulong> toTwo);
            FlowEvents ev = rig.Events!;
            string log = string.Join("\n", ev.Log);

            Assert.True(ev.Cleared.Count == toTwo.Count, "cleared " + ev.Cleared.Count + " times, expected " + toTwo.Count + ":\n" + log);
            for (int i = 0; i < toTwo.Count; i++)
            {
                Assert.True(ev.Cleared[i].Tick == toTwo[i], "clear " + i + " at tick " + ev.Cleared[i].Tick + ", expected " + toTwo[i] + ":\n" + log);
                Assert.Equal(2, ev.Cleared[i].E.ServersOpen);
                Assert.Equal(2, ev.Cleared[i].E.ServerCount);
            }

            // The first exceed is the load; then one per switch back to one lane.
            Assert.True(ev.Exceeded.Count == 1 + toOne.Count, "exceeded " + ev.Exceeded.Count + " times, expected " + (1 + toOne.Count) + ":\n" + log);
            Assert.True(ev.Exceeded[0].Tick < 20, log);
            for (int i = 0; i < toOne.Count; i++)
            {
                Assert.True(ev.Exceeded[i + 1].Tick == toOne[i], "exceed " + (i + 1) + " at tick " + ev.Exceeded[i + 1].Tick + ", expected " + toOne[i] + ":\n" + log);
                Assert.Equal(1, ev.Exceeded[i + 1].E.ServersOpen);
                Assert.Equal(2, ev.Exceeded[i + 1].E.ServerCount);
            }
        }

        [Fact]
        public void test_security_lane_queries_are_unaffected_by_promotion()
        {
            // 09 §9.7a: TryGetOutstanding "is unaffected by promotion (§9.1)",
            // and §9.7b's TryGetLaneState is read from node runtime state,
            // which promotion never touches. The same day, with and without
            // every node promoted, gives the same answers after every tick.
            List<string> Record(bool promote)
            {
                var rig = Rig.Fixture(DayScenario.Content());
                if (promote)
                {
                    foreach (NodeId n in rig.World.Nodes())
                    {
                        rig.Flow.SetPromoted(n, true);
                    }
                }

                var trace = new List<string>();
                new DayScenario().Run(rig, 0x7023_0001UL, 2400, promote, afterTick: t =>
                {
                    for (ulong f = 1; f <= 5; f++)
                    {
                        trace.Add(rig.Flow.TryGetOutstanding(new FlightId(f), out OutstandingPassengers o)
                            ? t + " f" + f + " " + o.Flight.Value + " x" + o.Count + " at n" + o.MostHeldAt.Value
                            : t + " f" + f + " none");
                    }

                    for (uint n = 1; n <= 10; n++)
                    {
                        trace.Add(rig.Flow.TryGetLaneState(new NodeId(n), out LaneState l)
                            ? t + " n" + n + " " + l.ServersOpen + "/" + l.ServerCount
                            : t + " n" + n + " none");
                    }
                });
                return trace;
            }

            List<string> plain = Record(false);
            List<string> promoted = Record(true);
            Assert.Contains(plain, l => l.Contains(" x", StringComparison.Ordinal));
            Assert.Contains(plain, l => l.EndsWith(" 0/3", StringComparison.Ordinal) || l.EndsWith(" 2/3", StringComparison.Ordinal) || l.EndsWith(" 3/3", StringComparison.Ordinal));
            Assert.Equal(plain.Count, promoted.Count);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.True(plain[i] == promoted[i], "plain '" + plain[i] + "', promoted '" + promoted[i] + "'");
            }
        }
    }
}
