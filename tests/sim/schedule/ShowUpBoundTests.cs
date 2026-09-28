using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using AirportSim.Sim.Flow;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// Q-038. 11 §11.9a: at construction, a resolved pax_profile with a bucket
    /// whose minutes_before_std exceeds MAX_SHOW_UP_MINUTES_BEFORE_STD (1440)
    /// is a load failure, reported in a fixed order. 11 §11.6: so no injection
    /// is ever due before its flight's publication. Fixtures and assertions
    /// are the ones 11 §11.10 pins.
    /// </summary>
    public sealed class ShowUpBoundTests
    {
        // Ids carry no digits, so a message containing "1441", "1500" or
        // "2000" can only have taken it from the offending bucket.
        private const string Inclusive = "showinclusive";
        private const string Late = "showlate";
        private const string Multi = "showmulti";
        private const string Unresolved = "showmissing";
        private const string BadAircraft = "zzjet";

        private static readonly (uint Minutes, uint Share)[] InclusiveCurve = { (1440, 1000) };
        private static readonly (uint Minutes, uint Share)[] LateCurve = { (1441, 1000) };
        private static readonly (uint Minutes, uint Share)[] MultiCurve = { (60, 500), (1500, 300), (2000, 200) };

        private static IContentIndex BoundContent()
        {
            return ScheduleContent.Index((Inclusive, InclusiveCurve), (Late, LateCurve), (Multi, MultiCurve));
        }

        private static IScheduleSystem Construct(params string[] rows)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(new SimHostConfig(1UL, BoundContent(), new RecordingCheckpointSink(), new NullLog()));
            ScheduleTable table = Load.Table(Csv.Of(rows), "bound.csv");
            return ScheduleFactory.CreateSystem(b.Services, table, null);
        }

        private static FormatException AssertConstructionFails(string[] rows, params string[] parts)
        {
            FormatException ex = Assert.Throws<FormatException>(() => Construct(rows));
            Assert.StartsWith("sim.schedule: ", ex.Message, StringComparison.Ordinal);
            foreach (string part in parts)
            {
                Assert.Contains(part, ex.Message, StringComparison.Ordinal);
            }

            return ex;
        }

        // ---- test_profile_with_show_up_beyond_publish_lead_fails_load ----

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_a_bound_is_inclusive()
        {
            Assert.NotNull(Construct(
                Csv.Row("QM500", "D", "12:00", profile: Inclusive),
                Csv.Row("QA100", "D", "12:00")));

            AssertConstructionFails(
                new[] { Csv.Row("QM500", "D", "12:00", profile: Late), Csv.Row("QA100", "D", "12:00") },
                "QM500", "pax_profile", Late, "1441");
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_b_pax_does_not_matter()
        {
            AssertConstructionFails(
                new[] { Csv.Row("QM500", "D", "12:00", pax: "0", entry: "", profile: Late), Csv.Row("QA100", "D", "12:00") },
                "QM500", "pax_profile", Late, "1441");
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_b_on_arrival_row()
        {
            // "This applies whatever the row's pax" (11 §11.9a): an arrival's
            // pax is 0 at Phase 0 and its profile still resolves at construction.
            AssertConstructionFails(
                new[] { Csv.Row("QM500", "A", "12:00", profile: Late), Csv.Row("QA100", "D", "12:00") },
                "QM500", "pax_profile", Late, "1441");
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_c_first_offending_bucket_is_named()
        {
            FormatException ex = AssertConstructionFails(
                new[] { Csv.Row("QM500", "D", "12:00", profile: Multi), Csv.Row("QA100", "D", "12:00") },
                "QM500", "pax_profile", Multi, "1500");
            Assert.DoesNotContain("2000", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_d_bound_on_earlier_row_wins()
        {
            // R1 = QA100 < R2 = QB200; file order is descending.
            FormatException ex = AssertConstructionFails(
                new[] { Csv.Row("QB200", "D", "12:00", aircraft: BadAircraft), Csv.Row("QA100", "D", "12:00", profile: Late) },
                "QA100", "1441");
            Assert.DoesNotContain("QB200", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(BadAircraft, ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_d_resolution_on_earlier_row_wins()
        {
            FormatException ex = AssertConstructionFails(
                new[] { Csv.Row("QB200", "D", "12:00", profile: Late), Csv.Row("QA100", "D", "12:00", aircraft: BadAircraft) },
                "QA100", "aircraft_type", BadAircraft);
            Assert.DoesNotContain("QB200", ex.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("1441", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_d_resolution_wins_on_same_row()
        {
            FormatException ex = AssertConstructionFails(
                new[] { Csv.Row("QA100", "D", "12:00", aircraft: BadAircraft, profile: Late) },
                "QA100", "aircraft_type", BadAircraft);
            Assert.DoesNotContain("1441", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void test_profile_with_show_up_beyond_publish_lead_fails_load_d_unresolved_profile_is_reported()
        {
            AssertConstructionFails(
                new[] { Csv.Row("QA100", "D", "12:00", profile: Unresolved) },
                "QA100", "pax_profile", Unresolved);
        }

        // ---- test_injection_tick_never_before_publication_tick_on_any_day ----

        private static readonly (uint Minutes, uint Share)[] EdgeCurve = { (60, 400), (1440, 600) };

        [Fact]
        public void test_injection_tick_never_before_publication_tick_on_any_day()
        {
            const string EdgeProfile = "showedge";
            byte[] csv = Csv.Of(Csv.Row("E1", "D", "00:00", day: "0", repeat: "1", pax: "10", profile: EdgeProfile, hold: "0", assist: "0", entry: "1"));
            var rig = new HostRig(csv, "edge.csv", withFlow: true, record: true, content: ScheduleContent.Index((EdgeProfile, EdgeCurve)));
            rig.RunTo(3UL * SchedConst.TicksPerDay);
            Assert.Equal(3UL * SchedConst.TicksPerDay, rig.Host.CurrentTick);

            RecordingFlow flow = rig.Flow!;
            EventLog log = rig.Events!;
            string trace = string.Join("\n", flow.Injections.ConvertAll(i => i.ToString()));

            // Four occurrences published inside the run, days 0..3; day 4 not yet.
            var planTicks = new Dictionary<ulong, ulong>();
            for (int i = 0; i < log.Events.Count; i++)
            {
                RecordedEvent e = log.Events[i];
                if (e.IsPlan)
                {
                    Assert.False(planTicks.ContainsKey(e.Flight), "second FlightPlanPublished for flight " + e.Flight.ToString(CultureInfo.InvariantCulture));
                    Assert.Equal(e.Env.Tick, log.HandledTicks[i]);
                    planTicks.Add(e.Flight, log.HandledTicks[i]);
                }
            }

            Assert.Equal(4, planTicks.Count);
            Assert.Equal(8, flow.Injections.Count);
            Assert.Equal(40L, flow.InjectedTotal);

            // (ScheduledTick; PublishTick = 1440-bucket tick; 60-bucket tick), 11 §11.10.
            (ulong Sched, ulong Publish, ulong Bucket60)[] days =
            {
                (0UL, 0UL, 0UL),
                (14400UL, 0UL, 13800UL),
                (28800UL, 14400UL, 28200UL),
                (43200UL, 28800UL, 42600UL),
            };

            for (uint d = 0; d < days.Length; d++)
            {
                ulong id = (d * SchedConst.DayStride) + 1UL;
                string where = "day " + d.ToString(CultureInfo.InvariantCulture) + " (flight " + id.ToString(CultureInfo.InvariantCulture) + ")\n" + trace;

                FlightRecord r = rig.Flight(id);
                Assert.Equal(d, r.DayIndex);
                Assert.Equal(days[d].Sched, r.ScheduledTick);
                Assert.Equal(days[d].Publish, r.PublishTick);

                Assert.True(planTicks.TryGetValue(id, out ulong planTick), "no FlightPlanPublished for " + where);
                Assert.Equal(r.PublishTick, planTick);

                List<Injection> mine = flow.Injections.FindAll(i => i.Key.Flight.Value == id);
                Assert.True(mine.Count == 2, "expected exactly two Inject calls for " + where);
                int total = 0;
                int sixes = 0;
                int fours = 0;
                foreach (Injection inj in mine)
                {
                    Assert.True(inj.Tick >= planTick, "Inject at tick " + inj.Tick.ToString(CultureInfo.InvariantCulture) + " before publication at " + planTick.ToString(CultureInfo.InvariantCulture) + " for " + where);
                    Assert.Equal(FlowDirection.Departing, inj.Key.Direction);
                    Assert.Equal(EdgeProfile, inj.Key.PaxProfile.Value);
                    Assert.Equal(0, inj.ClassIndex);
                    Assert.Equal(1U, inj.At);
                    total += inj.Count;
                    if (inj.Count == 6)
                    {
                        sixes++;
                        Assert.True(inj.Tick == planTick, "6-passenger Inject not on the publication tick for " + where);
                    }
                    else if (inj.Count == 4)
                    {
                        fours++;
                        Assert.True(inj.Tick == days[d].Bucket60, "4-passenger Inject not at ScheduledTick - 600 (clamped) for " + where);
                    }
                }

                Assert.True(sixes == 1 && fours == 1, "expected one 6-passenger and one 4-passenger Inject for " + where);
                Assert.Equal(10, total);
                Assert.Equal(0, rig.Schedule.PendingInjectionCount(new FlightId(id)));
            }

            Assert.False(planTicks.ContainsKey((4UL * SchedConst.DayStride) + 1UL));
        }
    }
}
