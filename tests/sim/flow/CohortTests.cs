using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// 09 §9.2-§9.3: integer head counts conserved by every operation except
    /// Source and Sink; mandatory merge of equal keys at end of update, with
    /// the lowest CohortId surviving (Q-032) and the earliest EnteredNodeAt.
    /// </summary>
    public sealed class CohortTests
    {
        [Fact]
        public void test_cohort_merge_equal_keys_on_one_node_at_end_of_update()
        {
            // Flight 1 arrives at the closed queue in two parts, two ticks apart;
            // flight 2 with it. After the update the queue holds exactly one
            // cohort per key: flight 1's merged, keeping the lowest id and the
            // earlier EnteredNodeAt.
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.One)));
            CohortId a = rig.InjectNow(1, 5, 1);
            rig.Step(3);
            List<PassengerCohort> firstPart = rig.CohortsOn(2);
            Assert.Single(firstPart);
            ulong earliest = firstPart[0].EnteredNodeAt;
            CohortId firstId = firstPart[0].Id;

            rig.InjectNow(1, 7, 1);
            rig.InjectNow(1, 2, 2);
            rig.Step(3);

            List<PassengerCohort> onQueue = rig.CohortsOn(2);
            Assert.Equal(2, onQueue.Count);
            PassengerCohort f1 = onQueue.Find(c => c.Key.Flight.Value == 1);
            PassengerCohort f2 = onQueue.Find(c => c.Key.Flight.Value == 2);
            Assert.Equal(12, f1.Count);
            Assert.Equal(earliest, f1.EnteredNodeAt);

            // Q-033: off a Corridor, the merged DueAt is the merged EnteredNodeAt.
            Assert.Equal(f1.EnteredNodeAt, f1.DueAt);
            Assert.Equal(firstId, f1.Id);
            Assert.True(f1.Id.Value < f2.Id.Value);
            Assert.Equal(2, f2.Count);
            Assert.True(a.Value <= firstId.Value);
        }

        [Fact]
        public void test_cohort_corridor_merges_only_equal_due_at()
        {
            // Q-033: on a Corridor, equal keys merge only if DueAt is equal too,
            // so a merge never moves a release tick. 60 m at 1 m/s is 10 ticks.
            var g = new TestGraph()
                .Node(1, "source").Node(2, "corridor", 60).Node(3, "gate").Node(4, "sink")
                .Edge(1, 2).Edge(2, 3).Edge(3, 4);
            var rig = Rig.Create(g, Graphs.Content());

            // Two parts entered in the same tick share DueAt and merge.
            rig.InjectNow(1, 2, 1);
            rig.InjectNow(1, 3, 1);
            rig.Step(3);
            List<PassengerCohort> together = rig.CohortsOn(2);
            Assert.Single(together);
            Assert.Equal(5, together[0].Count);

            // One tick later, a third part: same key, DueAt one tick later. It
            // stays a separate cohort and is released on its own tick.
            rig.InjectNow(1, 4, 1);
            rig.Step(2);
            List<PassengerCohort> apart = rig.CohortsOn(2);
            Assert.Equal(2, apart.Count);
            Assert.Equal(apart[0].Key, apart[1].Key);
            Assert.NotEqual(apart[0].DueAt, apart[1].DueAt);
            ulong firstDue = System.Math.Min(apart[0].DueAt, apart[1].DueAt);
            ulong secondDue = System.Math.Max(apart[0].DueAt, apart[1].DueAt);

            rig.Step(20);
            FlowEvents ev = rig.Events!;
            Assert.Equal(2, ev.Arrived.Count);
            Assert.Equal(firstDue, ev.Arrived[0].Tick);
            Assert.Equal(5, ev.Arrived[0].E.Count);
            Assert.Equal(secondDue, ev.Arrived[1].Tick);
            Assert.Equal(4, ev.Arrived[1].E.Count);

            // At the gate (not a Corridor) the two merge.
            Assert.Single(rig.CohortsOn(3));
            Assert.Equal(9, rig.Pop(3));
        }

        [Fact]
        public void test_cohort_keys_differing_in_any_field_never_merge()
        {
            var other = new ContentId("pax_other");
            IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(FlowKit.Walker, Fx.One),
                FlowKit.Pax(other, Fx.One),
                Graphs.Lane(Fx.One),
            });
            var rig = Rig.Create(Graphs.Line(1, 0, FlowKit.Lane), content);
            rig.Flow.Inject(FlowKit.Key(1), 1, new NodeId(1));
            rig.Flow.Inject(FlowKit.Key(2), 1, new NodeId(1));
            rig.Flow.Inject(FlowKit.Key(1, other), 1, new NodeId(1));
            rig.Flow.Inject(FlowKit.Key(1, bags: true), 1, new NodeId(1));
            rig.Flow.Inject(FlowKit.Key(1, assist: true), 1, new NodeId(1));
            rig.Flow.Inject(FlowKit.Key(1), 1, new NodeId(1));
            rig.Step(4);

            // Five distinct keys; the two identical flight-1 cohorts merged.
            List<PassengerCohort> onQueue = rig.CohortsOn(2);
            Assert.Equal(5, onQueue.Count);
            Assert.Equal(6, rig.Pop(2));
        }

        [Fact]
        public void test_cohort_head_count_conserved_property()
        {
            // 09 §9.2: total head count is conserved across every operation
            // except Source (Inject) and Sink (Absorb, plus the passengers it
            // reports missed). Random injections, lane commands and absorptions
            // on the fixture; invariants checked after every tick.
            const ulong seed = 0xC0_4007UL;
            var rng = new SplitMix64(seed);
            for (int iteration = 0; iteration < 12; iteration++)
            {
                IContentIndex content = ContentIndexFactory.Create(new IContentDefinition[]
                {
                    FlowKit.Pax(FlowKit.Walker, Fx.FromRatio(rng.Range(5, 20), 10)),
                    FlowKit.Queue(Landside.SecurityProfile, Fx.FromRatio(rng.Range(0, 100), 10), rng.Range(1, 60), Fx.FromInt(rng.Range(0, 30)), Fx.FromInt(rng.Range(0, 5))),
                });
                var rig = Rig.Fixture(content);
                var open = new HashSet<ulong>();
                long injected = 0;
                long absorbed = 0;
                long missed = 0;
                int missedSeen = 0;
                string where0 = "seed 0x" + seed.ToString("X") + ", iteration " + iteration;
                for (int t = 0; t < 700; t++)
                {
                    if (rng.Range(1, 100) <= 30)
                    {
                        int n = rng.Range(1, 40);
                        rig.InjectNow(rng.Range(0, 1) == 0 ? Landside.Kerb : Landside.RailBox, n, (ulong)rng.Range(1, 6));
                        injected += n;
                    }

                    if (rng.Range(1, 100) <= 3)
                    {
                        Assert.True(rig.Submit(rng.Range(0, 1) == 0 ? Landside.SecurityA : Landside.SecurityB, rng.Range(0, 3), out _));
                    }

                    // Absorb is a downward call made during a tick (sim.airside's, 09
                    // §9.7); the probe at position 2 makes it before flow updates.
                    rig.Inject = null;
                    if (rng.Range(1, 100) <= 2)
                    {
                        var flight = new FlightId((ulong)rng.Range(1, 6));
                        rig.Inject = (in TickContext ctx) => absorbed += rig.Flow.Absorb(new NodeId(Landside.Departed), flight);
                    }

                    rig.Step(1);
                    for (; missedSeen < rig.Events!.Missed.Count; missedSeen++)
                    {
                        missed += rig.Events.Missed[missedSeen].E.Count;
                    }

                    TrackEpisodes(rig.Events!, open);
                    string where = where0 + ", tick " + t;
                    int total = rig.CheckInvariants(where, open);
                    int sinkPop = rig.Pop(Landside.Departed);
                    Assert.True(total - sinkPop + absorbed + missed == injected, where + ": in " + injected + " != on-graph " + (total - sinkPop) + " + absorbed " + absorbed + " + missed " + missed);
                }
            }
        }

        /// <summary>
        /// Rebuilds the set of cohorts in an open blocking episode from the
        /// ordered event log: an id is open if its last episode event is Blocked.
        /// </summary>
        internal static void TrackEpisodes(FlowEvents ev, ISet<ulong> open)
        {
            var last = new Dictionary<ulong, bool>();
            foreach (string l in ev.Log)
            {
                string[] parts = l.Split(' ');
                if (parts.Length > 2 && (parts[1] == "blocked" || parts[1] == "unblocked"))
                {
                    last[ulong.Parse(parts[2].Substring(1), System.Globalization.CultureInfo.InvariantCulture)] = parts[1] == "blocked";
                }
            }

            open.Clear();
            foreach (KeyValuePair<ulong, bool> kv in last)
            {
                if (kv.Value)
                {
                    open.Add(kv.Key);
                }
            }
        }
    }
}
