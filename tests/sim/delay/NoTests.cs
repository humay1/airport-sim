using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>06 no_orphan_nodes and no_cycles, 14 §14.7 "Shape" and "Every reference points backwards".</summary>
    public sealed class NoTests
    {
        // Eight generated days: 115 200 ticks, under 07 L11a's 144 000.
        private const int LastDay = 7;

        [Fact]
        public void test_no_orphan_nodes()
        {
            // After every handler, every node of the touched flight resolves:
            // each leaf's Parent is a FlightTotal node of the same subject, and
            // each LinkedFlight is a retained record. At the end of each day,
            // every retained node's parent exists (pruning keeps rotation pairs
            // together, so no link dangles: §14.8).
            int leaves = 0;
            var run = new GeneratedRun(
                0x0024_0A11UL,
                Generator.SyntheticDay,
                model: false,
                perEvent: (d, flight, tick, context) =>
                {
                    foreach (DelayEventId id in d.LeavesOf(new FlightId(flight)))
                    {
                        Assert.True(d.TryGetNode(id, out DelayNode n), "leaf id with no node: " + context);
                        Assert.True(d.TryGetNode(n.Parent, out DelayNode parent), "orphan leaf " + id.Value + ": " + context);
                        Assert.True(parent.Kind == DelayNodeKind.FlightTotal && parent.Subject == n.Subject, "leaf parented to a non-root: " + context);
                        if (n.LinkedFlight.Value != 0UL)
                        {
                            Assert.True(d.TryGetFlightDelay(n.LinkedFlight, out _), "dangling LinkedFlight: " + context);
                        }

                        leaves++;
                    }
                });
            run.RunDays(LastDay, day =>
            {
                foreach (DelayNode n in Invariants.AllNodes(run.Rig.Delay))
                {
                    if (n.Kind == DelayNodeKind.Allocation)
                    {
                        Assert.True(run.Rig.Delay.TryGetNode(n.Parent, out _), "orphan node " + n.Id.Value + ", " + run.Context(day));
                    }
                    else
                    {
                        Assert.Equal(0UL, n.Parent.Value);
                    }

                    if (n.LinkedFlight.Value != 0UL)
                    {
                        Assert.True(run.Rig.Delay.TryGetFlightDelay(n.LinkedFlight, out _), "dangling link, " + run.Context(day));
                    }
                }
            });
            Assert.True(leaves > 1000, "too few leaves checked: " + leaves);
        }

        [Fact]
        public void test_no_cycles()
        {
            // At the end of each day, walking Parent then LinkedFlight's root from
            // every retained node only ever reaches strictly lower ids, so every
            // walk ends; and the walk from a leaf reaches a root in one Parent
            // step (no deeper nodes at Phase 0/1).
            int walked = 0;
            var run = new GeneratedRun(0x0024_C1C1UL, Generator.SyntheticDay, model: false);
            run.RunDays(LastDay, day =>
            {
                IDelaySystem d = run.Rig.Delay;
                foreach (DelayNode start in Invariants.AllNodes(d))
                {
                    var visited = new HashSet<ulong>();
                    var stack = new Stack<DelayNode>();
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        DelayNode n = stack.Pop();
                        if (!visited.Add(n.Id.Value))
                        {
                            // Reached twice along different edges (a linked
                            // tree's root via a leaf and directly); every edge
                            // already points strictly down, so this is no cycle.
                            continue;
                        }

                        if (n.Parent.Value != 0UL)
                        {
                            Assert.True(d.TryGetNode(n.Parent, out DelayNode p), run.Context(day));
                            Assert.True(p.Id.Value < n.Id.Value, "Parent not below child, " + run.Context(day));
                            Assert.True(p.Parent.Value == 0UL, "a node below a leaf, " + run.Context(day));
                            stack.Push(p);
                        }

                        if (n.LinkedFlight.Value != 0UL)
                        {
                            Assert.True(d.TryGetFlightDelay(n.LinkedFlight, out FlightDelay lr), run.Context(day));
                            Assert.True(d.TryGetNode(lr.Root, out DelayNode lroot), run.Context(day));
                            Assert.True(lroot.Id.Value < n.Id.Value, "LinkedFlight's root not below the leaf, " + run.Context(day));
                            foreach (DelayEventId id in d.LeavesOf(n.LinkedFlight))
                            {
                                Assert.True(d.TryGetNode(id, out DelayNode ll), run.Context(day));
                                stack.Push(ll);
                            }

                            stack.Push(lroot);
                        }

                        walked++;
                    }
                }
            });
            Assert.True(walked > 1000, "too few nodes walked: " + walked);
        }
    }
}
