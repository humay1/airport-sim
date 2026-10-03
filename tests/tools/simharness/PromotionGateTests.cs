using System.Collections.Generic;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.World;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.PromotionKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-014. HarnessGates.Promotion's second run promotes the lowest-id Gate and
    /// draws it after every tick (19 §19.2d, Q-084, owner 2026-10-02), tested as
    /// 19 §19.9 composes it. Promotion is outcome-neutral (09 §9.1), so the
    /// report alone cannot show it happened: the calls are observed through the
    /// tests' own IFlowSystem and IWorldSystem (PromotionKit).
    /// </summary>
    public sealed class PromotionGateTests
    {
        private static readonly IContentIndex Content = new HarnessTestKit.EmptyContent();

        /// <summary>G of §19.9: a test world with Nodes() [3, 7] and a spy flow with 3 → Source, 7 → Gate.</summary>
        private static Rig G(NodeKind kindOf7 = NodeKind.Gate, ushort flowPosition = 4, bool withWorld = true, bool withFlow = true)
        {
            return new Rig((b, call, log) =>
            {
                if (withWorld)
                {
                    b.Register(new TestWorld(log, 3, 7));
                }

                if (withFlow)
                {
                    b.Register(new SpyFlow(log, new Dictionary<uint, NodeKind> { { 3, NodeKind.Source }, { 7, kindOf7 } }, flowPosition));
                }
            });
        }

        private static string PassReport(uint ticks, uint checkpoints, string final)
        {
            return "PASS determinism_promotion ticks=" + ticks + " checkpoints=" + checkpoints + " final=" + final;
        }

        // ------------------------------------------------------------ §19.9, first test

        [Fact]
        public void test_harness_gates_promotion_promotes_lowest_gate_and_draws_it_every_tick()
        {
            Rig hashRig = G();
            string h = HarnessGates.FinalHash(Content, hashRig.Composer.Compose, Seed, Ticks);
            // §19.2d "Scope": FinalHash wraps nothing and promotes nothing.
            Assert.Single(hashRig.Logs);
            Assert.Empty(hashRig.Logs[0].Entries);

            Rig rig = G();
            GateResult r = HarnessGates.Promotion(Content, rig.Composer.Compose, Seed, Ticks);

            Assert.Equal(2, rig.Composer.Calls);
            Assert.Empty(rig.Logs[0].Entries);
            Assert.Equal(PromotedRunLog(Ids(3, 7), 7, Ticks), rig.Logs[1].Entries);
            Assert.True(r.Passed, r.Report);
            Assert.Equal(PassReport(Ticks, 3, h), r.Report);
        }

        // ------------------------------------------------------------ §19.9, second test

        [Fact]
        public void test_harness_gates_promotion_promotes_nothing_without_a_gate()
        {
            // (a) A spy flow alone, with no world: flow without world calls nothing (§19.2d step 1).
            AssertPassesWithLogs("a", G(withWorld: false), new List<string>());

            // (b) An empty Nodes(): one Nodes() and no KindOf (step 3).
            var b = new Rig((builder, call, log) =>
            {
                builder.Register(new TestWorld(log));
                builder.Register(new SpyFlow(log, new Dictionary<uint, NodeKind>()));
            });
            AssertPassesWithLogs("b", b, new List<string> { Nodes(0) });

            // (c) G with 7 → Sink: every node is asked and none is a Gate (step 3).
            AssertPassesWithLogs("c", G(kindOf7: NodeKind.Sink), new List<string> { Nodes(0), KindOf(3, 0), KindOf(7, 0) });

            // (d) G with the spy flow at position 5: it is never flow, so flow is
            // missing and not even Nodes() is called ("Finding the systems", step 1).
            AssertPassesWithLogs("d", G(flowPosition: 5), new List<string>());

            // (e) G's test world alone: flow is missing (step 1).
            AssertPassesWithLogs("e", G(withFlow: false), new List<string>());
        }

        private static void AssertPassesWithLogs(string label, Rig rig, List<string> run2)
        {
            GateResult r = HarnessGates.Promotion(Content, rig.Composer.Compose, Seed, Ticks);
            Assert.True(r.Passed, "(" + label + ") " + r.Report);
            Assert.StartsWith(PassReport(Ticks, 3, string.Empty), r.Report);
            Assert.Equal(2, rig.Composer.Calls);
            Assert.True(rig.Logs[0].Entries.Count == 0, "(" + label + ") run 1 recorded: " + string.Join(" ", rig.Logs[0].Entries));
            Assert.True(Same(run2, rig.Logs[1].Entries),
                "(" + label + ") run 2 recorded [" + string.Join(" ", rig.Logs[1].Entries) + "], expected [" + string.Join(" ", run2) + "]");
        }

        private static bool Same(List<string> expected, List<string> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                if (expected[i] != actual[i])
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------ §19.9, third test

        [Fact]
        public void test_harness_gates_promotion_promotes_same_node_on_every_call()
        {
            // Two Gates, 5 and 9: the lowest-id one is taken, and KindOf stops at it
            // (§19.2d step 2), so 9 is never asked.
            GateResult[] results = new GateResult[2];
            for (int i = 0; i < 2; i++)
            {
                var rig = new Rig((b, call, log) =>
                {
                    b.Register(new TestWorld(log, 2, 5, 9));
                    b.Register(new SpyFlow(log, new Dictionary<uint, NodeKind>
                    {
                        { 2, NodeKind.Source },
                        { 5, NodeKind.Gate },
                        { 9, NodeKind.Gate },
                    }));
                });
                results[i] = HarnessGates.Promotion(Content, rig.Composer.Compose, Seed, Ticks);

                Assert.Equal(2, rig.Composer.Calls);
                Assert.Empty(rig.Logs[0].Entries);
                Assert.Equal(PromotedRunLog(Ids(2, 5), 5, Ticks), rig.Logs[1].Entries);
                Assert.True(results[i].Passed, results[i].Report);
            }

            Assert.Equal(results[0].Report, results[1].Report);
        }

        // ------------------------------------------------------------ §19.9, fourth test

        [Fact]
        public void test_harness_gates_promotion_still_fails_on_divergence_with_a_gate()
        {
            var rig = new Rig((b, call, log) =>
            {
                b.Register(new TestWorld(log, 3, 7));
                b.Register(new SpyFlow(log, new Dictionary<uint, NodeKind> { { 3, NodeKind.Source }, { 7, NodeKind.Gate } }));
                b.Register(new HarnessTestKit.Probe(5, drifts: call == 2, driftFromTick: 1000));
            });

            GateResult r = HarnessGates.Promotion(Content, rig.Composer.Compose, Seed, Ticks);

            // SystemHashes index 2 is the probe: the world and the flow hash 0.
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_promotion tick=1200 at=system:2", r.Report);
            Assert.Equal(2, rig.Composer.Calls);
            Assert.Empty(rig.Logs[0].Entries);
            // The failure comes from the drift, not from a missing promotion.
            Assert.Equal(PromotedRunLog(Ids(3, 7), 7, Ticks), rig.Logs[1].Entries);
        }

        // ------------------------------------------------------------ §19.9, fifth test

        /// <summary>
        /// The §19.6 kit, with its world and flow each wrapped in a forwarding spy
        /// at the same position. 14 400 ticks per run, three runs (two in the gate,
        /// one for FinalHash), 43 200 in total: not Slow by 07 L11a rule (a).
        /// </summary>
        [Fact]
        public void test_harness_gates_promotion_phase0_kit_promotes_real_gate_and_passes()
        {
            const uint ticks = HarnessTestKit.TicksPerDay;
            IContentIndex content = KillGateKit.Content();

            var hashKit = new WrappedPhase0Composer();
            string h = HarnessGates.FinalHash(content, hashKit.Compose, Seed, ticks);

            var kit = new WrappedPhase0Composer();
            GateResult r = HarnessGates.Promotion(content, kit.Compose, Seed, ticks);

            Assert.Equal(2, kit.Logs.Count);
            Assert.Empty(kit.Logs[0].Entries);
            Assert.Equal(0, kit.Worlds[0].NodesCalls);
            Assert.Empty(kit.Flows[0].Draws);

            // The fixture's only Gate is NodeId(8) (19 §19.2d "In the CLI"), so
            // KindOf runs over 1 to 8 and stops there; 9, the Sink, is never asked.
            Assert.Equal(1, kit.Worlds[1].NodesCalls);
            var expected = new List<string>();
            for (uint n = 1; n <= 8; n++)
            {
                expected.Add(KindOf(n, 0));
            }

            expected.Add(SetPromoted(8, true, 0));
            for (uint k = 1; k <= ticks; k++)
            {
                expected.Add(AgentsAt(8, k));
            }

            Assert.Equal(expected, kit.Logs[1].Entries);

            // 09 §9.7 "Promotion rules": on a promoted node, one view per passenger.
            long sum = 0;
            List<(int Count, int Population)> draws = kit.Flows[1].Draws;
            Assert.Equal((int)ticks, draws.Count);
            for (int i = 0; i < draws.Count; i++)
            {
                Assert.True(draws[i].Count == draws[i].Population,
                    "AgentsAt after tick " + (i + 1) + " returned " + draws[i].Count + " views for a population of " + draws[i].Population);
                sum += draws[i].Count;
            }

            Assert.True(sum > 0, "the gate held no passenger after any of the day's ticks, so nothing was drawn");

            Assert.True(r.Passed, r.Report);
            Assert.Equal(PassReport(ticks, 24, h), r.Report);
        }

        /// <summary>
        /// §19.6's kit composer (KillGateKit.Phase0Composer), in its construction
        /// and registration order, with the world at position 1 and the flow at
        /// position 4 each registered through a forwarding spy. sim.schedule and
        /// the boarding probe hold the unwrapped systems, so only the harness's
        /// calls reach the spies.
        /// </summary>
        private sealed class WrappedPhase0Composer
        {
            private readonly byte[] _schedule = KillGateKit.Fixture(KillGateKit.SchedulePath);
            private readonly byte[] _world = KillGateKit.Fixture(KillGateKit.WorldPath);
            private readonly byte[] _flow = KillGateKit.Fixture(KillGateKit.FlowPath);

            public readonly List<CallLog> Logs = new List<CallLog>();
            public readonly List<ForwardingWorld> Worlds = new List<ForwardingWorld>();
            public readonly List<ForwardingFlow> Flows = new List<ForwardingFlow>();

            public void Compose(ISimHostBuilder builder)
            {
                SystemServices services = builder.Services;

                WalkGraph walk = WorldFactory.CreateGraphLoader().Load(_world, "phase0-landside.json");
                IWorldSystem world = WorldFactory.CreateSystem(services, walk);
                FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(_flow, "phase0-landside.flow.json", world);
                IFlowSystem flow = FlowFactory.CreateSystem(services, flowGraph, world);
                ScheduleTable table = ScheduleFactory.CreateLoader().Load(_schedule, "phase0-200.csv");
                IScheduleSystem schedule = ScheduleFactory.CreateSystem(services, table, flow);
                var boarding = new KillGateKit.BoardingProbe(schedule, flow);

                var log = new CallLog();
                var worldSpy = new ForwardingWorld(world);
                var flowSpy = new ForwardingFlow(flow, log);
                Logs.Add(log);
                Worlds.Add(worldSpy);
                Flows.Add(flowSpy);

                builder.Register(worldSpy);
                builder.Register(schedule);
                builder.Register(boarding);
                builder.Register(flowSpy);
            }
        }
    }
}
