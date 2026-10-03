using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.7 (Q-084): KindOf(node) returns the NodeKind the system's
    /// FlowGraph gives the node (§9.11). An unknown node throws
    /// ArgumentException, as SetPromoted does. It is a read-only query that
    /// may be called at any time, inside a Tick included.
    /// </summary>
    public sealed class KindOfTests
    {
        // tests/fixtures/flow/phase0-landside.flow.json, node by node. It holds
        // one node of each NodeKind, and two of Source, Corridor and Queue.
        private static readonly (uint Node, NodeKind Kind)[] FixtureKinds =
        {
            (Landside.Kerb, NodeKind.Source),
            (Landside.RailBox, NodeKind.Source),
            (Landside.CheckInHall, NodeKind.Hall),
            (Landside.LandsideCorridor, NodeKind.Corridor),
            (Landside.SecurityA, NodeKind.Queue),
            (Landside.SecurityB, NodeKind.Queue),
            (Landside.AirsideCorridor, NodeKind.Corridor),
            (Landside.Gate, NodeKind.Gate),
            (Landside.Departed, NodeKind.Sink),
        };

        private static readonly uint[] UnknownNodes = { 0U, 10U, 999U, uint.MaxValue };

        private static void AssertFixtureKinds(IFlowSystem flow, string where)
        {
            foreach ((uint node, NodeKind kind) in FixtureKinds)
            {
                Assert.True(kind == flow.KindOf(new NodeId(node)), where + ": KindOf(" + node + ") is " + flow.KindOf(new NodeId(node)) + ", the fixture says " + kind);
            }
        }

        [Fact]
        public void test_kind_of_returns_graph_kinds_and_throws_on_unknown_node()
        {
            // The fixture covers every NodeKind, so a constant answer cannot pass.
            var covered = new HashSet<NodeKind>();
            foreach ((uint _, NodeKind kind) in FixtureKinds)
            {
                covered.Add(kind);
            }

            Assert.Equal(Enum.GetValues(typeof(NodeKind)).Length, covered.Count);

            var rig = Rig.Fixture(DayScenario.Content(), recordEvents: false);
            Assert.Equal(FixtureKinds.Length, rig.World.Nodes().Count);

            // Between ticks, before the first one.
            AssertFixtureKinds(rig.Flow, "before tick 0");
            foreach (uint bad in UnknownNodes)
            {
                Assert.Throws<ArgumentException>(() => rig.Flow.KindOf(new NodeId(bad)));
            }

            // Inside a Tick (the probe runs at sim.schedule's position, 2), with
            // passengers moving through the graph. The answers never change.
            int callsInTick = 0;
            rig.Inject = (in TickContext ctx) =>
            {
                AssertFixtureKinds(rig.Flow, "inside tick " + ctx.Tick);
                foreach (uint bad in UnknownNodes)
                {
                    Assert.Throws<ArgumentException>(() => rig.Flow.KindOf(new NodeId(bad)));
                }

                callsInTick++;
            };
            rig.InjectNow(Landside.Kerb, 40, 1);
            rig.InjectNow(Landside.RailBox, 25, 1);
            rig.Step(300);
            Assert.Equal(300, callsInTick);
            Assert.True(rig.Pop(Landside.Kerb) + rig.Pop(Landside.RailBox) < 65, "no passenger left a source, so the in-tick calls saw no movement");

            // Read-only: the state hash is the same before and after the queries.
            rig.Inject = null;
            ulong hash = rig.Flow.ComputeStateHash();
            AssertFixtureKinds(rig.Flow, "after tick 299");
            foreach (uint bad in UnknownNodes)
            {
                Assert.Throws<ArgumentException>(() => rig.Flow.KindOf(new NodeId(bad)));
            }

            Assert.Equal(hash, rig.Flow.ComputeStateHash());
        }
    }
}
