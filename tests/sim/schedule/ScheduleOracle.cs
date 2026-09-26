using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Schedule.Tests
{
    internal sealed class OracleRow
    {
        public string Ref = "";
        public uint Day;
        public bool Repeat;
        public MovementKind Kind;
        public string Airline = "";
        public string Aircraft = "";
        public ulong MinuteOfDay;
        public string RotationRef = "";
        public int MinTurn;
        public int Pax;
        public string Profile = "";
        public int Hold;
        public int Assist;
        public uint Entry;
        public int Ordinal;
    }

    internal sealed class OracleFlight
    {
        public ulong Id;
        public OracleRow Row = new OracleRow();
        public uint Day;
        public ulong Sched;
        public ulong Publish;
        public ulong Rotation;
        public bool HasRotation;
        public ulong SchedArr;
        public ulong SchedDep;

        public string Describe()
        {
            // Same layout as Describe.Record, from the spec's rules.
            return string.Format(
                CultureInfo.InvariantCulture,
                "id={0} airline={1} ac={2} kind={3} day={4} sched={5} pub={6} rot={7} hasRot={8} mt={9} prof={10} pax={11} hold={12} assist={13} entry={14}",
                Id,
                Fnv.Airline32(Row.Airline),
                Row.Aircraft,
                Row.Kind,
                Day,
                Sched,
                Publish,
                Rotation,
                HasRotation,
                Fx.FromInt(Row.MinTurn).Raw,
                Row.Profile,
                Row.Pax,
                Row.Hold,
                Row.Assist,
                Row.Kind == MovementKind.Departure ? Row.Entry.ToString(CultureInfo.InvariantCulture) : "-");
        }
    }

    internal readonly struct OracleInjection
    {
        public OracleInjection(ulong flight, int bucket, int cls, ulong due, int count)
        {
            Flight = flight;
            Bucket = bucket;
            Class = cls;
            Due = due;
            Count = count;
        }

        public ulong Flight { get; }

        public int Bucket { get; }

        public int Class { get; }

        public ulong Due { get; }

        public int Count { get; }
    }

    /// <summary>
    /// An independent model of 11 §11.3–§11.6 and §11.9 over a well-formed
    /// fixture: id derivation, publication ticks, rotation links, the two
    /// largest-remainder splits and the declared hash order. Tests compare
    /// the module against it.
    /// </summary>
    internal sealed class ScheduleOracle
    {
        public readonly List<OracleRow> Rows = new List<OracleRow>();
        public readonly List<OracleFlight> Flights = new List<OracleFlight>();
        public readonly List<OracleInjection> Injections = new List<OracleInjection>();
        private readonly Dictionary<ulong, OracleFlight> _byId = new Dictionary<ulong, OracleFlight>();

        public ScheduleOracle(string csv, uint days)
        {
            string[] lines = csv.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                {
                    continue;
                }

                string[] f = lines[i].Split(',');
                var r = new OracleRow
                {
                    Ref = f[0],
                    Day = uint.Parse(f[1], CultureInfo.InvariantCulture),
                    Repeat = f[2] == "1",
                    Kind = f[3] == "A" ? MovementKind.Arrival : MovementKind.Departure,
                    Airline = f[4],
                    Aircraft = f[5],
                    MinuteOfDay = (ulong.Parse(f[6].Substring(0, 2), CultureInfo.InvariantCulture) * 60UL) + ulong.Parse(f[6].Substring(3, 2), CultureInfo.InvariantCulture),
                    RotationRef = f[7],
                    MinTurn = int.Parse(f[8], CultureInfo.InvariantCulture),
                    Pax = int.Parse(f[9], CultureInfo.InvariantCulture),
                    Profile = f[10],
                    Hold = int.Parse(f[11], CultureInfo.InvariantCulture),
                    Assist = int.Parse(f[12], CultureInfo.InvariantCulture),
                    Entry = f[13].Length == 0 ? 0U : uint.Parse(f[13], CultureInfo.InvariantCulture),
                };
                Rows.Add(r);
            }

            var sorted = new List<OracleRow>(Rows);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Ref, b.Ref));
            var byRef = new Dictionary<string, OracleRow>(StringComparer.Ordinal);
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Ordinal = i;
                byRef[sorted[i].Ref] = sorted[i];
            }

            for (uint d = 0; d < days; d++)
            {
                foreach (OracleRow r in sorted)
                {
                    if (!Occurs(r, d))
                    {
                        continue;
                    }

                    var fl = new OracleFlight
                    {
                        Id = IdOf(d, r),
                        Row = r,
                        Day = d,
                        Sched = (d * SchedConst.TicksPerDay) + (r.MinuteOfDay * SchedConst.TicksPerMinute),
                    };
                    fl.Publish = fl.Sched >= SchedConst.PublishLead ? fl.Sched - SchedConst.PublishLead : 0UL;
                    if (r.RotationRef.Length > 0)
                    {
                        OracleRow other = byRef[r.RotationRef];
                        fl.HasRotation = true;
                        fl.Rotation = IdOf(d, other);
                        ulong otherSched = (d * SchedConst.TicksPerDay) + (other.MinuteOfDay * SchedConst.TicksPerMinute);
                        fl.SchedArr = r.Kind == MovementKind.Arrival ? fl.Sched : otherSched;
                        fl.SchedDep = r.Kind == MovementKind.Departure ? fl.Sched : otherSched;
                    }
                    else
                    {
                        fl.HasRotation = false;
                        fl.Rotation = fl.Id;
                        fl.SchedArr = r.Kind == MovementKind.Arrival ? fl.Sched : SchedConst.TickUnscheduled;
                        fl.SchedDep = r.Kind == MovementKind.Departure ? fl.Sched : SchedConst.TickUnscheduled;
                    }

                    Flights.Add(fl);
                    _byId[fl.Id] = fl;
                }
            }

            Flights.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (OracleFlight fl in Flights)
            {
                Injections.AddRange(InjectionsOf(fl));
            }
        }

        public static ulong IdOf(uint day, OracleRow r)
        {
            return (day * SchedConst.DayStride) + (ulong)r.Ordinal + 1UL;
        }

        private static bool Occurs(OracleRow r, uint day)
        {
            return r.Repeat ? day >= r.Day : day == r.Day;
        }

        public OracleFlight ById(ulong id)
        {
            return _byId[id];
        }

        public OracleFlight ByRef(string flightRef, uint day)
        {
            foreach (OracleFlight f in Flights)
            {
                if (f.Day == day && f.Row.Ref == flightRef)
                {
                    return f;
                }
            }

            throw new ArgumentException("no flight " + flightRef);
        }

        /// <summary>§11.6 steps 1–4, in ascending (bucketIndex, classIndex); zero-count classes omitted.</summary>
        public static List<OracleInjection> InjectionsOf(OracleFlight fl)
        {
            var result = new List<OracleInjection>();
            OracleRow r = fl.Row;
            if (r.Kind != MovementKind.Departure || r.Pax == 0)
            {
                return result;
            }

            (uint Minutes, uint Share)[] curve = ScheduleContent.Curves[r.Profile];
            var shares = new long[curve.Length];
            for (int i = 0; i < curve.Length; i++)
            {
                shares[i] = curve[i].Share;
            }

            int[] buckets = LargestRemainder(r.Pax, shares, 1000L);
            long h = r.Hold;
            long a = r.Assist;
            long[] classWeights =
            {
                (1000L - h) * (1000L - a),
                (1000L - h) * a,
                h * (1000L - a),
                h * a,
            };
            for (int b = 0; b < curve.Length; b++)
            {
                ulong lead = curve[b].Minutes * SchedConst.TicksPerMinute;
                ulong due = fl.Sched >= lead ? fl.Sched - lead : 0UL;
                int[] classes = LargestRemainder(buckets[b], classWeights, 1000000L);
                for (int c = 0; c < 4; c++)
                {
                    if (classes[c] > 0)
                    {
                        result.Add(new OracleInjection(fl.Id, b, c, due, classes[c]));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Largest remainder: floor each exact share, then give the leftover
        /// one at a time to the largest remainders, ties by ascending index.
        /// </summary>
        public static int[] LargestRemainder(int total, long[] weights, long denominator)
        {
            var counts = new int[weights.Length];
            var rem = new long[weights.Length];
            long assigned = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                long exact = total * weights[i];
                counts[i] = (int)(exact / denominator);
                rem[i] = exact % denominator;
                assigned += counts[i];
            }

            long leftover = total - assigned;
            var taken = new bool[weights.Length];
            for (long k = 0; k < leftover; k++)
            {
                int best = -1;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (!taken[i] && (best < 0 || rem[i] > rem[best]))
                    {
                        best = i;
                    }
                }

                taken[best] = true;
                counts[best]++;
            }

            return counts;
        }

        /// <summary>Flights published once ticks 0..lastTick have run (none when lastTick is null).</summary>
        public List<OracleFlight> PublishedAfter(ulong? lastTick)
        {
            var result = new List<OracleFlight>();
            if (lastTick == null)
            {
                return result;
            }

            foreach (OracleFlight f in Flights)
            {
                if (f.Publish <= lastTick.Value)
                {
                    result.Add(f);
                }
            }

            return result;
        }

        /// <summary>Head count still to inject for a flight after ticks 0..lastTick.</summary>
        public int PendingAfter(OracleFlight fl, ulong? lastTick)
        {
            if (lastTick == null || fl.Publish > lastTick.Value)
            {
                return 0;
            }

            int n = 0;
            foreach (OracleInjection inj in InjectionsOf(fl))
            {
                if (inj.Due > lastTick.Value)
                {
                    n += inj.Count;
                }
            }

            return n;
        }

        /// <summary>
        /// The §11.9 hash, in its declared order, after ticks 0..lastTick.
        /// <paramref name="day"/> is item 2; <paramref name="countPrefix"/>
        /// feeds each list's length before its items. Neither is pinned by
        /// §11.9, so callers accept every variant.
        /// </summary>
        public ulong ExpectedHash(ulong fixtureHash, ulong? lastTick, ulong day, bool countPrefix)
        {
            var h = new FnvWords();
            h.U64(fixtureHash);
            h.U64(day);
            List<OracleFlight> published = PublishedAfter(lastTick);
            if (countPrefix)
            {
                h.U64((ulong)published.Count);
            }

            foreach (OracleFlight f in published)
            {
                h.U64(f.Id);
                h.U64(f.Publish);
                h.U64(f.Sched);
            }

            var pending = new List<OracleInjection>();
            foreach (OracleFlight f in published)
            {
                foreach (OracleInjection inj in InjectionsOf(f))
                {
                    if (inj.Due > lastTick!.Value)
                    {
                        pending.Add(inj);
                    }
                }
            }

            if (countPrefix)
            {
                h.U64((ulong)pending.Count);
            }

            foreach (OracleInjection inj in pending)
            {
                h.U64(inj.Due);
                h.I64(inj.Count);
                h.I64(inj.Class);
            }

            return h.Result;
        }
    }
}
