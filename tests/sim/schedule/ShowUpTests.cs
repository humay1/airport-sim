using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.6: PaxCount split across the show-up buckets, then across the
    /// four (HasHoldBaggage, RequiresAssistance) classes, both by largest
    /// remainder with ties by ascending index; injection tick clamped to 0;
    /// Inject once per due (FlightId, bucketIndex, classIndex), in that order.
    /// </summary>
    public sealed class ShowUpTests
    {
        private const ulong Std = 7200UL; // 12:00 on day 0

        private static readonly (string Profile, int Pax, int[] Buckets)[] BucketVectors =
        {
            ("business", 1, new[] { 0, 0, 1, 0, 0, 0 }),
            ("business", 2, new[] { 0, 0, 1, 1, 0, 0 }),
            ("business", 3, new[] { 0, 0, 1, 1, 1, 0 }),
            ("business", 4, new[] { 0, 1, 1, 1, 1, 0 }),
            ("business", 7, new[] { 0, 1, 2, 2, 2, 0 }),
            ("leisure", 1, new[] { 0, 0, 1, 0, 0, 0 }),
            ("leisure", 2, new[] { 0, 1, 1, 0, 0, 0 }),
            ("leisure", 3, new[] { 0, 1, 1, 1, 0, 0 }),
            ("leisure", 10, new[] { 1, 3, 3, 2, 1, 0 }),
            ("leisure", 4, new[] { 0, 1, 1, 1, 1, 0 }),
            ("business", 1000, new[] { 50, 150, 300, 250, 200, 50 }),
        };

        // (hold, assist, pax) -> counts per class index (hasBag ? 2 : 0) + (assist ? 1 : 0).
        private static readonly (int Hold, int Assist, int Pax, int[] Classes)[] ClassVectors =
        {
            (333, 100, 10, new[] { 6, 1, 3, 0 }),
            (500, 500, 1, new[] { 1, 0, 0, 0 }),
            (500, 500, 3, new[] { 1, 1, 1, 0 }),
            (1000, 0, 7, new[] { 0, 0, 7, 0 }),
            (0, 1000, 5, new[] { 0, 5, 0, 0 }),
            (1000, 1000, 4, new[] { 0, 0, 0, 4 }),
            (250, 750, 9, new[] { 2, 5, 0, 2 }),
        };

        private static byte[] BucketCsv()
        {
            var rows = new List<string>();
            for (int i = 0; i < BucketVectors.Length; i++)
            {
                rows.Add(Csv.Row(
                    "P" + (i + 1).ToString("D2", CultureInfo.InvariantCulture),
                    "D",
                    "12:00",
                    pax: BucketVectors[i].Pax.ToString(CultureInfo.InvariantCulture),
                    profile: BucketVectors[i].Profile,
                    hold: "0",
                    assist: "0"));
            }

            return Csv.Of(rows.ToArray());
        }

        private static ulong DueOf(string profile, int bucket)
        {
            return Std - (ScheduleContent.Curves[profile][bucket].Minutes * SchedConst.TicksPerMinute);
        }

        [Fact]
        public void test_show_up_vectors_agree_with_the_oracle()
        {
            foreach ((string profile, int pax, int[] buckets) in BucketVectors)
            {
                (uint Minutes, uint Share)[] curve = ScheduleContent.Curves[profile];
                var w = new long[curve.Length];
                for (int i = 0; i < w.Length; i++)
                {
                    w[i] = curve[i].Share;
                }

                Assert.Equal(buckets, ScheduleOracle.LargestRemainder(pax, w, 1000L));
            }

            foreach ((int h, int a, int pax, int[] classes) in ClassVectors)
            {
                long[] w = { (1000L - h) * (1000L - a), (1000L - h) * a, h * (1000L - a), (long)h * a };
                Assert.Equal(classes, ScheduleOracle.LargestRemainder(pax, w, 1000000L));
            }
        }

        [Fact]
        public void test_show_up_bucket_split_uses_largest_remainder_ties_by_index()
        {
            var rig = new HostRig(BucketCsv(), withFlow: true, record: false);
            rig.RunTo(Std);
            for (int i = 0; i < BucketVectors.Length; i++)
            {
                ulong flight = (ulong)i + 1UL;
                (string profile, int pax, int[] buckets) = BucketVectors[i];
                var got = new SortedDictionary<ulong, int>();
                foreach (Injection inj in rig.Flow!.Injections)
                {
                    if (inj.Key.Flight.Value == flight)
                    {
                        got.TryGetValue(inj.Tick, out int n);
                        got[inj.Tick] = n + inj.Count;
                    }
                }

                var expected = new SortedDictionary<ulong, int>();
                for (int b = 0; b < buckets.Length; b++)
                {
                    if (buckets[b] > 0)
                    {
                        expected[DueOf(profile, b)] = buckets[b];
                    }
                }

                Assert.Equal(expected, got);
            }
        }

        [Fact]
        public void test_show_up_pending_count_drains_by_bucket_without_flow()
        {
            var rig = new HostRig(BucketCsv(), record: false);
            rig.RunTo(1);
            for (int i = 0; i < BucketVectors.Length; i++)
            {
                Assert.Equal(BucketVectors[i].Pax, rig.Schedule.PendingInjectionCount(new FlightId((ulong)i + 1UL)));
            }

            var dues = new SortedSet<ulong>();
            foreach ((string profile, int _, int[] buckets) in BucketVectors)
            {
                for (int b = 0; b < buckets.Length; b++)
                {
                    dues.Add(DueOf(profile, b));
                }
            }

            foreach (ulong due in dues)
            {
                rig.RunTo(due);
                var before = new int[BucketVectors.Length];
                for (int i = 0; i < before.Length; i++)
                {
                    before[i] = rig.Schedule.PendingInjectionCount(new FlightId((ulong)i + 1UL));
                }

                rig.RunTo(due + 1UL);
                for (int i = 0; i < before.Length; i++)
                {
                    (string profile, int _, int[] buckets) = BucketVectors[i];
                    int expectedDrop = 0;
                    for (int b = 0; b < buckets.Length; b++)
                    {
                        if (DueOf(profile, b) == due)
                        {
                            expectedDrop += buckets[b];
                        }
                    }

                    int after = rig.Schedule.PendingInjectionCount(new FlightId((ulong)i + 1UL));
                    Assert.True(before[i] - after == expectedDrop, "flight " + (i + 1) + " at tick " + due + ": dropped " + (before[i] - after) + ", expected " + expectedDrop);
                }
            }

            for (int i = 0; i < BucketVectors.Length; i++)
            {
                Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId((ulong)i + 1UL)));
            }
        }

        [Fact]
        public void test_show_up_class_split_uses_largest_remainder_ties_by_index()
        {
            var rows = new List<string>();
            for (int i = 0; i < ClassVectors.Length; i++)
            {
                rows.Add(Csv.Row(
                    "Q" + (i + 1).ToString(CultureInfo.InvariantCulture),
                    "D",
                    "12:00",
                    pax: ClassVectors[i].Pax.ToString(CultureInfo.InvariantCulture),
                    profile: "single",
                    hold: ClassVectors[i].Hold.ToString(CultureInfo.InvariantCulture),
                    assist: ClassVectors[i].Assist.ToString(CultureInfo.InvariantCulture),
                    entry: (10 + i).ToString(CultureInfo.InvariantCulture)));
            }

            var rig = new HostRig(Csv.Of(rows.ToArray()), withFlow: true, record: false);
            rig.RunTo(Std);
            var expected = new List<string>();
            for (int i = 0; i < ClassVectors.Length; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    if (ClassVectors[i].Classes[c] > 0)
                    {
                        expected.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "t={0} f={1} dir={2} prof=single class={3} n={4} at={5}",
                            Std - 600UL, i + 1, FlowDirection.Departing, c, ClassVectors[i].Classes[c], 10 + i));
                    }
                }
            }

            Assert.Equal(expected, rig.Flow!.Injections.ConvertAll(x => x.ToString()));
            foreach (Injection inj in rig.Flow.Injections)
            {
                Assert.Equal(inj.ClassIndex >= 2, inj.Key.HasHoldBaggage);
                Assert.Equal(inj.ClassIndex % 2 == 1, inj.Key.RequiresAssistance);
            }
        }

        [Fact]
        public void test_show_up_injections_follow_flight_bucket_class_order()
        {
            var rig = new HostRig(Fixture.Bytes(), withFlow: true, record: false);
            ulong end = 2UL * SchedConst.TicksPerDay;
            rig.RunTo(end);
            var oracle = new ScheduleOracle(Fixture.Text(), 3);
            var due = new List<OracleInjection>();
            foreach (OracleInjection inj in oracle.Injections)
            {
                if (inj.Due < end)
                {
                    due.Add(inj);
                }
            }

            // Stable sort by due tick keeps the (FlightId, bucket, class) order within a tick.
            var ordered = new List<(OracleInjection Inj, int Index)>();
            for (int i = 0; i < due.Count; i++)
            {
                ordered.Add((due[i], i));
            }

            ordered.Sort((x, y) => x.Inj.Due != y.Inj.Due ? x.Inj.Due.CompareTo(y.Inj.Due) : x.Index.CompareTo(y.Index));
            var expected = new List<string>();
            foreach ((OracleInjection inj, int _) in ordered)
            {
                OracleFlight f = oracle.ById(inj.Flight);
                expected.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "t={0} f={1} dir={2} prof={3} class={4} n={5} at={6}",
                    inj.Due, inj.Flight, FlowDirection.Departing, f.Row.Profile, inj.Class, inj.Count, f.Row.Entry));
            }

            List<string> actual = rig.Flow!.Injections.ConvertAll(x => x.ToString());
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.True(expected[i] == actual[i], "injection " + i + ": expected " + expected[i] + ", got " + actual[i]);
            }
        }

        [Fact]
        public void test_show_up_split_conserves_head_count()
        {
            var rig = new HostRig(Fixture.Bytes(), withFlow: true, record: false);
            rig.RunTo(2UL * SchedConst.TicksPerDay);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            var injected = new Dictionary<ulong, long>();
            foreach (Injection inj in rig.Flow!.Injections)
            {
                injected.TryGetValue(inj.Key.Flight.Value, out long n);
                injected[inj.Key.Flight.Value] = n + inj.Count;
            }

            long totalPax = 0;
            long totalInjected = 0;
            foreach (OracleFlight f in oracle.Flights)
            {
                FlightRecord r = rig.Flight(f.Id);
                injected.TryGetValue(f.Id, out long n);
                Assert.True(r.PaxCount == n, "flight " + f.Id + ": pax " + r.PaxCount + ", injected " + n);
                Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(f.Id)));
                totalPax += r.PaxCount;
                totalInjected += n;
            }

            Assert.True(totalPax > 0);
            Assert.Equal(totalPax, totalInjected);
            Assert.Equal(0, rig.Flow.BadCalls);

            // Everything injected so far belongs to a flight that is published and carries that demand.
            long all = 0;
            foreach (Injection inj in rig.Flow.Injections)
            {
                all += inj.Count;
            }

            Assert.Equal(all, rig.Flow.InjectedTotal);
        }

        [Fact]
        public void test_show_up_split_conserves_head_count_on_random_rows()
        {
            // Property test (07 L4): random pax, shares, profiles and times, seed literal.
            const ulong seed = 0x0008_5EEDUL;
            var rng = new SplitMix64(seed);
            string[] profiles = { "business", "leisure", "single" };
            var rows = new List<string>();
            var pax = new List<int>();
            for (int i = 0; i < 300; i++)
            {
                int p = (int)(rng.Next() % 600UL);
                int minute = (int)(rng.Next() % 1440UL);
                rows.Add(Csv.Row(
                    "R" + i.ToString("D3", CultureInfo.InvariantCulture),
                    "D",
                    (minute / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("D2", CultureInfo.InvariantCulture),
                    pax: p.ToString(CultureInfo.InvariantCulture),
                    profile: profiles[rng.Next() % 3UL],
                    hold: (rng.Next() % 1001UL).ToString(CultureInfo.InvariantCulture),
                    assist: (rng.Next() % 1001UL).ToString(CultureInfo.InvariantCulture)));
                pax.Add(p);
            }

            var rig = new HostRig(Csv.Of(rows.ToArray()), withFlow: true, record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            var injected = new long[rows.Count];
            foreach (Injection inj in rig.Flow!.Injections)
            {
                injected[(int)inj.Key.Flight.Value - 1] += inj.Count;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                Assert.True(injected[i] == pax[i], "seed " + seed + " iteration " + i + ": pax " + pax[i] + ", injected " + injected[i]);
                Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId((ulong)i + 1UL)));
            }

            Assert.Equal(0, rig.Flow.BadCalls);
        }

        [Fact]
        public void test_show_up_clamps_early_buckets_to_tick_zero()
        {
            var oracle = new ScheduleOracle(Fixture.Text(), 1);
            var withFlow = new HostRig(Fixture.Bytes(), withFlow: true, record: false);
            var noFlow = new HostRig(Fixture.Bytes(), record: false);
            withFlow.RunTo(1);
            noFlow.RunTo(1);
            foreach (string flightRef in new[] { "NVA201", "BRW100" })
            {
                OracleFlight f = oracle.ByRef(flightRef, 0);
                int atZero = 0;
                foreach (OracleInjection inj in ScheduleOracle.InjectionsOf(f))
                {
                    atZero += inj.Due == 0UL ? inj.Count : 0;
                }

                Assert.True(atZero > 0, flightRef + " must have buckets clamped to tick 0");
                int injectedAtZero = 0;
                foreach (Injection inj in withFlow.Flow!.Injections)
                {
                    Assert.Equal(0UL, inj.Tick);
                    injectedAtZero += inj.Key.Flight.Value == f.Id ? inj.Count : 0;
                }

                Assert.Equal(atZero, injectedAtZero);
                Assert.Equal(f.Row.Pax - atZero, withFlow.Schedule.PendingInjectionCount(new FlightId(f.Id)));
                Assert.Equal(f.Row.Pax - atZero, noFlow.Schedule.PendingInjectionCount(new FlightId(f.Id)));
            }
        }

        [Fact]
        public void test_show_up_injects_nothing_for_zero_count_classes_or_arrivals()
        {
            var rig = new HostRig(Fixture.Bytes(), withFlow: true, record: false);
            rig.RunTo(SchedConst.TicksPerDay);
            Assert.NotEmpty(rig.Flow!.Injections);
            foreach (Injection inj in rig.Flow.Injections)
            {
                Assert.True(inj.Count > 0, inj.ToString());
                Assert.Equal(FlowDirection.Departing, inj.Key.Direction);
                Assert.Equal(MovementKind.Departure, rig.Flight(inj.Key.Flight.Value).Kind);
            }

            for (ulong id = 1; id <= 200; id++)
            {
                if (rig.Flight(id).Kind == MovementKind.Arrival)
                {
                    Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(id)));
                }
            }
        }

        [Fact]
        public void test_show_up_pending_count_before_publication_is_zero()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(1)));
            rig.RunTo(1);
            var oracle = new ScheduleOracle(Fixture.Text(), 2);
            foreach (OracleFlight f in oracle.Flights)
            {
                Assert.Equal(oracle.PendingAfter(f, 0UL), rig.Schedule.PendingInjectionCount(new FlightId(f.Id)));
            }
        }

        [Fact]
        public void test_show_up_pending_counts_track_the_oracle_through_two_days()
        {
            var rig = new HostRig(Fixture.Bytes(), record: false);
            var oracle = new ScheduleOracle(Fixture.Text(), 3);
            var sb = new StringBuilder();
            for (ulong t = 0; t < 2UL * SchedConst.TicksPerDay; t += 37UL)
            {
                rig.RunTo(t + 1UL);
                foreach (OracleFlight f in oracle.Flights)
                {
                    if (f.Day > 2)
                    {
                        continue;
                    }

                    int expected = oracle.PendingAfter(f, t);
                    int actual = rig.Schedule.PendingInjectionCount(new FlightId(f.Id));
                    if (expected != actual && sb.Length < 2000)
                    {
                        sb.Append("t=").Append(t).Append(" f=").Append(f.Id).Append(" expected ").Append(expected).Append(" got ").Append(actual).Append('\n');
                    }
                }
            }

            Assert.True(sb.Length == 0, sb.ToString());
        }
    }
}
