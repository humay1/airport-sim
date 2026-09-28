using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.4, exact per §9.12 (Q-032): the queue throughput model, driven on
    /// Source 1 -> Queue 2 -> Gate 3 -> Sink 4 and checked tick by tick against
    /// QueueModel, an independent model of the arithmetic.
    /// </summary>
    public sealed class QueueThroughputTests
    {
        internal sealed class Plan
        {
            public readonly Dictionary<ulong, int> Injections = new Dictionary<ulong, int>();
            public readonly Dictionary<ulong, int> OpenCommands = new Dictionary<ulong, int>();
        }

        /// <summary>
        /// Runs the rig and the model side by side. Injections at tick t happen
        /// between Steps, before tick t runs. The queue's arrivals are observed as
        /// the drop in source population, so the model does not depend on how
        /// Inject sets EnteredNodeAt. Returns the passengers moved on per tick.
        /// </summary>
        internal static List<int> Drive(Rig rig, QueueModel model, int serverCount, Plan plan, int ticks, string where)
        {
            var movedPerTick = new List<int>();
            var pendingOpen = new Dictionary<ulong, int>();
            for (int i = 0; i < ticks; i++)
            {
                ulong t = rig.NextTick;
                if (plan.Injections.TryGetValue(t, out int n))
                {
                    rig.InjectNow(1, n, 1);
                }

                if (plan.OpenCommands.TryGetValue(t, out int open))
                {
                    Assert.True(rig.Submit(2, open, out CommandRejection reason), where + ": command rejected " + reason);
                    pendingOpen[t + SimConstants.COMMAND_MIN_LEAD_TICKS] = Math.Max(0, Math.Min(open, serverCount));
                }

                int sourceBefore = rig.Pop(1);
                int gateBefore = rig.Pop(3);
                rig.Step(1);
                int arrivals = sourceBefore - rig.Pop(1);
                if (arrivals > 0)
                {
                    model.Arrive(t, arrivals);
                }

                if (pendingOpen.TryGetValue(t, out int nowOpen))
                {
                    model.Open = nowOpen;
                }

                int expectedMoved = model.Step(t);
                int moved = rig.Pop(3) - gateBefore;
                Assert.True(model.Population == rig.Pop(2), where + ", tick " + t + ": queue population " + rig.Pop(2) + ", model " + model.Population);
                Assert.True(expectedMoved == moved, where + ", tick " + t + ": moved " + moved + ", model " + expectedMoved);
                Assert.True(model.Wait() == rig.Flow.PredictedWaitMinutes(new NodeId(2)), where + ", tick " + t + ": wait " + rig.Flow.PredictedWaitMinutes(new NodeId(2)).Raw + ", model " + model.Wait().Raw);
                movedPerTick.Add(moved);
            }

            return movedPerTick;
        }

        [Fact]
        public void test_queue_throughput_two_and_a_half_per_minute_serves_two_or_three_per_minute()
        {
            Fx rate = Fx.FromRatio(5, 2);
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)));
            var model = new QueueModel(rate, 1, 1, Fx.FromInt(1000000), Fx.Zero);
            var plan = new Plan();
            plan.Injections[0] = 500;
            List<int> moved = Drive(rig, model, 1, plan, 1200, "2.5/min");

            // Q-032: 2.5 pax/min is exactly 0.25 per tick, so once the queue is
            // busy it serves one passenger every fourth tick: 2 or 3 per sim
            // minute, alternating to exactly 5 every 2 minutes and 25 per 10.
            int first = moved.FindIndex(m => m > 0);
            Assert.True(first >= 0);
            for (int start = first; start + 200 <= moved.Count; start += 10)
            {
                int minute = 0;
                int twoMinutes = 0;
                int tenMinutes = 0;
                for (int k = 0; k < 100; k++)
                {
                    if (k < 10)
                    {
                        minute += moved[start + k];
                    }

                    if (k < 20)
                    {
                        twoMinutes += moved[start + k];
                    }

                    tenMinutes += moved[start + k];
                }

                Assert.InRange(minute, 2, 3);
                Assert.Equal(5, twoMinutes);
                Assert.Equal(25, tenMinutes);
            }

            Assert.All(moved, m => Assert.InRange(m, 0, 1));
        }

        [Fact]
        public void test_queue_throughput_matches_reference_model_property()
        {
            // 07 L4 property test: rates, lane counts, arrivals and lane commands
            // drawn from SplitMix64; every tick compared with QueueModel.
            const ulong seed = 0x7007_0001UL;
            var rng = new SplitMix64(seed);
            string[] rates = { "0", "0.7", "1", "1.3", "2.5", "4", "7.75", "12.25", "30" };
            for (int iteration = 0; iteration < 40; iteration++)
            {
                Fx rate = Fx.Parse(rates[rng.Range(0, rates.Length - 1)]);
                int servers = rng.Range(1, 4);
                int open = rng.Range(0, servers);
                long threshold = rng.Range(0, 40);
                long hysteresis = rng.Range(0, (int)threshold);
                var rig = Rig.Create(Graphs.Line(servers, open, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate, 100000, threshold, hysteresis)));
                var model = new QueueModel(rate, servers, open, Fx.FromInt(threshold), Fx.FromInt(hysteresis));
                var plan = new Plan();
                for (int k = 0; k < 12; k++)
                {
                    plan.Injections[(ulong)rng.Range(0, 250)] = rng.Range(1, 60);
                }

                for (int k = 0; k < 4; k++)
                {
                    plan.OpenCommands[(ulong)rng.Range(0, 280)] = rng.Range(-1, servers + 1);
                }

                string where = "seed 0x" + seed.ToString("X") + ", iteration " + iteration + ", rate " + rate + ", servers " + open + "/" + servers;
                Drive(rig, model, servers, plan, 320, where);

                var actual = new List<string>();
                foreach (var e in rig.Events!.Exceeded)
                {
                    actual.Add(e.Tick + " exceeded w" + e.E.WaitMinutes.Raw + " " + e.E.ServersOpen + "/" + e.E.ServerCount);
                }

                foreach (var e in rig.Events.Cleared)
                {
                    actual.Add(e.Tick + " cleared w" + e.E.WaitMinutes.Raw + " " + e.E.ServersOpen + "/" + e.E.ServerCount);
                }

                actual.Sort(StringComparer.Ordinal);
                var expected = new List<string>(model.Events);
                expected.Sort(StringComparer.Ordinal);
                Assert.True(string.Join("\n", expected) == string.Join("\n", actual), where + ": threshold events\nexpected:\n" + string.Join("\n", expected) + "\nactual:\n" + string.Join("\n", actual));
            }
        }

        [Fact]
        public void test_queue_throughput_idle_credit_is_capped_at_one_server_tick()
        {
            // 09 §9.4 step 5: a lane left idle for a long time does not discharge
            // a burst. 3 lanes at 10 pax/min are exactly 3 passengers a tick, so
            // after 300 idle ticks the first busy tick serves 3, not hundreds.
            Fx rate = Fx.FromInt(10);
            var rig = Rig.Create(Graphs.Line(3, 3, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)));
            var model = new QueueModel(rate, 3, 3, Fx.FromInt(1000000), Fx.Zero);
            var plan = new Plan();
            plan.Injections[300] = 100;
            List<int> moved = Drive(rig, model, 3, plan, 340, "idle 3x10/min");
            int first = moved.FindIndex(m => m > 0);
            Assert.Equal(3, moved[first]);
            Assert.All(moved, m => Assert.InRange(m, 0, 3));

            // Fractional: at 2.5 pax/min idle credit is capped at 0.25, so the
            // first passenger is served no sooner than a busy lane would allow.
            Fx slow = Fx.FromRatio(5, 2);
            var rig2 = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(slow)));
            var model2 = new QueueModel(slow, 1, 1, Fx.FromInt(1000000), Fx.Zero);
            var plan2 = new Plan();
            plan2.Injections[301] = 10;
            List<int> moved2 = Drive(rig2, model2, 1, plan2, 340, "idle 1x2.5/min");
            Assert.All(moved2, m => Assert.InRange(m, 0, 1));
        }

        [Fact]
        public void test_queue_throughput_never_serves_same_tick_arrivals()
        {
            // Q-032: only cohorts with EnteredNodeAt < t are served at t. The
            // queue (node 2) is processed after the source (node 1) in the same
            // tick and has credit for 6 a tick, yet serves nobody until t + 1.
            Fx rate = Fx.FromInt(60);
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(rate)));
            rig.Step(5);
            rig.InjectNow(1, 4, 1);
            int guard = 0;
            while (rig.Pop(1) > 0 && guard++ < 5)
            {
                rig.Step(1);
            }

            Assert.Equal(0, rig.Pop(1));
            Assert.Equal(4, rig.Pop(2));
            Assert.Equal(0, rig.Pop(3));
            List<PassengerCohort> onQueue = rig.CohortsOn(2);
            Assert.Single(onQueue);
            Assert.Equal(rig.Host.CurrentTick - 1, onQueue[0].EnteredNodeAt);

            rig.Step(1);
            Assert.Equal(0, rig.Pop(2));
            Assert.Equal(4, rig.Pop(3));
        }

        [Fact]
        public void test_queue_throughput_servers_open_command_clamps_and_takes_effect_next_tick()
        {
            // 09 §9.8: clamped to [0, ServerCount] at Apply; takes effect at the
            // tick it is due. TryGetLaneState reflects every applied command.
            var rig = Rig.Create(Graphs.Line(3, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(2))));
            Assert.True(rig.Flow.TryGetLaneState(new NodeId(2), out LaneState lanes));
            Assert.Equal(3, lanes.ServerCount);
            Assert.Equal(1, lanes.ServersOpen);

            Assert.True(rig.Submit(2, 99, out CommandRejection r1));
            Assert.Equal(CommandRejection.None, r1);
            rig.Step(1);
            rig.Flow.TryGetLaneState(new NodeId(2), out lanes);
            Assert.Equal(1, lanes.ServersOpen);
            rig.Step(1);
            rig.Flow.TryGetLaneState(new NodeId(2), out lanes);
            Assert.Equal(3, lanes.ServersOpen);

            Assert.True(rig.Submit(2, -7, out CommandRejection r2));
            Assert.Equal(CommandRejection.None, r2);
            rig.Step(2);
            rig.Flow.TryGetLaneState(new NodeId(2), out lanes);
            Assert.Equal(0, lanes.ServersOpen);
            Assert.Equal(3, lanes.ServerCount);

            Assert.True(rig.Submit(2, 2, out _));
            rig.Step(2);
            rig.Flow.TryGetLaneState(new NodeId(2), out lanes);
            Assert.Equal(2, lanes.ServersOpen);
        }

        [Fact]
        public void test_queue_throughput_servers_open_command_validation()
        {
            // 09 §9.8 Validate: length other than 8 and an unknown node are
            // MalformedPayload; a node that is not a Queue is NotPermitted.
            var rig = Rig.Create(Graphs.Line(3, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(2))));
            ulong due = rig.Host.CurrentTick + SimConstants.COMMAND_MIN_LEAD_TICKS;

            Assert.False(rig.Host.TrySubmit(new Command(due, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, new byte[7]), out CommandRejection shortPayload));
            Assert.Equal(CommandRejection.MalformedPayload, shortPayload);
            Assert.False(rig.Host.TrySubmit(new Command(due, SimConstants.PLAYER_LOCAL, CommandKind.SetServersOpen, new byte[9]), out CommandRejection longPayload));
            Assert.Equal(CommandRejection.MalformedPayload, longPayload);
            Assert.False(rig.Submit(77, 1, out CommandRejection unknown));
            Assert.Equal(CommandRejection.MalformedPayload, unknown);
            Assert.False(rig.Submit(1, 1, out CommandRejection source));
            Assert.Equal(CommandRejection.NotPermitted, source);
            Assert.False(rig.Submit(3, 1, out CommandRejection gate));
            Assert.Equal(CommandRejection.NotPermitted, gate);
        }
    }
}
