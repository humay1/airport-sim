using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// What boarding leaves behind (09 §9.7 "Exact rules", Q-033): Absorb
    /// "boards the flight's Departing cohorts on every Gate node, which leave
    /// the simulation", and it removes the missed cohorts from the simulation.
    /// So after Absorb no cohort of the flight exists on any node, the Sink
    /// included, and §9.7a's TryGetOutstanding(flight) is false. §9.2's head
    /// count is then conserved exactly: on-graph passengers (every node) plus
    /// boarded plus missed equals injected. Over a long run of distinct
    /// flights this also keeps §9.10's "no allocation in the update path" and
    /// its bounded cohort count, whatever the number of flights.
    /// Absorb is a downward call made during a tick, so it runs in the
    /// position-2 probe here.
    /// </summary>
    public sealed class BoardingTests
    {
        private const uint Source = 1;
        private const uint Lane = 2;
        private const uint Gate = 3;
        private const uint Sink = 4;

        private static int AbsorbInTick(Rig rig, ulong flight)
        {
            int boarded = -1;
            rig.Inject = (in TickContext ctx) => boarded = rig.Flow.Absorb(new NodeId(Sink), new FlightId(flight));
            rig.Step(1);
            rig.Inject = null;
            return boarded;
        }

        private static List<CohortId> Ids(Rig rig, uint node)
        {
            return new List<CohortId>(rig.Flow.CohortsAt(new NodeId(node)));
        }

        private static int LiveCohorts(Rig rig)
        {
            int live = 0;
            var nodes = rig.World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                live += rig.Flow.CohortsAt(nodes[i]).Count;
            }

            return live;
        }

        /// <summary>No cohort of the flight exists anywhere; it is not outstanding.</summary>
        private static void AssertFlightGone(Rig rig, ulong flight, IEnumerable<CohortId> formerCohorts, string where)
        {
            var id = new FlightId(flight);
            Assert.True(rig.Flow.PopulationForFlight(id, FlowDirection.Departing) == 0, where + ": flight " + flight + " still has passengers in the sim");
            Assert.False(rig.Flow.TryGetOutstanding(id, out OutstandingPassengers o), where + ": flight " + flight + " still outstanding, " + o.Count + " at node " + o.MostHeldAt.Value);
            foreach (CohortId c in formerCohorts)
            {
                Assert.False(rig.Flow.TryGetCohort(c, out PassengerCohort left), where + ": cohort " + c.Value + " still live on node " + left.Node.Value);
            }

            foreach (NodeId n in rig.World.Nodes())
            {
                foreach (CohortId c in rig.Flow.CohortsAt(n))
                {
                    Assert.True(rig.Flow.TryGetCohort(c, out PassengerCohort pc));
                    Assert.False(pc.Key.Flight == id, where + ": cohort " + c.Value + " of flight " + flight + " left on node " + n.Value);
                }
            }
        }

        [Fact]
        public void test_boarding_clean_absorb_boarded_passengers_leave_the_simulation()
        {
            // Everyone of flight 1 is on the gate; nobody is missed. Flight 2
            // waits on the gate too and is untouched.
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60))));
            rig.InjectNow(Source, 5, 1);
            rig.InjectNow(Source, 2, 2);
            rig.Step(6);
            Assert.Equal(7, rig.Pop(Gate));
            Assert.Equal(5, rig.Flow.PopulationForFlight(new FlightId(1), FlowDirection.Departing));
            Assert.False(rig.Flow.TryGetOutstanding(new FlightId(1), out _));
            var gateIds = new List<CohortId>();
            foreach (CohortId c in Ids(rig, Gate))
            {
                Assert.True(rig.Flow.TryGetCohort(c, out PassengerCohort pc));
                if (pc.Key.Flight == new FlightId(1))
                {
                    gateIds.Add(c);
                }
            }

            Assert.NotEmpty(gateIds);

            Assert.Equal(5, AbsorbInTick(rig, 1));
            Assert.Empty(rig.Events!.Missed);
            AssertFlightGone(rig, 1, gateIds, "after Absorb");
            Assert.Equal(0, rig.Pop(Sink));
            Assert.Empty(rig.Flow.CohortsAt(new NodeId(Sink)));
            Assert.Equal(2, rig.Pop(Gate));
            Assert.Equal(2, rig.Flow.PopulationForFlight(new FlightId(2), FlowDirection.Departing));
            Assert.Equal(2, FlowKit.TotalPopulation(rig.Flow, rig.World));

            // It stays gone: nothing reappears on later ticks.
            rig.Step(10);
            AssertFlightGone(rig, 1, gateIds, "10 ticks later");
            Assert.Equal(0, rig.Pop(Sink));

            // Flight 2 then boards cleanly as well, and the sim is empty.
            List<CohortId> flight2 = Ids(rig, Gate);
            Assert.Equal(2, AbsorbInTick(rig, 2));
            AssertFlightGone(rig, 2, flight2, "after the second Absorb");
            Assert.Equal(0, FlowKit.TotalPopulation(rig.Flow, rig.World));
            Assert.Equal(0, LiveCohorts(rig));
        }

        [Fact]
        public void test_boarding_absorb_with_missed_removes_boarded_and_missed_passengers()
        {
            // Five of flight 1 reach the gate; the lane is then closed and four
            // more wait on the queue: 5 boarded, 4 missed. Flight 2 has three on
            // the gate and is untouched.
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60))));
            rig.InjectNow(Source, 5, 1);
            rig.InjectNow(Source, 3, 2);
            rig.Step(6);
            Assert.Equal(8, rig.Pop(Gate));
            Assert.True(rig.Submit(Lane, 0, out _));
            rig.Step(1);
            rig.InjectNow(Source, 4, 1);
            rig.Step(6);
            Assert.Equal(4, rig.Pop(Lane));
            var former = new List<CohortId>();
            foreach (uint node in new[] { Gate, Lane })
            {
                foreach (CohortId c in Ids(rig, node))
                {
                    Assert.True(rig.Flow.TryGetCohort(c, out PassengerCohort pc));
                    if (pc.Key.Flight == new FlightId(1))
                    {
                        former.Add(c);
                    }
                }
            }

            Assert.True(former.Count >= 2);
            Assert.Equal(9, rig.Flow.PopulationForFlight(new FlightId(1), FlowDirection.Departing));

            Assert.Equal(5, AbsorbInTick(rig, 1));
            Assert.Single(rig.Events!.Missed);
            Assert.Equal(4, rig.Events.Missed[0].E.Count);
            AssertFlightGone(rig, 1, former, "after Absorb");
            Assert.Equal(0, rig.Pop(Sink));
            Assert.Equal(0, rig.Pop(Lane));
            Assert.Equal(3, rig.Pop(Gate));
            Assert.Equal(3, FlowKit.TotalPopulation(rig.Flow, rig.World));

            rig.Step(10);
            AssertFlightGone(rig, 1, former, "10 ticks later");
            Assert.Equal(0, rig.Pop(Sink));
        }

        [Fact]
        public void test_boarding_thousands_of_clean_flights_allocate_nothing_and_leave_no_state()
        {
            // 09 §9.10: no allocation in the update path, and a cohort count
            // bounded by merging, however long the run. One new flight a tick,
            // 3 passengers, each absorbed Lag ticks later when all of it is on
            // the gate (source at t, lane at t + 1, gate at t + 2), so every
            // boarding is clean. 3001 ticks board 2996 distinct flights,
            // well past any fixed per-flight pool. Allocation is measured in
            // four windows between the hash checkpoints of 08 §8.9 (ticks
            // 600, 1200, 1800, 2400), which allocate a SystemHashes array.
            const int PerFlight = 3;
            const ulong Lag = 5;
            const ulong Ticks = 3000;
            var rig = Rig.Create(Graphs.Line(1, 1, FlowKit.Lane), Graphs.Content(Graphs.Lane(Fx.FromInt(60))), recordEvents: false);
            var source = new NodeId(Source);
            var sink = new NodeId(Sink);
            long injected = 0;
            long boarded = 0;
            int unclean = 0;
            rig.Inject = (in TickContext ctx) =>
            {
                rig.Flow.Inject(FlowKit.Key(1 + ctx.Tick), PerFlight, source);
                injected += PerFlight;
                if (ctx.Tick >= Lag)
                {
                    int b = rig.Flow.Absorb(sink, new FlightId(1 + ctx.Tick - Lag));
                    boarded += b;
                    if (b != PerFlight)
                    {
                        unclean++;
                    }
                }
            };

            // Warm-up: ticks 0..600, checkpoints 0 and 600 included.
            rig.Step(601);
            long allocated = 0;
            var windows = new List<string>();
            for (int w = 0; w < 4; w++)
            {
                long before = Allocation.Start();
                rig.Step(599);
                long after = Allocation.Since(before);
                allocated += after;
                windows.Add(after.ToString(System.Globalization.CultureInfo.InvariantCulture));
                rig.Step(1);
            }

            rig.Inject = null;
            Assert.Equal(Ticks + 1, rig.Host.CurrentTick);
            Assert.True(allocated == 0, "update path allocated " + allocated + " bytes after warm-up (per window: " + string.Join(", ", windows) + ")");

            // Every flight boarded whole, and the head count is conserved
            // exactly: nothing boarded is still on any node.
            ulong absorbedFlights = Ticks + 1 - Lag;
            Assert.Equal(0, unclean);
            Assert.Equal((long)absorbedFlights * PerFlight, boarded);
            Assert.Equal(injected, FlowKit.TotalPopulation(rig.Flow, rig.World) + boarded);
            Assert.Equal(0, rig.Pop(Sink));
            Assert.Empty(rig.Flow.CohortsAt(sink));

            // No absorbed flight is outstanding or present, and only the Lag
            // live flights hold cohorts: the ceiling of Graphs.CohortCeiling,
            // four one-cohort nodes times Lag keys, 20.
            for (ulong f = 1; f <= absorbedFlights; f++)
            {
                var id = new FlightId(f);
                if (rig.Flow.TryGetOutstanding(id, out OutstandingPassengers o))
                {
                    Assert.Fail("absorbed flight " + f + " still outstanding: " + o.Count + " at node " + o.MostHeldAt.Value);
                }

                Assert.True(rig.Flow.PopulationForFlight(id, FlowDirection.Departing) == 0, "absorbed flight " + f + " still has passengers");
            }

            int ceiling = Graphs.CohortCeiling(rig.World, rig.Corridors, Fx.One, (int)Lag, 0);
            Assert.Equal(20, ceiling);
            int live = LiveCohorts(rig);
            Assert.True(live <= ceiling, live + " live cohorts exceed the ceiling " + ceiling);
            Assert.Equal((int)Lag * PerFlight, FlowKit.TotalPopulation(rig.Flow, rig.World));
        }

        [Fact]
        public void test_boarding_head_count_conserved_exactly_over_a_day()
        {
            // 09 §9.2 with §9.7's "leave the simulation": at every tick the
            // passengers on every node, the Sink included, plus those boarded,
            // plus those reported missed, equal those injected. The day
            // scenario absorbs a flight every hour, some cleanly and some with
            // passengers still upstream.
            var rig = Rig.Fixture(DayScenario.Content());
            var day = new DayScenario();
            long missed = 0;
            int missedSeen = 0;
            int cleanAbsorbs = 0;
            int lastMissedCount = 0;
            day.Run(rig, 0xB0A2_D1E6UL, (int)SimConstants.TICKS_PER_SIM_DAY, afterTick: t =>
            {
                FlowEvents ev = rig.Events!;
                for (; missedSeen < ev.Missed.Count; missedSeen++)
                {
                    missed += ev.Missed[missedSeen].E.Count;
                }

                // DayScenario absorbs at h * TICKS_PER_SIM_HOUR + 1, h >= 2.
                if (t % SimConstants.TICKS_PER_SIM_HOUR == 1 && t > SimConstants.TICKS_PER_SIM_HOUR)
                {
                    if (ev.Missed.Count == lastMissedCount)
                    {
                        cleanAbsorbs++;
                    }

                    lastMissedCount = ev.Missed.Count;
                }

                int onGraph = FlowKit.TotalPopulation(rig.Flow, rig.World);
                Assert.True(rig.Pop(Landside.Departed) == 0, "tick " + t + ": " + rig.Pop(Landside.Departed) + " passengers resident on the Sink");
                Assert.True(onGraph + day.Boarded + missed == day.Injected, "tick " + t + ": on-graph " + onGraph + " + boarded " + day.Boarded + " + missed " + missed + " != injected " + day.Injected);
            });

            Assert.True(day.Boarded > 0);
            Assert.True(cleanAbsorbs > 0, "the day had no clean boarding");
        }
    }
}
