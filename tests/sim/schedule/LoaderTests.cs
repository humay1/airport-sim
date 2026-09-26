using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.4: the fixture format. Every violation is a hard load failure
    /// that throws FormatException with a message starting "sourceName: " and
    /// naming the 1-based line (07 "Error handling", Q-030).
    /// </summary>
    public sealed class LoaderTests
    {
        private const string Src = "sched_fixture.csv";

        // Line 2: arrival AAA1, line 3: its departure AAA2, line 4: a lone departure BBB1.
        private static string[] Base()
        {
            var rows = new List<string>(Csv.Pair("AAA1", "AAA2", "10:00", "11:00"));
            rows.Add(Csv.Row("BBB1", "D", "12:00"));
            return rows.ToArray();
        }

        private static string[] BaseWith(int line, string row)
        {
            string[] rows = Base();
            rows[line - 2] = row;
            return rows;
        }

        [Fact]
        public void test_loader_accepts_phase0_fixture_with_200_rows()
        {
            ScheduleTable table = Load.Table(Fixture.Bytes(), Fixture.SourceName);
            Assert.Equal(200, table.Rows.Count);
        }

        [Fact]
        public void test_loader_fixture_hash_is_fnv1a64_of_raw_bytes()
        {
            byte[] bytes = Fixture.Bytes();
            ScheduleTable table = Load.Table(bytes, Fixture.SourceName);
            Assert.Equal(Fnv.Raw64(bytes), table.FixtureHash);

            byte[] small = Csv.Of(Base());
            Assert.Equal(Fnv.Raw64(small), Load.Table(small, Src).FixtureHash);
        }

        [Fact]
        public void test_loader_fixture_hash_changes_with_row_order()
        {
            ScheduleTable a = Load.Table(Fixture.Bytes(), Fixture.SourceName);
            ScheduleTable b = Load.Table(Fixture.Permuted(0x0008_0001UL), Fixture.SourceName);
            Assert.NotEqual(a.FixtureHash, b.FixtureHash);
            Assert.Equal(a.Rows.Count, b.Rows.Count);
        }

        [Fact]
        public void test_loader_phase0_fixture_meets_section_11_10()
        {
            // The fixture's own binding rules, checked so an edit cannot quietly break them.
            byte[] bytes = Fixture.Bytes();
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM");
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[bytes.Length - 1]);
            string text = Encoding.UTF8.GetString(bytes);
            Assert.StartsWith(Csv.Header + "\n", text, StringComparison.Ordinal);

            var oracle = new ScheduleOracle(text, 1);
            Assert.Equal(200, oracle.Rows.Count);
            int arrivals = 0, departures = 0, loneA = 0, loneD = 0, clamped = 0;
            foreach (OracleRow r in oracle.Rows)
            {
                Assert.Equal(0U, r.Day);
                Assert.True(r.Repeat, r.Ref);
                Assert.True(ScheduleContent.Curves.ContainsKey(r.Profile), r.Ref);
                Assert.Contains(r.Aircraft, ScheduleContent.AircraftIds);
                if (r.Kind == Core.MovementKind.Arrival)
                {
                    arrivals++;
                    loneA += r.RotationRef.Length == 0 ? 1 : 0;
                }
                else
                {
                    departures++;
                    loneD += r.RotationRef.Length == 0 ? 1 : 0;
                    Assert.True(r.Pax > 0, r.Ref);
                    Assert.True(r.Entry > 0, r.Ref);
                }
            }

            foreach (OracleFlight f in oracle.Flights)
            {
                foreach (OracleInjection inj in ScheduleOracle.InjectionsOf(f))
                {
                    ulong lead = 0;
                    foreach ((uint m, uint _) in ScheduleContent.Curves[f.Row.Profile])
                    {
                        lead = Math.Max(lead, m * SchedConst.TicksPerMinute);
                    }

                    if (inj.Due == 0 && f.Sched < lead)
                    {
                        clamped++;
                    }
                }
            }

            Assert.Equal(100, arrivals);
            Assert.Equal(100, departures);
            Assert.True(loneA >= 1 && loneD >= 1, "needs a lone A and a lone D");
            Assert.True(clamped > 0, "needs a show-up curve clamped to tick 0");
        }

        [Fact]
        public void test_loader_accepts_departure_with_zero_pax_and_no_entry_node()
        {
            ScheduleTable t = Load.Table(Csv.Of(Csv.Row("Z1", "D", "12:00", pax: "0", entry: "")), Src);
            Assert.Single(t.Rows);
        }

        [Fact]
        public void test_loader_rejects_reordered_header_with_line_number()
        {
            string header = Csv.Header.Replace("flight_ref,day,repeat_daily", "flight_ref,repeat_daily,day", StringComparison.Ordinal);
            byte[] csv = Csv.Utf8(header + "\n" + string.Join("\n", Base()) + "\n");
            Load.AssertFails(csv, Src, 1);
        }

        [Theory]
        [InlineData("flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille")]
        [InlineData("flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node,extra")]
        [InlineData("Flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node")]
        [InlineData("flight_ref, day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node")]
        [InlineData("flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node,")]
        public void test_loader_rejects_header_not_byte_equal_with_line_number(string header)
        {
            byte[] csv = Csv.Utf8(header + "\n" + string.Join("\n", Base()) + "\n");
            Load.AssertFails(csv, Src, 1);
        }

        [Fact]
        public void test_loader_rejects_empty_file_with_line_number()
        {
            Load.AssertFails(Array.Empty<byte>(), Src, 1);
        }

        [Fact]
        public void test_loader_rejects_bom_with_line_number()
        {
            byte[] body = Csv.Of(Base());
            var csv = new byte[body.Length + 3];
            csv[0] = 0xEF;
            csv[1] = 0xBB;
            csv[2] = 0xBF;
            Array.Copy(body, 0, csv, 3, body.Length);
            Load.AssertFails(csv, Src, 1);
        }

        [Fact]
        public void test_loader_rejects_crlf_line_ending_with_line_number()
        {
            string[] rows = Base();
            byte[] csv = Csv.Utf8(Csv.Header + "\n" + rows[0] + "\n" + rows[1] + "\r\n" + rows[2] + "\n");
            Load.AssertFails(csv, Src, 3);
        }

        [Fact]
        public void test_loader_rejects_missing_final_newline_with_line_number()
        {
            string[] rows = Base();
            byte[] csv = Csv.Utf8(Csv.Header + "\n" + string.Join("\n", rows));
            Load.AssertFails(csv, Src, 4);
        }

        [Fact]
        public void test_loader_rejects_blank_line_with_line_number()
        {
            string[] rows = Base();
            byte[] csv = Csv.Of(rows[0], "", rows[1], rows[2]);
            Load.AssertFails(csv, Src, 3);
        }

        [Fact]
        public void test_loader_rejects_trailing_blank_line_with_line_number()
        {
            byte[] csv = Csv.Of(Base()[0], Base()[1], Base()[2], "");
            Load.AssertFails(csv, Src, 5);
        }

        [Fact]
        public void test_loader_rejects_comment_line_with_line_number()
        {
            string[] rows = Base();
            byte[] csv = Csv.Of(rows[0], "# a comment", rows[1], rows[2]);
            Load.AssertFails(csv, Src, 3);
        }

        [Fact]
        public void test_loader_rejects_quoted_field_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("\"BBB1\"", "D", "12:00"))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_extra_field_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00") + ",x")), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_missing_field_with_line_number()
        {
            string row = Csv.Row("BBB1", "D", "12:00");
            Load.AssertFails(Csv.Of(BaseWith(4, row.Substring(0, row.LastIndexOf(',')))), Src, 4);
        }

        [Theory]
        [InlineData(" BBB1", "business", "12:00")]
        [InlineData("BBB1 ", "business", "12:00")]
        [InlineData("BBB1", " business", "12:00")]
        [InlineData("BBB1", "business ", "12:00")]
        [InlineData("BBB1", "business", "12:00 ")]
        [InlineData("BBB1", "business", "\t12:00")]
        public void test_loader_rejects_whitespace_around_field_with_line_number(string flightRef, string profile, string sched)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row(flightRef, "D", sched, profile: profile))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_whitespace_around_numeric_field_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", pax: " 100"))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_invalid_utf8_with_line_number()
        {
            byte[] good = Csv.Of(Base());
            string text = Encoding.UTF8.GetString(good);
            int at = text.IndexOf("BBB1", StringComparison.Ordinal);
            good[at + 1] = 0xFF;
            Load.AssertFails(good, Src, 4);
        }

        [Fact]
        public void test_loader_rejects_duplicate_flight_ref_with_line_number()
        {
            string[] rows = Base();
            byte[] csv = Csv.Of(rows[0], rows[1], rows[2], Csv.Row("BBB1", "D", "13:00"));
            Load.AssertFails(csv, Src, 4, 5);
        }

        [Theory]
        [InlineData("x")]
        [InlineData("-1")]
        [InlineData("")]
        [InlineData("4294967296")]
        [InlineData("1.0")]
        public void test_loader_rejects_malformed_day_with_line_number(string day)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", day: day))), Src, 4);
        }

        [Theory]
        [InlineData("2")]
        [InlineData("")]
        [InlineData("true")]
        [InlineData("-1")]
        public void test_loader_rejects_repeat_daily_not_0_or_1_with_line_number(string repeat)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", repeat: repeat))), Src, 4);
        }

        [Theory]
        [InlineData("d")]
        [InlineData("X")]
        [InlineData("")]
        [InlineData("DD")]
        public void test_loader_rejects_movement_not_a_or_d_with_line_number(string movement)
        {
            string row = string.Join(",", new[] { "BBB1", "0", "0", movement, "NVA", "a320", "12:00", "", "35", "100", "business", "500", "10", "1" });
            Load.AssertFails(Csv.Of(BaseWith(4, row)), Src, 4);
        }

        [Theory]
        [InlineData("6:00")]
        [InlineData("12:5")]
        [InlineData("1200")]
        [InlineData("24:00")]
        [InlineData("12:60")]
        [InlineData("")]
        [InlineData("12:00:00")]
        [InlineData("-1:00")]
        [InlineData("ab:cd")]
        public void test_loader_rejects_malformed_sched_hhmm_with_line_number(string sched)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", sched))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_unknown_rotation_ref_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", rotation: "ZZZ9"))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_self_rotation_ref_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", rotation: "BBB1"))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_rotation_with_same_movement_with_line_number()
        {
            byte[] csv = Csv.Of(
                Csv.Row("C1", "D", "10:00", rotation: "C2"),
                Csv.Row("C2", "D", "11:00", rotation: "C1"));
            Load.AssertFails(csv, Src, 2, 3);
        }

        [Fact]
        public void test_loader_rejects_rotation_not_mutual_with_line_number()
        {
            byte[] csv = Csv.Of(
                Csv.Row("AAA1", "A", "10:00", rotation: "AAA2"),
                Csv.Row("AAA2", "D", "11:00", rotation: ""));
            Load.AssertFails(csv, Src, 2, 3);
        }

        [Fact]
        public void test_loader_rejects_rotation_pointing_at_third_row_with_line_number()
        {
            byte[] csv = Csv.Of(
                Csv.Row("AAA1", "A", "10:00", rotation: "AAA2"),
                Csv.Row("AAA2", "D", "11:00", rotation: "AAA3"),
                Csv.Row("AAA3", "A", "09:00", rotation: "AAA2"));
            Load.AssertFails(csv, Src, 2, 3, 4);
        }

        [Fact]
        public void test_loader_rejects_rotation_across_days_with_line_number()
        {
            byte[] csv = Csv.Of(
                Csv.Row("AAA1", "A", "22:00", rotation: "AAA2", day: "0"),
                Csv.Row("AAA2", "D", "06:00", rotation: "AAA1", day: "1"));
            Load.AssertFails(csv, Src, 2, 3);
        }

        [Theory]
        [InlineData("11:00", "11:00")]
        [InlineData("12:00", "11:00")]
        [InlineData("23:50", "00:30")]
        public void test_loader_rejects_rotation_with_sta_not_before_std_with_line_number(string sta, string std)
        {
            Load.AssertFails(Csv.Of(Csv.Pair("AAA1", "AAA2", sta, std)), Src, 2, 3);
        }

        [Fact]
        public void test_loader_rejects_rotation_with_mismatched_repeat_daily_with_line_number()
        {
            byte[] csv = Csv.Of(
                Csv.Row("AAA1", "A", "10:00", rotation: "AAA2", repeat: "1"),
                Csv.Row("AAA2", "D", "11:00", rotation: "AAA1", repeat: "0"));
            Load.AssertFails(csv, Src, 2, 3);
        }

        [Fact]
        public void test_loader_rejects_arrival_with_pax_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(2, Csv.Row("AAA1", "A", "10:00", rotation: "AAA2", pax: "5"))), Src, 2);
        }

        [Fact]
        public void test_loader_rejects_arrival_with_entry_node_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(2, Csv.Row("AAA1", "A", "10:00", rotation: "AAA2", entry: "1"))), Src, 2);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("x")]
        [InlineData("")]
        [InlineData("2147483648")]
        public void test_loader_rejects_malformed_pax_with_line_number(string pax)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", pax: pax))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_departure_with_pax_and_no_entry_node_with_line_number()
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", pax: "10", entry: ""))), Src, 4);
        }

        [Theory]
        [InlineData("x")]
        [InlineData("-1")]
        [InlineData("4294967296")]
        public void test_loader_rejects_malformed_entry_node_with_line_number(string entry)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", entry: entry))), Src, 4);
        }

        [Theory]
        [InlineData("1001", "10")]
        [InlineData("-1", "10")]
        [InlineData("", "10")]
        [InlineData("500", "1001")]
        [InlineData("500", "-1")]
        [InlineData("500", "")]
        public void test_loader_rejects_permille_out_of_range_with_line_number(string hold, string assist)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", hold: hold, assist: assist))), Src, 4);
        }

        [Theory]
        [InlineData("")]
        [InlineData("-5")]
        [InlineData("x")]
        public void test_loader_rejects_malformed_min_turnaround_with_line_number(string minTurn)
        {
            Load.AssertFails(Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "12:00", minTurn: minTurn))), Src, 4);
        }

        [Fact]
        public void test_loader_rejects_airline_hash_collision_with_line_number()
        {
            // Precondition: the two codes collide under FNV-1a-32 (found offline by search).
            Assert.Equal(Fnv.Airline32("LQNQX"), Fnv.Airline32("ZAORB"));
            string[] rows = Base();
            byte[] csv = Csv.Of(
                rows[0],
                rows[1],
                Csv.Row("BBB1", "D", "12:00", airline: "LQNQX"),
                Csv.Row("BBB2", "D", "12:30", airline: "ZAORB"));
            Load.AssertFails(csv, Src, 4, 5);
        }

        [Fact]
        public void test_loader_accepts_distinct_airlines_without_collision()
        {
            string[] rows = Base();
            byte[] csv = Csv.Of(rows[0], rows[1], Csv.Row("BBB1", "D", "12:00", airline: "LQNQX"), Csv.Row("BBB2", "D", "12:30", airline: "LQNQY"));
            Assert.Equal(4, Load.Table(csv, Src).Rows.Count);
        }

        [Fact]
        public void test_loader_rejects_more_than_99999_rows_per_day()
        {
            var sb = new StringBuilder();
            sb.Append(Csv.Header).Append('\n');
            for (int i = 0; i < 100000; i++)
            {
                sb.Append('F').Append(i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(",0,0,A,NVA,a320,06:00,,35,0,business,0,0,\n");
            }

            Load.AssertFails(Csv.Utf8(sb.ToString()), Src);
        }

        [Fact]
        public void test_loader_message_names_the_source_it_was_given()
        {
            byte[] csv = Csv.Of(BaseWith(4, Csv.Row("BBB1", "D", "25:00")));
            Load.AssertFails(csv, "other/name.csv", 4);
        }
    }
}
