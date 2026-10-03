using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    // Shared data for the T-021 suite, written from 12-interfaces-airside.md,
    // 11-interfaces-schedule.md, 09 §9.7/§9.7a, 10-events.md and
    // 08-interfaces-core.md only.

    internal static class AirConst
    {
        // 08 §8.1, 11 §11.2 and 12 §12.2, as literals: the spec names no class
        // for the sim.schedule or sim.airside constants, so the tests do not
        // reference one.
        public const ulong TicksPerDay = 14400UL;
        public const ulong TicksPerHour = 600UL;
        public const ulong TicksPerMinute = 10UL;
        public const ulong CruiseLead = 1200UL;
        public const ulong TickUnscheduled = ulong.MaxValue;
        public const ulong DayStride = 100000UL;
        public const ushort ScheduleSystemId = 2;
        public const ushort AirsideSystemId = 3;
        public const ushort FlowSystemId = 4;
        public const ushort TurnaroundSystemId = 5;
        public const ushort RecorderSystemId = 7;
        public const int StandWaitCapacity = 1024;
        public const int PendingCapacity = 2048;

        /// <summary>
        /// The suite's DoorsOpenDelayMinutes (12 §12.4: "Test fixtures carry
        /// their own values beside their tests"). It equals the owner's
        /// playtest value, but is never read from data/.
        /// </summary>
        public const uint FixtureDoorDelayMinutes = 2U;

        public const ulong FixtureDoorDelayTicks = FixtureDoorDelayMinutes * TicksPerMinute;

        /// <summary>minSeparationTicks = ceil(TICKS_PER_SIM_HOUR / DeclaredCapacityPerHour), 12 §12.5.</summary>
        public static ulong MinSeparation(int capacityPerHour)
        {
            ulong c = (ulong)capacityPerHour;
            return (TicksPerHour + c - 1UL) / c;
        }

        /// <summary>A sched_hhmm on day 0 as a tick (08 §8.2 TickOfDayTime).</summary>
        public static ulong At(int hh, int mm)
        {
            return (ulong)((hh * 60) + mm) * TicksPerMinute;
        }
    }

    /// <summary>
    /// Test-owned content (08 §8.11a lets tests build definitions directly).
    /// The aircraft ids are the ones tests/fixtures/schedule/phase0-200.csv
    /// uses. The size categories and their ordinals are fixture sizing for
    /// 12 §12.7's compatibility rule, not balance.
    /// </summary>
    internal static class AirsideContent
    {
        public const string Small = "small";
        public const string Medium = "medium";
        public const string Heavy = "heavy";
        public const string Super = "super";

        public static readonly (string Id, int Ordinal)[] Sizes =
        {
            (Small, 1), (Medium, 2), (Heavy, 3), (Super, 4),
        };

        public static readonly (string Id, string Size)[] Aircraft =
        {
            ("a320", Medium), ("a321", Medium), ("a359", Heavy), ("a388", Super), ("atr72", Small),
            ("b738", Medium), ("b744", Heavy), ("b789", Heavy), ("crj900", Small),
        };

        // (minutes_before_std, share_permille): valid 11 §11.6 curves, so
        // sim.schedule resolves the fixture's profiles.
        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

        public static int SizeOrdinal(string size)
        {
            foreach ((string id, int ordinal) in Sizes)
            {
                if (string.Equals(id, size, StringComparison.Ordinal))
                {
                    return ordinal;
                }
            }

            throw new ArgumentException("unknown size " + size);
        }

        public static string SizeOf(string aircraft)
        {
            foreach ((string id, string size) in Aircraft)
            {
                if (string.Equals(id, aircraft, StringComparison.Ordinal))
                {
                    return size;
                }
            }

            throw new ArgumentException("unknown aircraft " + aircraft);
        }

        /// <summary>12 §12.7: size ordinal of the aircraft &lt;= the stand's maximum.</summary>
        public static bool Fits(string aircraft, string maxSize)
        {
            return SizeOrdinal(SizeOf(aircraft)) <= SizeOrdinal(maxSize);
        }

        public static IContentIndex Index()
        {
            var defs = new List<IContentDefinition>();
            foreach ((string id, int ordinal) in Sizes)
            {
                defs.Add(new SizeCategoryDefinition(new ContentId(id), ordinal));
            }

            foreach ((string id, string size) in Aircraft)
            {
                defs.Add(new AircraftDefinition(new ContentId(id), new ContentId(size)));
            }

            defs.Add(Profile("business", Business));
            defs.Add(Profile("leisure", Leisure));
            return ContentIndexFactory.Create(defs);
        }

        private static PaxProfileDefinition Profile(string id, (uint Minutes, uint Share)[] curve)
        {
            var buckets = new List<ShowUpBucket>();
            foreach ((uint m, uint s) in curve)
            {
                buckets.Add(new ShowUpBucket(m, s));
            }

            return new PaxProfileDefinition(new ContentId(id), Fx.FromRatio(13, 10), buckets);
        }
    }

    internal static class Csv
    {
        public const string Header =
            "flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node";

        public static byte[] Utf8(string s)
        {
            return new UTF8Encoding(false).GetBytes(s);
        }

        /// <summary>The byte-exact 11 §11.4 header, then each row, each followed by LF.</summary>
        public static byte[] Of(params string[] rows)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            foreach (string r in rows)
            {
                sb.Append(r).Append('\n');
            }

            return Utf8(sb.ToString());
        }

        /// <summary>
        /// One day-0 row with valid defaults. A departure carries 100 pax at
        /// entry node 1; an arrival 0 pax and no entry node (11 §11.4).
        /// </summary>
        public static string Row(
            string flightRef,
            string movement,
            string sched,
            string rotation = "",
            string aircraft = "a320",
            string minTurn = "35",
            string repeat = "0",
            string day = "0")
        {
            bool dep = movement == "D";
            return string.Join(",", new[]
            {
                flightRef, day, repeat, movement, "NVA", aircraft, sched, rotation, minTurn,
                dep ? "100" : "0", "business", dep ? "500" : "0", dep ? "10" : "0", dep ? "1" : "",
            });
        }

        /// <summary>A linked arrival/departure pair (11 §11.4: same day, opposite movement, mutual).</summary>
        public static string[] Pair(string arr, string dep, string sta, string std, string aircraft = "a320", string arrMinTurn = "35", string depMinTurn = "35")
        {
            return new[]
            {
                Row(arr, "A", sta, dep, aircraft, arrMinTurn),
                Row(dep, "D", std, arr, aircraft, depMinTurn),
            };
        }

        /// <summary>
        /// 11 §11.3: FlightId.Value = DayIndex * 100000 + RowOrdinal + 1, where
        /// RowOrdinal indexes the rows sorted by flight_ref, ordinal. Each
        /// row maps to the id of its first occurrence, on its own `day`.
        /// </summary>
        public static Dictionary<string, ulong> Ids(byte[] csv)
        {
            string[] lines = Encoding.UTF8.GetString(csv).Split('\n');
            var rows = new List<(string Ref, ulong Day)>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    string[] f = lines[i].Split(',');
                    rows.Add((f[0], ulong.Parse(f[1], CultureInfo.InvariantCulture)));
                }
            }

            rows.Sort((a, b) => string.CompareOrdinal(a.Ref, b.Ref));
            var ids = new Dictionary<string, ulong>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                ids.Add(rows[i].Ref, (rows[i].Day * AirConst.DayStride) + (ulong)i + 1UL);
            }

            return ids;
        }
    }

    /// <summary>
    /// The repository root is the nearest ancestor of AppContext.BaseDirectory
    /// holding AirportSim.sln (07 "Fixture location", Q-031). A missing root
    /// fails the test.
    /// </summary>
    internal static class Repo
    {
        public static byte[] Read(params string[] relative)
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            var parts = new List<string> { dir! };
            parts.AddRange(relative);
            return File.ReadAllBytes(Path.Combine(parts.ToArray()));
        }
    }

    /// <summary>tests/fixtures/airside/phase1-single-runway.json, the §12.13 fixture in §12.4's file format.</summary>
    internal static class AirsideFixture
    {
        public const string SourceName = "phase1-single-runway.json";

        public static byte[] Bytes()
        {
            return Repo.Read("tests", "fixtures", "airside", SourceName);
        }

        public static AirsideLayout Parse()
        {
            return AirsideFactory.CreateLayoutLoader().Parse(Bytes(), SourceName);
        }
    }

    internal static class ScheduleFixture
    {
        public const string SourceName = "phase0-200.csv";

        private static byte[]? _bytes;

        /// <summary>
        /// tests/fixtures/schedule/phase0-200.csv under the repository root, the
        /// nearest ancestor of AppContext.BaseDirectory holding AirportSim.sln
        /// (07 "Fixture location", Q-031). A missing root fails the test.
        /// </summary>
        public static byte[] Bytes()
        {
            if (_bytes == null)
            {
                _bytes = Repo.Read("tests", "fixtures", "schedule", "phase0-200.csv");
            }

            return (byte[])_bytes.Clone();
        }

        /// <summary>
        /// The max-tier movement load (03 "How a budget is measured": 800 daily
        /// movements): four copies of the Phase 0 fixture's rows, each
        /// flight_ref and rotation_ref suffixed per copy. Fixture sizing only.
        /// </summary>
        public static byte[] MaxTier()
        {
            string[] lines = Encoding.UTF8.GetString(Bytes()).Split('\n');
            var sb = new StringBuilder();
            sb.Append(lines[0]).Append('\n');
            foreach (string suffix in new[] { "a", "b", "c", "d" })
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Length == 0)
                    {
                        continue;
                    }

                    string[] f = lines[i].Split(',');
                    f[0] += suffix;
                    if (f[7].Length > 0)
                    {
                        f[7] += suffix;
                    }

                    sb.Append(string.Join(",", f)).Append('\n');
                }
            }

            return Csv.Utf8(sb.ToString());
        }

        /// <summary>The aircraft_type column of every data row.</summary>
        public static SortedSet<string> AircraftTypes()
        {
            var types = new SortedSet<string>(StringComparer.Ordinal);
            string[] lines = Encoding.UTF8.GetString(Bytes()).Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    types.Add(lines[i].Split(',')[5]);
                }
            }

            return types;
        }
    }

    /// <summary>Builds an AirsideLayout (12 §12.4) from ids and values.</summary>
    internal sealed class LayoutBuilder
    {
        public readonly List<RunwayDef> Runways = new List<RunwayDef>();
        public readonly List<TaxiNodeDef> Nodes = new List<TaxiNodeDef>();
        public readonly List<TaxiEdgeDef> Edges = new List<TaxiEdgeDef>();
        public readonly List<StandDef> Stands = new List<StandDef>();

        public LayoutBuilder Runway(ushort id, ushort threshold, int capacityPerHour, uint occupancyTicks, int directionDeg = 270)
        {
            Runways.Add(new RunwayDef(new RunwayId(id), new TaxiNodeId(threshold), directionDeg, capacityPerHour, occupancyTicks));
            return this;
        }

        public LayoutBuilder Node(ushort id, TaxiNodeKind kind)
        {
            Nodes.Add(new TaxiNodeDef(new TaxiNodeId(id), kind));
            return this;
        }

        public LayoutBuilder Edge(ushort id, ushort from, ushort to, uint ticks, bool bidirectional = true)
        {
            Edges.Add(new TaxiEdgeDef(new TaxiEdgeId(id), new TaxiNodeId(from), new TaxiNodeId(to), ticks, bidirectional));
            return this;
        }

        public LayoutBuilder Stand(ushort id, ushort node, string maxSize, uint sink)
        {
            Stands.Add(new StandDef(new StandId(id), new TaxiNodeId(node), new ContentId(maxSize), new NodeId(sink)));
            return this;
        }

        public AirsideLayout Raw()
        {
            return new AirsideLayout(Runways.ToArray(), Nodes.ToArray(), Edges.ToArray(), Stands.ToArray());
        }

        public AirsideLayout Load()
        {
            return AirsideFactory.CreateLayoutLoader().Load(Raw());
        }
    }

    /// <summary>
    /// The Phase 0/1 airside layout that 12 §12.13 describes, built in code.
    /// tests/fixtures/airside/phase1-single-runway.json holds the same layout
    /// in §12.4's file format, and test_layout_parse_fixture_file_equals_built_layout
    /// keeps the two equal. Rigs use this built copy, so that a Parse bug
    /// fails the Parse tests and nothing else. Every §12.13 requirement is
    /// asserted on it in FixtureLayoutTests.
    ///
    ///   T(1) --E1 30-- J1(2) --E2 20-- S1(node 11)
    ///                   |
    ///                  E3 15
    ///                   |
    ///                 J2(3) --E4 10-- S2(node 12)
    ///                   |---E5 15-- S3(node 13)
    ///                   |---E6 20-- S4(node 14)
    ///
    /// E1 is shared by every threshold-to-stand route, E3 by three of them.
    /// </summary>
    internal static class FixtureLayout
    {
        public const ushort Runway = 1;
        public const ushort Threshold = 1;
        public const ushort J1 = 2;
        public const ushort J2 = 3;
        public const ushort E1 = 1;
        public const ushort E2 = 2;
        public const ushort E3 = 3;
        public const ushort E4 = 4;
        public const ushort E5 = 5;
        public const ushort E6 = 6;
        public const ushort S1 = 1;
        public const ushort S2 = 2;
        public const ushort S3 = 3;
        public const ushort S4 = 4;

        // Fixture sizing (12 §12.4 "Test fixtures carry their own values").
        // 15 per hour is 40 ticks between slots, tighter than the fixture's
        // 06:00 bank of arrivals 3 minutes (30 ticks) apart, so
        // AircraftHeldForRunway fires in a single day (12 §12.13).
        public const int CapacityPerHour = 15;
        public const uint OccupancyTicks = 10;

        public static ushort StandNode(ushort stand)
        {
            return (ushort)(10 + stand);
        }

        /// <summary>Every stand sinks to the flow fixture's one Sink, node 9 (12 §12.13, Q-095).</summary>
        public static uint Sink(ushort stand)
        {
            return 9U;
        }

        public static string MaxSize(ushort stand)
        {
            switch (stand)
            {
                case S1: return AirsideContent.Medium;
                case S2: return AirsideContent.Super;
                case S3: return AirsideContent.Heavy;
                case S4: return AirsideContent.Super;
                default: throw new ArgumentOutOfRangeException(nameof(stand));
            }
        }

        /// <summary>RouteTicks(threshold, stand), equal both ways: every edge is bidirectional.</summary>
        public static ulong RouteTicks(ushort stand)
        {
            switch (stand)
            {
                case S1: return 30UL + 20UL;
                case S2: return 30UL + 15UL + 10UL;
                case S3: return 30UL + 15UL + 15UL;
                case S4: return 30UL + 15UL + 20UL;
                default: throw new ArgumentOutOfRangeException(nameof(stand));
            }
        }

        public static LayoutBuilder Builder(int capacityPerHour = CapacityPerHour)
        {
            var b = new LayoutBuilder()
                .Runway(Runway, Threshold, capacityPerHour, OccupancyTicks)
                .Node(Threshold, TaxiNodeKind.RunwayThreshold)
                .Node(J1, TaxiNodeKind.Junction)
                .Node(J2, TaxiNodeKind.Junction);
            for (ushort s = S1; s <= S4; s++)
            {
                b.Node(StandNode(s), TaxiNodeKind.StandPosition);
            }

            b.Edge(E1, Threshold, J1, 30)
             .Edge(E2, J1, StandNode(S1), 20)
             .Edge(E3, J1, J2, 15)
             .Edge(E4, J2, StandNode(S2), 10)
             .Edge(E5, J2, StandNode(S3), 15)
             .Edge(E6, J2, StandNode(S4), 20);
            for (ushort s = S1; s <= S4; s++)
            {
                b.Stand(s, StandNode(s), MaxSize(s), Sink(s));
            }

            return b;
        }

        public static AirsideLayout Layout(int capacityPerHour = CapacityPerHour)
        {
            return Builder(capacityPerHour).Load();
        }
    }

    /// <summary>
    /// The max-tier airside of 03 "How a budget is measured": 60 stands and 3
    /// runways. Three thresholds feed a hub; six piers of ten stands hang off
    /// it. Stands 1-3 of each pier take medium aircraft at most, the rest
    /// any. Fixture sizing only.
    /// </summary>
    internal static class MaxTierLayout
    {
        public const int Runways = 3;
        public const int Piers = 6;
        public const int StandsPerPier = 10;
        public const ushort Hub = 100;

        public static AirsideLayout Layout()
        {
            var b = new LayoutBuilder();
            for (ushort r = 1; r <= Runways; r++)
            {
                b.Runway(r, r, 40, 10).Node(r, TaxiNodeKind.RunwayThreshold);
            }

            b.Node(Hub, TaxiNodeKind.Junction);
            for (ushort r = 1; r <= Runways; r++)
            {
                b.Edge(r, r, Hub, 30);
            }

            ushort stand = 1;
            for (ushort p = 1; p <= Piers; p++)
            {
                ushort pier = (ushort)(Hub + p);
                b.Node(pier, TaxiNodeKind.Junction).Edge((ushort)(10 + p), Hub, pier, 20);
                for (ushort j = 1; j <= StandsPerPier; j++)
                {
                    ushort node = (ushort)(200 + ((p - 1) * StandsPerPier) + j);
                    b.Node(node, TaxiNodeKind.StandPosition)
                     .Edge((ushort)(100 + stand), pier, node, (uint)(10 + j))
                     .Stand(stand, node, j <= 3 ? AirsideContent.Medium : AirsideContent.Super, 1000U + stand);
                    stand++;
                }
            }

            return b.Load();
        }
    }

    /// <summary>
    /// A reference least-TraversalTicks search (12 §12.4) over a layout, for
    /// the test oracle. It honours Bidirectional; the tie-break is not needed
    /// for route cost.
    /// </summary>
    internal static class Routes
    {
        public static ulong LeastTicks(AirsideLayout layout, ushort from, ushort to)
        {
            var dist = new Dictionary<ushort, ulong>();
            var done = new HashSet<ushort>();
            foreach (TaxiNodeDef n in layout.Nodes)
            {
                dist[n.Id.Value] = ulong.MaxValue;
            }

            dist[from] = 0UL;
            while (true)
            {
                ushort best = 0;
                ulong bestD = ulong.MaxValue;
                var keys = new List<ushort>(dist.Keys);
                keys.Sort();
                foreach (ushort k in keys)
                {
                    if (!done.Contains(k) && dist[k] < bestD)
                    {
                        best = k;
                        bestD = dist[k];
                    }
                }

                if (bestD == ulong.MaxValue)
                {
                    return ulong.MaxValue;
                }

                if (best == to)
                {
                    return bestD;
                }

                done.Add(best);
                foreach (TaxiEdgeDef e in layout.Edges)
                {
                    if (e.From.Value == best)
                    {
                        Relax(dist, e.To.Value, bestD + e.TraversalTicks);
                    }

                    if (e.Bidirectional && e.To.Value == best)
                    {
                        Relax(dist, e.From.Value, bestD + e.TraversalTicks);
                    }
                }
            }
        }

        /// <summary>Every edge id on some least-ticks threshold-to-stand route, counted per route.</summary>
        public static Dictionary<ushort, int> EdgeUse(AirsideLayout layout, ushort threshold)
        {
            var use = new Dictionary<ushort, int>();
            foreach (StandDef s in layout.Stands)
            {
                ulong total = LeastTicks(layout, threshold, s.Node.Value);
                foreach (TaxiEdgeDef e in layout.Edges)
                {
                    // An edge lies on a least route iff it splits the route exactly.
                    bool on = OnLeast(layout, threshold, s.Node.Value, total, e.From.Value, e.To.Value, e.TraversalTicks)
                        || (e.Bidirectional && OnLeast(layout, threshold, s.Node.Value, total, e.To.Value, e.From.Value, e.TraversalTicks));
                    if (on)
                    {
                        use[e.Id.Value] = use.TryGetValue(e.Id.Value, out int c) ? c + 1 : 1;
                    }
                }
            }

            return use;
        }

        private static bool OnLeast(AirsideLayout layout, ushort from, ushort to, ulong total, ushort a, ushort b, uint ticks)
        {
            ulong x = LeastTicks(layout, from, a);
            ulong y = LeastTicks(layout, b, to);
            return x != ulong.MaxValue && y != ulong.MaxValue && x + ticks + y == total;
        }

        private static void Relax(Dictionary<ushort, ulong> dist, ushort node, ulong d)
        {
            if (dist.TryGetValue(node, out ulong cur) && d < cur)
            {
                dist[node] = d;
            }
        }
    }

    internal static class Payload
    {
        /// <summary>08 §8.7: FlightId.Value as uint64 then StandId.Value as uint16, little-endian, no padding.</summary>
        public static byte[] Reassign(ulong flight, ushort stand)
        {
            var b = new byte[10];
            for (int i = 0; i < 8; i++)
            {
                b[i] = (byte)(flight >> (8 * i));
            }

            b[8] = (byte)stand;
            b[9] = (byte)(stand >> 8);
            return b;
        }

        public static Command ReassignCommand(ulong tick, ulong flight, ushort stand)
        {
            return new Command(tick, SimConstants.PLAYER_LOCAL, CommandKind.ReassignStand, Reassign(flight, stand));
        }
    }

    internal static class Show
    {
        public static string Opt<T>(T? v, Func<T, ulong> value) where T : struct
        {
            return v.HasValue ? value(v.Value).ToString(CultureInfo.InvariantCulture) : "-";
        }

        public static string Track(in AircraftTrack t)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "f={0} kind={1} phase={2} at={3} edge={4} prog={5} stand={6} rwy={7} entered={8} due={9} holdSince={10} recorded={11} openHold={12} plannedOnStand={13}",
                t.Flight.Value,
                t.Kind,
                t.Phase,
                Opt(t.AtNode, x => x.Value),
                Opt(t.OnEdge, x => x.Value),
                t.EdgeProgress.Raw,
                Opt(t.Stand, x => x.Value),
                Opt(t.Runway, x => x.Value),
                t.PhaseEnteredAt,
                t.DueAt,
                t.PassengerHoldSince,
                Cause(t.RecordedCause),
                Cause(t.OpenHold),
                t.PlannedOnStand);
        }

        public static string Cause(in EventRef r)
        {
            return r.HasValue ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", r.Id.Tick, r.Id.Sequence) : "-";
        }

        public static string Ids(IReadOnlyList<StandId> ids)
        {
            var parts = new List<string>();
            foreach (StandId s in ids)
            {
                parts.Add(s.Value.ToString(CultureInfo.InvariantCulture));
            }

            return "[" + string.Join(",", parts) + "]";
        }
    }
}
