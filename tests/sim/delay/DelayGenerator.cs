using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Delay.Tests
{
    /// <summary>One movement as the generator needs it.</summary>
    internal readonly struct Move
    {
        public Move(ulong flight, bool departure, ulong rotation, ulong scheduledTick, ulong minTurnaroundTicks)
        {
            Flight = flight;
            Departure = departure;
            Rotation = rotation;
            ScheduledTick = scheduledTick;
            MinTurnaroundTicks = minTurnaroundTicks;
        }

        public ulong Flight { get; }

        public bool Departure { get; }

        /// <summary>The linked counterpart's FlightId, 0 if none.</summary>
        public ulong Rotation { get; }

        /// <summary>STA for an arrival, STD for a departure (11 §11.2).</summary>
        public ulong ScheduledTick { get; }

        public ulong MinTurnaroundTicks { get; }

        /// <summary>11 §11.5: PublishTick = ScheduledTick − PLAN_PUBLISH_LEAD_TICKS, clamped to 0.</summary>
        public ulong PublishTick => ScheduledTick > DConst.PlanPublishLead ? ScheduledTick - DConst.PlanPublishLead : 0UL;
    }

    /// <summary>tests/fixtures/schedule/phase0-200.csv (11 §11.10) as movements, read with 11 §11.3/§11.4's rules.</summary>
    internal static class ScheduleFixture
    {
        public static byte[] Bytes()
        {
            return Repo.Read("tests", "fixtures", "schedule", "phase0-200.csv");
        }

        /// <summary>
        /// The max-tier movement load (03 "How a budget is measured": 800 daily
        /// movements): four copies of the fixture's rows, flight_ref and
        /// rotation_ref suffixed per copy. Fixture sizing only.
        /// </summary>
        public static byte[] MaxTier()
        {
            string[] lines = Encoding.UTF8.GetString(Bytes()).Split('\n');
            var sb = new StringBuilder();
            sb.Append(lines[0].TrimEnd('\r')).Append('\n');
            foreach (string suffix in new[] { "a", "b", "c", "d" })
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    string[] f = line.Split(',');
                    f[0] += suffix;
                    if (f[7].Length > 0)
                    {
                        f[7] += suffix;
                    }

                    sb.Append(string.Join(",", f)).Append('\n');
                }
            }

            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        /// <summary>
        /// Every occurrence on day <paramref name="day"/>: FlightId.Value = DayIndex ×
        /// 100000 + RowOrdinal + 1 over rows sorted by flight_ref (11 §11.3), a
        /// repeat_daily row on every day from its own, rotations resolved within
        /// the day (11 §11.5).
        /// </summary>
        public static List<Move> Day(byte[] csv, int day)
        {
            string[] lines = Encoding.UTF8.GetString(csv).Split('\n');
            var rows = new List<string[]>();
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length > 0)
                {
                    rows.Add(line.Split(','));
                }
            }

            rows.Sort((a, b) => string.CompareOrdinal(a[0], b[0]));
            var ordinalOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                ordinalOf[rows[i][0]] = i;
            }

            var result = new List<Move>();
            ulong d = (ulong)day;
            for (int ordinal = 0; ordinal < rows.Count; ordinal++)
            {
                string[] f = rows[ordinal];
                ulong rowDay = ulong.Parse(f[1], CultureInfo.InvariantCulture);
                bool repeat = f[2] == "1";
                if (d < rowDay || (d != rowDay && !repeat))
                {
                    continue;
                }

                string[] hm = f[6].Split(':');
                ulong timeOfDay = ((ulong.Parse(hm[0], CultureInfo.InvariantCulture) * 60UL) + ulong.Parse(hm[1], CultureInfo.InvariantCulture)) * DConst.TicksPerMinute;
                ulong minTurn = ulong.Parse(f[8], CultureInfo.InvariantCulture) * DConst.TicksPerMinute;
                ulong rotation = f[7].Length > 0 ? (d * 100000UL) + (ulong)ordinalOf[f[7]] + 1UL : 0UL;
                result.Add(new Move((d * 100000UL) + (ulong)ordinal + 1UL, f[3] == "D", rotation, (d * DConst.TicksPerDay) + timeOfDay, minTurn));
            }

            return result;
        }
    }

    /// <summary>
    /// The seeded generator of well-formed event streams 14 §14.14 asks for.
    /// Every rule of 10 §10.3 and 14 §14.4–§14.5 is respected: publication
    /// first (at 11 §11.5's PublishTick), checkpoints in order with
    /// schedule-anchored PlannedTicks, early and late checkpoints (so both the
    /// cap and recovery happen), paired intervals of every family with
    /// overlaps and intervals spanning a checkpoint, job-dependency waits,
    /// intervals and missed passengers after finalisation, rotation pairs and
    /// rotation-less flights, departures that never finalise, and day
    /// boundaries crossed. A Stand interval's close carries its opener's Stand
    /// value, so every reading of 14 §14.5's key pairs them.
    /// </summary>
    internal static class Generator
    {
        private const ulong ArrivalTaxiIn = 60UL;
        private const ulong DepartureTaxiOut = 80UL;
        private const ushort SpanningEdge = 99;
        private const ushort SpanningStand = 7;

        private static readonly (JobKind Kind, DelayCategory Category)[] DepartureJobs =
        {
            (JobKind.CabinClean, DelayCategory.Cleaning),
            (JobKind.Catering, DelayCategory.Catering),
            (JobKind.Fuel, DelayCategory.Fuel),
            (JobKind.BaggageLoad, DelayCategory.Loading),
            (JobKind.PushbackPrep, DelayCategory.GroundHandling),
        };

        /// <summary>
        /// A synthetic day for the long property runs: 8 to 16 rotation pairs
        /// and up to two rotation-less flights, STAs spread over the day, some
        /// departures crossing midnight. Ids follow 11 §11.3's day stride.
        /// </summary>
        public static List<Move> SyntheticDay(SplitMix64 rng, int day)
        {
            var moves = new List<Move>();
            ulong d = (ulong)day;
            ulong next = (d * 100000UL) + 1UL;
            long pairs = rng.Range(8, 16);
            for (long i = 0; i < pairs; i++)
            {
                ulong arr = next++;
                ulong dep = next++;
                ulong sta = (d * DConst.TicksPerDay) + (ulong)rng.Range(300, 13500);
                ulong std = sta + (ulong)rng.Range(400, 1500);
                ulong minTurn = (ulong)rng.Range(250, 450);
                moves.Add(new Move(arr, false, dep, sta, minTurn));
                moves.Add(new Move(dep, true, arr, std, minTurn));
            }

            if (rng.Permille(600))
            {
                moves.Add(new Move(next++, false, 0UL, (d * DConst.TicksPerDay) + (ulong)rng.Range(300, 14000), 350UL));
            }

            if (rng.Permille(600))
            {
                moves.Add(new Move(next++, true, 0UL, (d * DConst.TicksPerDay) + (ulong)rng.Range(600, 14300), 350UL));
            }

            return moves;
        }

        /// <summary>Appends one day's movements to <paramref name="s"/>.</summary>
        public static void Day(SplitMix64 rng, List<Move> moves, Script s, int stuckPermille)
        {
            var sorted = new List<Move>(moves);
            sorted.Sort((a, b) => a.Flight.CompareTo(b.Flight));
            foreach (Move m in sorted)
            {
                s.Plan(m.PublishTick, m.Flight, m.Departure ? MovementKind.Departure : MovementKind.Arrival, m.Rotation);
                s.Milestone(m.PublishTick, m.Flight, FlightMilestone.PlanPublished, m.PublishTick);
            }

            var onStand = new Dictionary<ulong, ulong>();
            foreach (Move m in sorted)
            {
                if (!m.Departure)
                {
                    onStand[m.Flight] = Arrival(rng, m, s);
                }
            }

            foreach (Move m in sorted)
            {
                if (m.Departure)
                {
                    ulong inbound = m.Rotation != 0UL ? onStand[m.Rotation] : 0UL;
                    Departure(rng, m, s, inbound, stuckPermille);
                }
            }
        }

        private static ulong At(long t, ulong floor)
        {
            return t < (long)floor ? floor : (ulong)t;
        }

        private static ulong Arrival(SplitMix64 rng, Move m, Script s)
        {
            ulong pub = m.PublishTick;
            ulong f = m.Flight;
            ulong planLanded = m.ScheduledTick;
            ulong landed = At((long)planLanded + rng.Range(-30, 240), pub + 1UL);
            ulong planOnStand = planLanded + ArrivalTaxiIn;
            ulong onStand = At((long)planOnStand + rng.Range(-20, 150), landed + 1UL);

            // Landed window: runway holds; a stand wait may open here and span Landed.
            ulong lo = landed > 400UL ? Math.Max(pub + 1UL, landed - 400UL) : pub + 1UL;
            Window(rng, s, f, lo, landed, new[] { Fam.Runway(1), Fam.Runway(2), Fam.Runway(3) }, 2);
            bool spanning = rng.Permille(150);
            if (spanning)
            {
                s.StandUnavailable(At((long)landed - rng.Range(0, 100), lo), f, SpanningStand, (ulong)rng.Range(1, 999));
            }

            s.Milestone(landed, f, FlightMilestone.Landed, planLanded);

            // OnStand window: taxi holds and stand waits.
            Window(rng, s, f, landed, onStand, new[] { Fam.Taxi(1), Fam.Taxi(2), Fam.Taxi(3), Fam.StandNull(), Fam.Stand(4) }, 3);
            if (spanning)
            {
                s.StandAssigned((ulong)rng.Range((long)landed, (long)onStand), f, SpanningStand);
            }

            s.Milestone(onStand, f, FlightMilestone.OnStand, planOnStand);

            // After the terminal checkpoint: ignored milestones and intervals (14 §14.5).
            s.Milestone(onStand + 5UL, f, FlightMilestone.DoorsOpen, planOnStand + 5UL);
            if (rng.Permille(400))
            {
                ulong a = onStand + (ulong)rng.Range(1, 60);
                s.JobBlocked(a, f, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4UL);
                s.JobUnblocked(a + (ulong)rng.Range(0, 200), f, JobKind.BaggageUnload, ResourceKind.Vehicle, DelayCategory.Loading, 4UL);
            }

            return onStand;
        }

        private static void Departure(SplitMix64 rng, Move m, Script s, ulong inboundOnStand, int stuckPermille)
        {
            ulong pub = m.PublishTick;
            ulong f = m.Flight;
            ulong std = m.ScheduledTick;
            ulong planOnStand = std > m.MinTurnaroundTicks ? std - m.MinTurnaroundTicks : 0UL;
            ulong onStand = At((long)planOnStand + rng.Range(-20, 120), pub + 1UL);
            if (m.Rotation != 0UL)
            {
                onStand = Math.Max(onStand, inboundOnStand + (ulong)rng.Range(1, 40));
            }

            s.Milestone(onStand, f, FlightMilestone.OnStand, planOnStand);

            if (rng.Permille(stuckPermille))
            {
                // 14 §14.8: a departure whose Catering never gets a truck never finalises.
                s.JobBlocked(onStand + (ulong)rng.Range(0, 50), f, JobKind.Catering, ResourceKind.Vehicle, DelayCategory.Catering);
                if (rng.Permille(500))
                {
                    s.Missed(onStand + (ulong)rng.Range(100, 400), f, (int)rng.Range(1, 9), (uint)rng.Range(1, 30));
                }

                return;
            }

            ulong planPushback = std;
            ulong pushback = At((long)planPushback + rng.Range(-40, 220), onStand + 1UL);
            ulong planAirborne = std + DepartureTaxiOut;
            ulong airborne = At((long)planAirborne + rng.Range(-30, 150), pushback + 1UL);

            // Pushback window: vehicle waits, the ignored Boarding job-dependency wait, a passenger hold.
            var fams = new List<Fam>();
            foreach ((JobKind k, DelayCategory c) in DepartureJobs)
            {
                fams.Add(Fam.Job(k, c));
            }

            fams.Add(Fam.Hold());
            Window(rng, s, f, onStand, pushback, fams.ToArray(), 4);
            if (rng.Permille(500))
            {
                ulong a = (ulong)rng.Range((long)onStand, (long)pushback);
                s.JobBlocked(a, f, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
                s.JobUnblocked((ulong)rng.Range((long)a, (long)pushback), f, JobKind.Boarding, ResourceKind.JobDependency, DelayCategory.GroundHandling);
            }

            s.Milestone((ulong)rng.Range((long)onStand, (long)pushback), f, FlightMilestone.ReadyToBoard, planOnStand + 120UL);
            bool spanning = rng.Permille(100);
            if (spanning)
            {
                s.TaxiHeld((ulong)rng.Range((long)onStand, (long)pushback), f, SpanningEdge, rng.Permille(500) ? (ulong?)null : (ulong)rng.Range(1, 999));
            }

            s.Milestone(pushback, f, FlightMilestone.Pushback, planPushback);

            // Airborne window: taxi and runway holds, all closed by Airborne (10 §10.3 rule 2).
            Window(rng, s, f, pushback, airborne, new[] { Fam.Taxi(1), Fam.Taxi(2), Fam.Runway(1), Fam.Runway(2) }, 3);
            if (spanning)
            {
                s.TaxiReleased((ulong)rng.Range((long)pushback, (long)airborne), f, SpanningEdge);
            }

            s.Milestone(airborne, f, FlightMilestone.Airborne, planAirborne);

            if (rng.Permille(150))
            {
                long from = Math.Max((long)onStand + 1L, (long)pushback - 30L);
                s.Missed((ulong)rng.Range(from, (long)airborne + 300L), f, (int)rng.Range(1, 12), (uint)rng.Range(1, 30));
            }

            if (rng.Permille(100))
            {
                s.Missed(airborne + (ulong)rng.Range(1, 500), f, (int)rng.Range(1, 3), (uint)rng.Range(1, 30));
            }
        }

        /// <summary>One interval family and key.</summary>
        private readonly struct Fam
        {
            private Fam(int family, ushort key, JobKind job, DelayCategory category)
            {
                Family = family;
                Key = key;
                Kind = job;
                Category = category;
            }

            public int Family { get; }

            public ushort Key { get; }

            public JobKind Kind { get; }

            public DelayCategory Category { get; }

            public static Fam Runway(ushort id) => new Fam(0, id, default, default);

            public static Fam Taxi(ushort id) => new Fam(1, id, default, default);

            public static Fam StandNull() => new Fam(2, 0, default, default);

            public static Fam Stand(ushort id) => new Fam(3, id, default, default);

            public static Fam Job(JobKind k, DelayCategory c) => new Fam(4, 0, k, c);

            public static Fam Hold() => new Fam(5, 0, default, default);
        }

        /// <summary>
        /// Up to <paramref name="max"/> distinct keys, each with one interval in
        /// [lo, hi] and sometimes a second after it, so intervals of one window
        /// overlap freely but no key is ever open twice.
        /// </summary>
        private static void Window(SplitMix64 rng, Script s, ulong f, ulong lo, ulong hi, Fam[] fams, int max)
        {
            var pool = new List<Fam>(fams);
            long n = rng.Range(0, Math.Min(max, pool.Count));
            for (long i = 0; i < n; i++)
            {
                int pick = (int)rng.Range(0, pool.Count - 1);
                Fam fam = pool[pick];
                pool.RemoveAt(pick);
                ulong a = (ulong)rng.Range((long)lo, (long)hi);
                ulong b = (ulong)rng.Range((long)a, (long)hi);
                Interval(rng, s, f, fam, a, b);
                if (b < hi && rng.Permille(200))
                {
                    ulong a2 = (ulong)rng.Range((long)b, (long)hi);
                    Interval(rng, s, f, fam, a2, (ulong)rng.Range((long)a2, (long)hi));
                }
            }
        }

        private static void Interval(SplitMix64 rng, Script s, ulong f, Fam fam, ulong a, ulong b)
        {
            switch (fam.Family)
            {
                case 0:
                    s.RunwayHeld(a, f, fam.Key, (int)rng.Range(1, 6));
                    s.RunwayReleased(b, f, fam.Key);
                    break;
                case 1:
                    s.TaxiHeld(a, f, fam.Key, rng.Permille(300) ? (ulong?)null : (ulong)rng.Range(1, 999));
                    s.TaxiReleased(b, f, fam.Key);
                    break;
                case 2:
                    s.StandUnavailable(a, f, null, null);
                    s.StandAssigned(b, f, null);
                    break;
                case 3:
                    s.StandUnavailable(a, f, fam.Key, (ulong)rng.Range(1, 999));
                    s.StandAssigned(b, f, fam.Key);
                    break;
                case 4:
                    {
                        ulong vehicle = (ulong)rng.Range(1, 5);
                        s.JobBlocked(a, f, fam.Kind, ResourceKind.Vehicle, fam.Category, vehicle);
                        s.JobUnblocked(b, f, fam.Kind, ResourceKind.Vehicle, fam.Category, vehicle);
                        break;
                    }

                default:
                    s.Hold(a, f, (int)rng.Range(1, 40), (uint)rng.Range(1, 30));
                    s.HoldReleased(b, f);
                    break;
            }
        }
    }

    /// <summary>
    /// Runs generated days through a rig, a day ahead of publication (11 §11.5
    /// publishes a day early), checking after every event sim.delay received.
    /// The first failure is kept and reported with the seed and day; checking
    /// stops there so the report is the first divergence.
    /// </summary>
    internal sealed class GeneratedRun
    {
        public readonly DelayRig Rig;
        public readonly DelayModel? Model;
        private readonly SplitMix64 _rng;
        private readonly ulong _seed;
        private readonly Func<SplitMix64, int, List<Move>> _moves;
        private readonly int _stuckPermille;
        private int _generatedThrough = -1;
        private string? _failure;

        public long EventsChecked;

        /// <param name="perEvent">
        /// Called after each event sim.delay received whose flight it still
        /// retains: (system, flight, the event's tick, context for messages).
        /// </param>
        public GeneratedRun(ulong seed, Func<SplitMix64, int, List<Move>> moves, bool model, Action<IDelaySystem, ulong, ulong, string>? perEvent = null, int stuckPermille = 40, bool keepTrace = false)
        {
            _seed = seed;
            _rng = new SplitMix64(seed);
            _moves = moves;
            _stuckPermille = stuckPermille;
            Model = model ? new DelayModel() : null;
            Rig = new DelayRig(new Script());
            Rig.R.Keep = keepTrace;
            Rig.R.After = (env, payload, flight) =>
            {
                if (_failure != null)
                {
                    return;
                }

                string context = string.Format(CultureInfo.InvariantCulture, "seed 0x{0:X}, day {1}, after event {2} {3} f={4}", _seed, env.Tick / DConst.TicksPerDay, Show.Id(env.Id), payload.GetType().Name, flight);
                try
                {
                    EventsChecked++;
                    if (Model != null)
                    {
                        Model.Apply(env, payload);
                        if (flight != 0UL)
                        {
                            string want = Model.Tree(flight);
                            string got = Show.Tree(Rig.Delay, flight);
                            if (want != got)
                            {
                                _failure = context + "\nexpected (14 §14.4-§14.9):\n" + want + "actual:\n" + got;
                                return;
                            }
                        }
                    }

                    if (flight != 0UL && Rig.Delay.TryGetFlightDelay(new FlightId(flight), out FlightDelay _))
                    {
                        perEvent?.Invoke(Rig.Delay, flight, env.Tick, context);
                    }
                }
                catch (Exception ex)
                {
                    _failure = context + "\n" + ex.Message;
                }
            };
        }

        /// <summary>Runs through the end of day <paramref name="lastDay"/>, calling <paramref name="endOfDay"/> with each finished day's index.</summary>
        public void RunDays(int lastDay, Action<int>? endOfDay = null)
        {
            for (int day = (int)(Rig.Host.CurrentTick / DConst.TicksPerDay); day <= lastDay; day++)
            {
                // Day d + 1 publishes during day d.
                GenerateThrough(day + 1);
                Rig.RunTo((ulong)(day + 1) * DConst.TicksPerDay);
                Fail();
                endOfDay?.Invoke(day);
                Fail();
            }
        }

        /// <summary>Appends the generated streams of every day up to and including <paramref name="day"/>.</summary>
        public void GenerateThrough(int day)
        {
            while (_generatedThrough < day)
            {
                _generatedThrough++;
                var s = new Script();
                Generator.Day(_rng, _moves(_rng, _generatedThrough), s, _stuckPermille);
                Rig.Append(s);
            }
        }

        public void Fail()
        {
            Assert.True(_failure == null, _failure);
        }

        public string Context(int day)
        {
            return string.Format(CultureInfo.InvariantCulture, "seed 0x{0:X}, end of day {1}", _seed, day);
        }
    }
}
