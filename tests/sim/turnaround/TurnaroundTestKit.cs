using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    // Shared data for the T-022 suite, written from 13-interfaces-turnaround.md,
    // 12-interfaces-airside.md §12.3/§12.8, 11-interfaces-schedule.md,
    // 10-events.md and 08-interfaces-core.md only.

    internal static class TConst
    {
        // 08 §8.1, 11 §11.2, 12 §12.2 and 13 §13.2, as literals: the spec names
        // no class for the sim.schedule or sim.turnaround constants.
        public const ulong TicksPerDay = 14400UL;
        public const ulong TicksPerMinute = 10UL;
        public const ulong TickUnscheduled = ulong.MaxValue;
        public const ulong DayStride = 100000UL;
        public const int JobKindBits = 8;
        public const ushort ScheduleSystemId = 2;
        public const ushort AirsideSystemId = 3;
        public const ushort TurnaroundSystemId = 5;
        public const ushort RecorderSystemId = 7;

        /// <summary>A sched_hhmm on day 0 as a tick (08 §8.2).</summary>
        public static ulong At(int hh, int mm)
        {
            return (ulong)((hh * 60) + mm) * TicksPerMinute;
        }

        /// <summary>13 §13.3: JobId.Value = (Flight.Value &lt;&lt; JOB_KIND_BITS) | (uint64)(int)Kind.</summary>
        public static JobId Job(ulong flight, JobKind kind)
        {
            return new JobId((flight << JobKindBits) | (ulong)(uint)kind);
        }

        public static readonly JobKind[] ArrivalJobs = { JobKind.Deboard, JobKind.BaggageUnload };

        public static readonly JobKind[] DepartureJobs =
        {
            JobKind.CabinClean, JobKind.Catering, JobKind.Fuel, JobKind.BaggageLoad, JobKind.PushbackPrep, JobKind.Boarding,
        };

        /// <summary>13 §13.6: the five jobs Boarding waits on.</summary>
        public static readonly JobKind[] BoardingPrerequisites =
        {
            JobKind.CabinClean, JobKind.Catering, JobKind.Fuel, JobKind.BaggageLoad, JobKind.PushbackPrep,
        };
    }

    /// <summary>
    /// Test-owned content (08 §8.11a lets tests build definitions directly),
    /// so sim.schedule resolves the aircraft and pax profiles the fixture
    /// rows name. Fixture sizing, not balance.
    /// </summary>
    internal static class TurnContent
    {
        private static readonly (string Id, int Ordinal)[] Sizes =
        {
            ("small", 1), ("medium", 2), ("heavy", 3), ("super", 4),
        };

        private static readonly (string Id, string Size)[] Aircraft =
        {
            ("a320", "medium"), ("a321", "medium"), ("a359", "heavy"), ("a388", "super"), ("atr72", "small"),
            ("b738", "medium"), ("b744", "heavy"), ("b789", "heavy"), ("crj900", "small"),
        };

        private static readonly (uint Minutes, uint Share)[] Business =
        {
            (30, 50), (45, 150), (60, 300), (75, 250), (90, 200), (120, 50),
        };

        private static readonly (uint Minutes, uint Share)[] Leisure =
        {
            (45, 50), (60, 250), (90, 300), (120, 250), (150, 100), (180, 50),
        };

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

    /// <summary>One schedule row as the tests need it: its FlightId and 11 §11.4 columns.</summary>
    internal readonly struct Movement
    {
        public Movement(ulong flight, bool departure, ulong scheduledTick, ulong minTurnaroundTicks)
        {
            Flight = flight;
            Departure = departure;
            ScheduledTick = scheduledTick;
            MinTurnaroundTicks = minTurnaroundTicks;
        }

        public ulong Flight { get; }

        public bool Departure { get; }

        public ulong ScheduledTick { get; }

        public ulong MinTurnaroundTicks { get; }

        /// <summary>12 §12.3: a departure's planned OnStand is max(0, STD − MinTurnaround).</summary>
        public ulong PlannedDepartureOnStand =>
            ScheduledTick > MinTurnaroundTicks ? ScheduledTick - MinTurnaroundTicks : 0UL;
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
        /// One row with valid defaults, on day 0 unless given. A departure
        /// carries 100 pax at entry node 1; an arrival 0 pax and no entry node
        /// (11 §11.4).
        /// </summary>
        public static string Row(string flightRef, string movement, string sched, string rotation = "", string minTurn = "35", string repeat = "0", string day = "0")
        {
            bool dep = movement == "D";
            return string.Join(",", new[]
            {
                flightRef, day, repeat, movement, "NVA", "a320", sched, rotation, minTurn,
                dep ? "100" : "0", "business", dep ? "500" : "0", dep ? "10" : "0", dep ? "1" : "",
            });
        }

        /// <summary>
        /// 11 §11.3: FlightId.Value = DayIndex * 100000 + RowOrdinal + 1, where
        /// RowOrdinal indexes the rows sorted by flight_ref, ordinal.
        /// </summary>
        public static Dictionary<string, ulong> Ids(byte[] csv)
        {
            var ids = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach ((string r, Movement m) in Rows(csv, 1))
            {
                ids[r] = m.Flight;
            }

            return ids;
        }

        /// <summary>
        /// Every occurrence on days 0 to <paramref name="days"/> − 1: a
        /// repeat_daily row once per day from its own day on, other rows on
        /// their own day only (11 §11.3, §11.9).
        /// </summary>
        public static List<(string Ref, Movement Move)> Rows(byte[] csv, int days)
        {
            string[] lines = Encoding.UTF8.GetString(csv).Split('\n');
            var rows = new List<string[]>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    rows.Add(lines[i].Split(','));
                }
            }

            rows.Sort((a, b) => string.CompareOrdinal(a[0], b[0]));
            var result = new List<(string Ref, Movement Move)>();
            for (int ordinal = 0; ordinal < rows.Count; ordinal++)
            {
                string[] f = rows[ordinal];
                ulong day = ulong.Parse(f[1], CultureInfo.InvariantCulture);
                bool repeat = f[2] == "1";
                string[] hm = f[6].Split(':');
                ulong timeOfDay = ((ulong.Parse(hm[0], CultureInfo.InvariantCulture) * 60UL) + ulong.Parse(hm[1], CultureInfo.InvariantCulture)) * TConst.TicksPerMinute;
                ulong minTurn = ulong.Parse(f[8], CultureInfo.InvariantCulture) * TConst.TicksPerMinute;
                for (ulong d = day; d < (ulong)days; d++)
                {
                    if (d != day && !repeat)
                    {
                        break;
                    }

                    ulong id = (d * TConst.DayStride) + (ulong)ordinal + 1UL;
                    result.Add((f[0], new Movement(id, f[3] == "D", (d * TConst.TicksPerDay) + timeOfDay, minTurn)));
                }
            }

            return result;
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

    internal static class ScheduleFixture
    {
        public const string SourceName = "phase0-200.csv";

        /// <summary>tests/fixtures/schedule/phase0-200.csv (11 §11.10).</summary>
        public static byte[] Bytes()
        {
            return Repo.Read("tests", "fixtures", "schedule", SourceName);
        }

        /// <summary>
        /// The max-tier movement load (03 "How a budget is measured": 800
        /// daily movements): four copies of the Phase 0 fixture's rows, each
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
    }

    /// <summary>Builds catalogues and fleets (13 §13.4) from values.</summary>
    internal static class Setups
    {
        /// <summary>
        /// The suite's unit catalogue: every JobKind once, Deboard and Boarding
        /// with no vehicle (13 §13.4), every duration distinct so that no two
        /// jobs of one flight finish in the same tick when unimpeded.
        /// </summary>
        public static JobDef[] UnitJobs()
        {
            return new[]
            {
                new JobDef(JobKind.Deboard, null, 40U, DelayCategory.GroundHandling),
                new JobDef(JobKind.BaggageUnload, VehicleKind.BaggageTractor, 50U, DelayCategory.Loading),
                new JobDef(JobKind.CabinClean, VehicleKind.CleaningCrew, 60U, DelayCategory.Cleaning),
                new JobDef(JobKind.Catering, VehicleKind.CateringTruck, 70U, DelayCategory.Catering),
                new JobDef(JobKind.Fuel, VehicleKind.FuelTruck, 80U, DelayCategory.Fuel),
                new JobDef(JobKind.BaggageLoad, VehicleKind.BaggageTractor, 55U, DelayCategory.Loading),
                new JobDef(JobKind.PushbackPrep, VehicleKind.PushbackTug, 30U, DelayCategory.GroundHandling),
                new JobDef(JobKind.Boarding, null, 100U, DelayCategory.GroundHandling),
            };
        }

        public static uint Duration(JobDef[] jobs, JobKind kind)
        {
            foreach (JobDef j in jobs)
            {
                if (j.Kind == kind)
                {
                    return j.NominalDurationTicks;
                }
            }

            throw new ArgumentException("no catalogue entry for " + kind);
        }

        public static DelayCategory Category(JobDef[] jobs, JobKind kind)
        {
            foreach (JobDef j in jobs)
            {
                if (j.Kind == kind)
                {
                    return j.Category;
                }
            }

            throw new ArgumentException("no catalogue entry for " + kind);
        }

        public static VehicleKind? Requires(JobDef[] jobs, JobKind kind)
        {
            foreach (JobDef j in jobs)
            {
                if (j.Kind == kind)
                {
                    return j.RequiresVehicle;
                }
            }

            throw new ArgumentException("no catalogue entry for " + kind);
        }

        /// <summary>A fleet from (id, kind) pairs.</summary>
        public static VehicleDef[] Fleet(params (ushort Id, VehicleKind Kind)[] vehicles)
        {
            var defs = new VehicleDef[vehicles.Length];
            for (int i = 0; i < vehicles.Length; i++)
            {
                defs[i] = new VehicleDef(new VehicleId(vehicles[i].Id), vehicles[i].Kind);
            }

            return defs;
        }

        /// <summary>
        /// <paramref name="each"/> vehicles of every VehicleKind except those in
        /// <paramref name="overrides"/>, which get the given count. Ids are
        /// 1-based, consecutive, in ascending VehicleKind then index order.
        /// </summary>
        public static VehicleDef[] Plenty(int each = 4, params (VehicleKind Kind, int Count)[] overrides)
        {
            var defs = new List<VehicleDef>();
            ushort next = 1;
            foreach (VehicleKind k in (VehicleKind[])Enum.GetValues(typeof(VehicleKind)))
            {
                int count = each;
                foreach ((VehicleKind ok, int oc) in overrides)
                {
                    if (ok == k)
                    {
                        count = oc;
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    defs.Add(new VehicleDef(new VehicleId(next++), k));
                }
            }

            return defs.ToArray();
        }

        public static TurnaroundSetup Of(JobDef[] jobs, VehicleDef[] fleet)
        {
            return new TurnaroundSetup(new TurnaroundCatalogue(jobs), new TurnaroundFleet(fleet));
        }

        public static TurnaroundSetup Unit(VehicleDef[] fleet)
        {
            return Of(UnitJobs(), fleet);
        }
    }

    /// <summary>
    /// The 13 §13.11 Phase 0/1 setup (Q-086, Q-088), built in code, and the
    /// same values in tests/fixtures/turnaround/phase1-five-vehicles.json
    /// (13 §13.10a "File format"); test_fixture_file_loads_with_one_vehicle_per_kind
    /// keeps the two equal. Rigs use this built copy, so that a Load bug
    /// fails the loader tests and nothing else.
    ///
    /// - Exactly five vehicles, one of each VehicleKind, ids 1 to 5. The one
    ///   fuel truck serves every Fuel job (120 ticks) and the one tractor
    ///   every BaggageUnload and BaggageLoad, so peak concurrent demand
    ///   exceeds supply over a day of tests/fixtures/schedule/phase0-200.csv
    ///   and TurnaroundJobBlocked fires.
    /// - All eight JobKinds, RequiresVehicle per 13 §13.4's table, durations
    ///   short against the schedule's smallest MinTurnaround (25 min = 250
    ///   ticks): unimpeded, a departure is ready to board 120 ticks after
    ///   OnStand (the fuel truck's 120 is the longest) and boarding completes
    ///   at 220.
    /// </summary>
    internal static class Phase1Fixture
    {
        public const string SourceName = "phase1-five-vehicles.json";
        public const ulong UnimpededReadyToBoardTicks = 120UL;
        public const ulong UnimpededBoardingCompleteTicks = 220UL;

        public static byte[] Bytes()
        {
            return Repo.Read("tests", "fixtures", "turnaround", SourceName);
        }

        public static JobDef[] Jobs()
        {
            return new[]
            {
                new JobDef(JobKind.Deboard, null, 60U, DelayCategory.GroundHandling),
                new JobDef(JobKind.BaggageUnload, VehicleKind.BaggageTractor, 40U, DelayCategory.Loading),
                new JobDef(JobKind.CabinClean, VehicleKind.CleaningCrew, 80U, DelayCategory.Cleaning),
                new JobDef(JobKind.Catering, VehicleKind.CateringTruck, 90U, DelayCategory.Catering),
                new JobDef(JobKind.Fuel, VehicleKind.FuelTruck, 120U, DelayCategory.Fuel),
                new JobDef(JobKind.BaggageLoad, VehicleKind.BaggageTractor, 50U, DelayCategory.Loading),
                new JobDef(JobKind.PushbackPrep, VehicleKind.PushbackTug, 20U, DelayCategory.GroundHandling),
                new JobDef(JobKind.Boarding, null, 100U, DelayCategory.GroundHandling),
            };
        }

        public static VehicleDef[] Fleet()
        {
            return Setups.Fleet(
                (1, VehicleKind.CleaningCrew),
                (2, VehicleKind.CateringTruck),
                (3, VehicleKind.FuelTruck),
                (4, VehicleKind.BaggageTractor),
                (5, VehicleKind.PushbackTug));
        }

        public static TurnaroundSetup Setup()
        {
            return Setups.Of(Jobs(), Fleet());
        }
    }

    /// <summary>13 §13.10a "File format" (Q-086): spellings and a file builder.</summary>
    internal static class SetupFile
    {
        public static string Spell(JobKind k)
        {
            switch (k)
            {
                case JobKind.Deboard: return "deboard";
                case JobKind.BaggageUnload: return "baggage_unload";
                case JobKind.CabinClean: return "cabin_clean";
                case JobKind.Catering: return "catering";
                case JobKind.Fuel: return "fuel";
                case JobKind.BaggageLoad: return "baggage_load";
                case JobKind.PushbackPrep: return "pushback_prep";
                default: return "boarding";
            }
        }

        public static string Spell(VehicleKind k)
        {
            switch (k)
            {
                case VehicleKind.CleaningCrew: return "cleaning_crew";
                case VehicleKind.CateringTruck: return "catering_truck";
                case VehicleKind.FuelTruck: return "fuel_truck";
                case VehicleKind.BaggageTractor: return "baggage_tractor";
                default: return "pushback_tug";
            }
        }

        public static string Spell(DelayCategory c)
        {
            switch (c)
            {
                case DelayCategory.GroundHandling: return "ground_handling";
                case DelayCategory.Fuel: return "fuel";
                case DelayCategory.Catering: return "catering";
                case DelayCategory.Cleaning: return "cleaning";
                case DelayCategory.Loading: return "loading";
                case DelayCategory.Pushback: return "pushback";
                default: throw new ArgumentException("no spelling in this kit for " + c);
            }
        }

        /// <summary>A file in the 13 §13.10a shape, one entry per line, from the given entries.</summary>
        public static byte[] Of(IEnumerable<JobDef> jobs, IEnumerable<VehicleDef> vehicles)
        {
            var j = new List<string>();
            foreach (JobDef d in jobs)
            {
                j.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "    {{ \"kind\": \"{0}\", \"nominal_duration_ticks\": {1}, \"category\": \"{2}\" }}",
                    Spell(d.Kind),
                    d.NominalDurationTicks,
                    Spell(d.Category)));
            }

            var v = new List<string>();
            foreach (VehicleDef d in vehicles)
            {
                v.Add(string.Format(CultureInfo.InvariantCulture, "    {{ \"id\": {0}, \"kind\": \"{1}\" }}", d.Id.Value, Spell(d.Kind)));
            }

            string text = "{\n  \"schema_version\": 1,\n  \"jobs\": [\n" + string.Join(",\n", j) + "\n  ],\n  \"vehicles\": [\n" + string.Join(",\n", v) + "\n  ]\n}\n";
            return Csv.Utf8(text);
        }

        /// <summary>The fixture's jobs and fleet with <paramref name="edit"/> applied to the jobs.</summary>
        public static byte[] FixtureWith(Func<List<JobDef>, List<JobDef>> edit)
        {
            return Of(edit(new List<JobDef>(Phase1Fixture.Jobs())), Phase1Fixture.Fleet());
        }
    }

    internal static class Show
    {
        public static string Id(in EventRef r)
        {
            return r.HasValue ? Id(r.Id) : "-";
        }

        public static string Id(in EventId id)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", id.Tick, id.Sequence);
        }

        public static string Job(in TurnaroundJob j)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "job {0} f={1} {2} {3} v={4} created={5} started={6} due={7}",
                j.Id.Value,
                j.Flight.Value,
                j.Kind,
                j.Status,
                j.Vehicle.HasValue ? j.Vehicle.Value.Value.ToString(CultureInfo.InvariantCulture) : "-",
                j.CreatedAt,
                j.StartedAt == TConst.TickUnscheduled ? "U" : j.StartedAt.ToString(CultureInfo.InvariantCulture),
                j.DueAt == TConst.TickUnscheduled ? "U" : j.DueAt.ToString(CultureInfo.InvariantCulture));
        }
    }
}
