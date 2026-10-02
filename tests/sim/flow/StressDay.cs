using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Flow.Tests
{
    /// <summary>
    /// Counts sim.flow's events without allocating, subscribed at sim.delay's
    /// registry position (7), as the production consumer is.
    /// </summary>
    internal sealed class FlowCounters
    {
        public long Blocked;
        public long Unblocked;
        public long ArrivedPassengers;
        public long MissedPassengers;
        public long Thresholds;

        public FlowCounters(IEventBus bus)
        {
            var id = new SystemId(7);
            bus.Subscribe<FlowBlocked>(id, (in EventEnvelope env, in FlowBlocked e, in TickContext ctx) => Blocked++);
            bus.Subscribe<FlowUnblocked>(id, (in EventEnvelope env, in FlowUnblocked e, in TickContext ctx) => Unblocked++);
            bus.Subscribe<PassengersArrivedAtGate>(id, (in EventEnvelope env, in PassengersArrivedAtGate e, in TickContext ctx) => ArrivedPassengers += e.Count);
            bus.Subscribe<PassengersMissedFlight>(id, (in EventEnvelope env, in PassengersMissedFlight e, in TickContext ctx) => MissedPassengers += e.Count);
            bus.Subscribe<QueueThresholdExceeded>(id, (in EventEnvelope env, in QueueThresholdExceeded e, in TickContext ctx) => Thresholds++);
            bus.Subscribe<QueueThresholdCleared>(id, (in EventEnvelope env, in QueueThresholdCleared e, in TickContext ctx) => Thresholds++);
        }
    }

    /// <summary>
    /// The T-011 stress fixture: a full sim-day of departing cohort traffic
    /// summing to exactly 30 000 passengers (the Phase 0 kill gate,
    /// tasks/queue.md), through a representative terminal of 63 nodes:
    /// sources -> corridors -> check-in halls -> corridors -> two security
    /// queues -> corridors -> airside hall -> piers of corridor segments ->
    /// 24 gates -> 3 sinks, with a passport queue in front of the third pier.
    ///
    /// The day's shape is fixture sizing, not balance (11 §11.10): 200
    /// departures between 06:00 and 22:55 in banks, the largest at 07:00,
    /// 80..220 passengers each. Each flight's passengers are expanded as
    /// sim.schedule expands them (11 §11.6): split across a five-bucket
    /// show-up curve and then across the four (bag, assist) classes, each by
    /// largest remainder, one Inject per non-empty (bucket, class), and the
    /// flight is absorbed at its STD, as sim.airside would at gate close.
    /// The day repeats: day d uses flight ids 200 d + 1 .. 200 d + 200.
    ///
    /// Security runs 9 + 7 lanes at 2.5 pax/min, so queues build at the
    /// morning banks and cross their 15-minute threshold. Every queue's
    /// CapacityStanding (1 000 000) is above any population the day can
    /// hold, so no node is ever full (09 §9.5) and no cohort is ever
    /// blocked: this fixture measures volume and queueing, not spillback.
    /// </summary>
    internal sealed class StressDay
    {
        public const int Flights = 200;
        public const int DailyPassengers = 30000;
        public const ulong Day = SimConstants.TICKS_PER_SIM_DAY;

        public static readonly ContentId Business = new ContentId("pax_stress_business");
        public static readonly ContentId Leisure = new ContentId("pax_stress_leisure");
        public static readonly ContentId Security = new ContentId("queue_stress_security");
        public static readonly ContentId Passport = new ContentId("queue_stress_passport");
        public static readonly Fx BusinessSpeed = Fx.FromRatio(14, 10);
        public static readonly Fx LeisureSpeed = Fx.FromRatio(11, 10);

        // Departures per STD hour, 06:00..22:00 (sums to Flights).
        private static readonly int[] BankSizes = { 14, 22, 16, 12, 10, 10, 12, 12, 10, 10, 12, 14, 14, 12, 10, 6, 4 };
        private const int FirstBankHour = 6;

        // Show-up curve (fixture values): minutes before STD, share permille.
        private static readonly int[] ShowUpMinutes = { 150, 120, 90, 60, 40 };
        private static readonly int[] ShowUpPermille = { 100, 200, 300, 250, 150 };

        // (bag, assist) class weights, class index (bag ? 2 : 0) + (assist ? 1 : 0),
        // from hold_bag 600 permille and assist 20 permille.
        private static readonly int[] ClassWeights = { 400 * 980, 400 * 20, 600 * 980, 600 * 20 };

        private static readonly uint[] SourceIds = { 1, 2, 3, 4 };
        private static readonly uint[] SinkIds = { 190, 191, 192 };

        internal struct Injection
        {
            public ulong Tick;
            public CohortKey Key;
            public int Count;
            public NodeId At;
        }

        internal struct Boarding
        {
            public ulong Tick;
            public NodeId Sink;
            public FlightId Flight;
        }

        internal struct Flight
        {
            public ulong Std;
            public int Pax;
            public ContentId Profile;
            public uint Source;
            public uint Sink;
        }

        public Rig Rig = null!;
        public FlowClock Clock = null!;
        public FlowCounters Counters = null!;
        public HashSet<uint> Corridors = null!;
        public Injection[] Injections = null!;
        public Boarding[] Boardings = null!;
        public long Injected;
        public long Boarded;
        private int _nextInjection;
        private int _nextBoarding;

        public static IContentIndex Content()
        {
            return ContentIndexFactory.Create(new IContentDefinition[]
            {
                FlowKit.Pax(Business, BusinessSpeed),
                FlowKit.Pax(Leisure, LeisureSpeed),
                FlowKit.Queue(Security, Fx.Parse("2.5"), 1000000, Fx.FromInt(15), Fx.FromInt(5)),
                FlowKit.Queue(Passport, Fx.FromInt(3), 1000000, Fx.FromInt(15), Fx.FromInt(5)),
            });
        }

        public static TestGraph Terminal()
        {
            var g = new TestGraph();

            // Landside: four entrances, two check-in halls.
            g.Node(1, "source", 10).Node(2, "source", 10).Node(3, "source", 10).Node(4, "source", 10);
            g.Node(11, "corridor", 80).Node(12, "corridor", 60).Node(13, "corridor", 150).Node(14, "corridor", 200);
            g.Node(20, "hall", 90).Node(21, "hall", 90);
            g.Edge(1, 11).Edge(11, 20).Edge(2, 12).Edge(12, 21).Edge(3, 13).Edge(13, 20).Edge(4, 14).Edge(14, 21);

            // Each hall reaches both security queues; the near one is shorter.
            g.Node(30, "corridor", 40).Node(31, "corridor", 110).Node(32, "corridor", 110).Node(33, "corridor", 40);
            g.Queue(40, 12, 9, Security, 25).Queue(41, 10, 7, Security, 25);
            g.Edge(20, 30).Edge(30, 40).Edge(20, 31).Edge(31, 41).Edge(21, 32).Edge(32, 40).Edge(21, 33).Edge(33, 41);

            // Airside hall, two piers direct and a third behind passport control.
            g.Node(50, "corridor", 70).Node(51, "corridor", 70).Node(60, "hall", 120);
            g.Edge(40, 50).Edge(50, 60).Edge(41, 51).Edge(51, 60);
            g.Node(61, "corridor", 90).Node(62, "corridor", 150).Node(63, "corridor", 60);
            g.Queue(70, 6, 6, Passport, 20).Node(64, "corridor", 100);
            g.Edge(60, 61).Edge(60, 62).Edge(60, 63).Edge(63, 70).Edge(70, 64);

            Pier(g, 61, 110, 120, 190);
            Pier(g, 62, 130, 140, 191);
            Pier(g, 64, 150, 160, 192);
            return g;
        }

        /// <summary>Four 60 m corridor segments, two gates off each, one sink.</summary>
        private static void Pier(TestGraph g, uint entry, uint firstSegment, uint firstGate, uint sink)
        {
            g.Node(sink, "sink");
            uint previous = entry;
            for (uint s = 0; s < 4; s++)
            {
                uint seg = firstSegment + s;
                g.Node(seg, "corridor", 60).Edge(previous, seg);
                for (uint k = 0; k < 2; k++)
                {
                    uint gate = firstGate + 2 * s + k;
                    g.Node(gate, "gate", 30).Edge(seg, gate).Edge(gate, sink);
                }

                previous = seg;
            }
        }

        /// <summary>One day's flights, from a SplitMix64 sequence seeded by the caller.</summary>
        public static Flight[] Schedule(ulong seed)
        {
            var rng = new SplitMix64(seed);
            var flights = new List<Flight>();
            for (int h = 0; h < BankSizes.Length; h++)
            {
                for (int k = 0; k < BankSizes[h]; k++)
                {
                    flights.Add(new Flight
                    {
                        Std = (ulong)(FirstBankHour + h) * SimConstants.TICKS_PER_SIM_HOUR + (ulong)(rng.Range(0, 11) * 5) * SimConstants.TICKS_PER_SIM_MINUTE,
                        Profile = rng.Range(0, 3) == 0 ? Business : Leisure,
                        Source = SourceIds[rng.Range(0, SourceIds.Length - 1)],
                        Sink = SinkIds[rng.Range(0, SinkIds.Length - 1)],
                    });
                }
            }

            // Paired deviations around 150 keep the day's total exact.
            for (int i = 0; i + 1 < flights.Count; i += 2)
            {
                int d = rng.Range(0, 70);
                Flight a = flights[i];
                Flight b = flights[i + 1];
                a.Pax = 150 + d;
                b.Pax = 150 - d;
                flights[i] = a;
                flights[i + 1] = b;
            }

            flights.Sort((x, y) => x.Std.CompareTo(y.Std));
            return flights.ToArray();
        }

        /// <summary>11 §11.6's largest remainder: ties by ascending index, sum exact.</summary>
        public static int[] Split(int total, int[] weights)
        {
            long sum = 0;
            foreach (int w in weights)
            {
                sum += w;
            }

            var parts = new int[weights.Length];
            var remainders = new long[weights.Length];
            int given = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                parts[i] = (int)(total * (long)weights[i] / sum);
                remainders[i] = total * (long)weights[i] % sum;
                given += parts[i];
            }

            for (; given < total; given++)
            {
                int best = 0;
                for (int i = 1; i < weights.Length; i++)
                {
                    if (remainders[i] > remainders[best])
                    {
                        best = i;
                    }
                }

                parts[best]++;
                remainders[best] = -1;
            }

            return parts;
        }

        /// <summary>
        /// The expanded plan for <paramref name="days"/> repeats of the day,
        /// ordered by tick, then (FlightId, bucket, class) as 11 §11.6 drains.
        /// </summary>
        public static void Plan(Flight[] day, int days, out Injection[] injections, out Boarding[] boardings)
        {
            var inj = new List<(ulong Tick, ulong Flight, int Bucket, int Class, Injection I)>();
            var brd = new List<Boarding>();
            for (int d = 0; d < days; d++)
            {
                for (int f = 0; f < day.Length; f++)
                {
                    ulong flight = (ulong)(d * day.Length + f + 1);
                    ulong std = (ulong)d * Day + day[f].Std;
                    int[] buckets = Split(day[f].Pax, ShowUpPermille);
                    for (int b = 0; b < buckets.Length; b++)
                    {
                        int[] classes = Split(buckets[b], ClassWeights);
                        for (int c = 0; c < classes.Length; c++)
                        {
                            if (classes[c] == 0)
                            {
                                continue;
                            }

                            var key = new CohortKey(new FlightId(flight), FlowDirection.Departing, day[f].Profile, c >= 2, (c & 1) == 1);
                            ulong tick = std - (ulong)ShowUpMinutes[b] * SimConstants.TICKS_PER_SIM_MINUTE;
                            inj.Add((tick, flight, b, c, new Injection { Tick = tick, Key = key, Count = classes[c], At = new NodeId(day[f].Source) }));
                        }
                    }

                    brd.Add(new Boarding { Tick = std, Sink = new NodeId(day[f].Sink), Flight = new FlightId(flight) });
                }
            }

            inj.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick)
                : x.Flight != y.Flight ? x.Flight.CompareTo(y.Flight)
                : x.Bucket != y.Bucket ? x.Bucket.CompareTo(y.Bucket)
                : x.Class.CompareTo(y.Class));
            brd.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.Flight.Value.CompareTo(y.Flight.Value));
            injections = new Injection[inj.Count];
            for (int i = 0; i < inj.Count; i++)
            {
                injections[i] = inj[i].I;
            }

            boardings = brd.ToArray();
        }

        /// <summary>
        /// Composes sim.world at 1, the injector at 2 (sim.schedule's
        /// position, doubling as sim.airside's Absorb), sim.flow at 4 behind
        /// <see cref="TimedSystem"/>, built with <see cref="FlowClock"/>'s
        /// shimmed services (03 Q-064), and the event counters at 7.
        /// </summary>
        public static StressDay Create(ulong seed, int days)
        {
            var s = new StressDay();
            TestGraph graph = Terminal();
            s.Corridors = graph.Corridors();
            Plan(Schedule(seed), days, out s.Injections, out s.Boardings);

            var rig = new Rig { Checkpoints = new RecordingSink(), Corridors = s.Corridors };
            ISimHostBuilder b = FlowKit.Builder(Content(), seed, rig.Checkpoints);
            rig.World = graph.World(b);
            FlowGraph flowGraph = FlowFactory.CreateGraphLoader().Load(Fixtures.Utf8(graph.FlowJson()), "stress.flow.json", rig.World);
            s.Clock = new FlowClock((int)Day);
            rig.Flow = FlowFactory.CreateSystem(s.Clock.Shim(b.Services), flowGraph, rig.World);
            rig.Injector = new ProbeSystem(2) { OnTick = s.Drive };
            b.Register(rig.World);
            b.Register(rig.Injector);
            b.Register(new TimedSystem(rig.Flow, s.Clock));
            s.Counters = new FlowCounters(b.Services.Events);
            b.Register(new ProbeSystem(7));
            rig.Host = b.Build();
            s.Rig = rig;
            return s;
        }

        /// <summary>The injector's Tick: drains the plan due now; allocates nothing.</summary>
        private void Drive(in TickContext ctx)
        {
            for (; _nextInjection < Injections.Length && Injections[_nextInjection].Tick == ctx.Tick; _nextInjection++)
            {
                ref Injection i = ref Injections[_nextInjection];
                Rig.Flow.Inject(i.Key, i.Count, i.At);
                Injected += i.Count;
            }

            for (; _nextBoarding < Boardings.Length && Boardings[_nextBoarding].Tick == ctx.Tick; _nextBoarding++)
            {
                ref Boarding a = ref Boardings[_nextBoarding];
                Boarded += Rig.Flow.Absorb(a.Sink, a.Flight);
            }
        }

        /// <summary>One Step(1), sampled by <see cref="Clock"/> while it records.</summary>
        public void Step()
        {
            Clock.Begin();
            Rig.Step(1);
            Clock.End();
        }

        public int LiveCohorts()
        {
            int live = 0;
            IReadOnlyList<NodeId> nodes = Rig.World.Nodes();
            for (int i = 0; i < nodes.Count; i++)
            {
                live += Rig.Flow.CohortsAt(nodes[i]).Count;
            }

            return live;
        }

        /// <summary>
        /// The live-cohort ceiling after each tick of day <paramref name="day"/>
        /// (09 §9.10: fixture sizing, Q-033), derived as Graphs.CohortCeiling
        /// derives it, per key. Once merged (§9.3), an unblocked key holds at
        /// most one cohort per non-Corridor node, and at most traversalTicks
        /// cohorts on a Corridor (one per entry tick, since Corridor cohorts
        /// merge only on equal DueAt). traversalTicks depends on the key's pax
        /// profile. A key is live from the tick of its first Inject until its
        /// flight is absorbed at STD, which removes every cohort of the flight
        /// (09 §9.7 "Absorb"); no cohort is ever blocked in this fixture, so
        /// there is no open-episode term. Index i is the tick day x Day + i.
        /// </summary>
        public long[] CeilingByTick(int day)
        {
            long perBusiness = PerKey(BusinessSpeed);
            long perLeisure = PerKey(LeisureSpeed);
            var first = new Dictionary<CohortKey, ulong>();
            foreach (Injection i in Injections)
            {
                if (!first.ContainsKey(i.Key))
                {
                    first[i.Key] = i.Tick;
                }
            }

            var std = new Dictionary<ulong, ulong>();
            foreach (Boarding a in Boardings)
            {
                std[a.Flight.Value] = a.Tick;
            }

            ulong dayStart = (ulong)day * Day;
            var delta = new long[Day + 1];
            foreach (KeyValuePair<CohortKey, ulong> k in first)
            {
                ulong from = Math.Max(k.Value, dayStart);
                ulong to = Math.Min(std[k.Key.Flight.Value], dayStart + Day);
                if (from >= to)
                {
                    continue;
                }

                long per = k.Key.PaxProfile.Equals(Business) ? perBusiness : perLeisure;
                delta[from - dayStart] += per;
                delta[to - dayStart] -= per;
            }

            var ceiling = new long[Day];
            long running = 0;
            for (int t = 0; t < (int)Day; t++)
            {
                running += delta[t];
                ceiling[t] = running;
            }

            return ceiling;
        }

        private long PerKey(Fx walkSpeed)
        {
            long perKey = 0;
            foreach (NodeId n in Rig.World.Nodes())
            {
                perKey += Corridors.Contains(n.Value) ? Graphs.TraversalTicks(Rig.World.LengthMetres(n), walkSpeed) : 1;
            }

            return perKey;
        }
    }
}
