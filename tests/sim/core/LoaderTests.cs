using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Core.Tests.LoaderTestKit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-027. The strict content loader against 08-interfaces-core.md §8.11 "The
    /// loader" (Q-011), 04-data-schemas.md "Phase 0/1 content fields", 11 §11.6
    /// (show-up curve) and 07 "Error handling" (Q-030: every load failure is a
    /// FormatException whose message starts with the file's path and names the
    /// offending id). Tests assert the type, the path prefix and the id, nothing
    /// else in the message.
    /// </summary>
    public sealed class LoaderTests
    {
        private const string Small = "size_categories/small.json";
        private const string A320 = "aircraft/a320.json";
        private const string Business = "pax_profiles/business.json";
        private const string Security = "queue_profiles/security_main.json";

        private const string CurveOk = "[ { \"minutes_before_std\": 30, \"share_permille\": 1000 } ]";

        private static string SizeJson(string ordinal)
        {
            return "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": " + ordinal + " }";
        }

        private static string PaxJson(string walk, string curve)
        {
            return "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": " + walk + ", \"show_up_curve\": " + curve + " }";
        }

        private static string QueueJson(string rate, string capacity, string threshold, string hysteresis, string category)
        {
            return "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": " + rate
                + ", \"capacity_standing\": " + capacity + ", \"threshold_wait_minutes\": " + threshold
                + ", \"hysteresis_minutes\": " + hysteresis + ", \"delay_category\": " + category + " }";
        }

        // ------------------------------------------------------------ success paths

        [Fact]
        public void test_loader_valid_fixture_loads_expected_definitions()
        {
            AssertSameDefinitions(ExpectedValid(), Load(Valid()));
        }

        [Fact]
        public void test_loader_reads_files_in_ordinal_path_order_regardless_of_source_order()
        {
            MemorySource valid = Valid();
            IContentDefinition[] expected = ExpectedValid();
            AssertSameDefinitions(expected, Load(valid));
            AssertSameDefinitions(expected, Load(valid.InOrder(valid.Order.Reverse())));
            AssertSameDefinitions(expected, Load(valid.InOrder(valid.Order.OrderBy(p => p, StringComparer.Ordinal))));
            // A culture-aware order puts a10 < a2 < a320 < B777; the output must not follow it.
            AssertSameDefinitions(expected, Load(valid.InOrder(valid.Order.OrderBy(p => p, StringComparer.InvariantCultureIgnoreCase))));

            var gen = new SplitMix64(0x5EED0027A0000001UL);
            for (int i = 0; i < 20; i++)
            {
                List<string> shuffled = valid.Order.ToList();
                for (int k = shuffled.Count - 1; k > 0; k--)
                {
                    int j = (int)(gen.Next() % (ulong)(k + 1));
                    (shuffled[k], shuffled[j]) = (shuffled[j], shuffled[k]);
                }
                AssertSameDefinitions(expected, Load(valid.InOrder(shuffled)));
            }
        }

        [Fact]
        public void test_loader_definition_kind_follows_directory()
        {
            IReadOnlyList<IContentDefinition> defs = Load(Valid());
            for (int i = 0; i < defs.Count; i++)
            {
                string dir = ValidKindPathsOrdinal[i].Split('/')[0];
                ContentKind expected = dir switch
                {
                    "size_categories" => ContentKind.SizeCategory,
                    "aircraft" => ContentKind.Aircraft,
                    "pax_profiles" => ContentKind.PaxProfile,
                    _ => ContentKind.QueueProfile,
                };
                Assert.Equal(expected, defs[i].Kind);
            }
        }

        [Theory]
        [InlineData("policies/extra.json")]
        [InlineData("schemas/aircraft.schema.json")]
        [InlineData("balance/x.json")]
        [InlineData("objects/x.json")]
        [InlineData("airlines/x.json")]
        [InlineData("aircraft_old/x.json")]
        [InlineData("size_category/x.json")]
        [InlineData("data/aircraft/x.json")]
        [InlineData("x/aircraft/y.json")]
        [InlineData("root.json")]
        public void test_loader_ignores_files_outside_the_four_kind_directories(string path)
        {
            // Malformed on purpose: the loader must not even parse it.
            MemorySource source = Valid().With(path, "{ \"schema_version\": 2, \"id\": 1.5, ");
            AssertSameDefinitions(ExpectedValid(), Load(source));
        }

        [Theory]
        [InlineData("aircraft/notes.txt")]
        [InlineData("aircraft/a320.json.bak")]
        [InlineData("pax_profiles/README")]
        [InlineData("queue_profiles/draft.jsonc")]
        public void test_loader_ignores_non_json_files_in_kind_directories(string path)
        {
            MemorySource source = Valid().With(path, "not json {");
            AssertSameDefinitions(ExpectedValid(), Load(source));
        }

        [Fact]
        public void test_loader_empty_source_returns_empty_list()
        {
            var empty = new MemorySource(Array.Empty<KeyValuePair<string, byte[]>>());
            Assert.Empty(Load(empty));
            var ignoredOnly = new MemorySource(new[]
            {
                new KeyValuePair<string, byte[]>("policies/p.json", Utf8("{")),
                new KeyValuePair<string, byte[]>("schemas/s.json", Utf8("[1.5]")),
            });
            Assert.Empty(Load(ignoredOnly));
        }

        [Fact]
        public void test_loader_whitespace_and_key_order_do_not_change_result()
        {
            string pretty = "{\r\n\t\"id\" :\t\"size.small\" ,\r\n  \"ordinal\"\n:\n1,\r\"schema_version\" : 1\r\n}\r\n";
            string compact = "{\"schema_version\":1,\"id\":\"size.small\",\"ordinal\":1}";
            AssertSameDefinitions(ExpectedValid(), Load(Valid().With(Small, pretty)));
            AssertSameDefinitions(ExpectedValid(), Load(Valid().With(Small, compact)));
        }

        [Fact]
        public void test_loader_decimal_strings_are_read_with_fx_parse_exactly()
        {
            string[] decimals = { "0.1", "1.34", "3", "0.0000000003", "2147483647.9999999999", "12.3456789012" };
            foreach (string d in decimals)
            {
                IReadOnlyList<IContentDefinition> defs = Load(Valid().With(Business, PaxJson("\"" + d + "\"", CurveOk)));
                var pax = (PaxProfileDefinition)defs.Single(x => x.Id.Value == "pax.business");
                Assert.Equal(Fx.Parse(d).Raw, pax.WalkSpeedMps.Raw);
            }
            IReadOnlyList<IContentDefinition> q = Load(Valid().With(Security, QueueJson("\"0.3\"", "5", "\"7.25\"", "\"0.0000000001\"", "\"security_queue\"")));
            var queue = (QueueProfileDefinition)q.Single(x => x.Id.Value == "queue.security_main");
            Assert.Equal(Fx.Parse("0.3").Raw, queue.ServiceRatePerServerPerMinute.Raw);
            Assert.Equal(Fx.Parse("7.25").Raw, queue.ThresholdWaitMinutes.Raw);
            Assert.Equal(Fx.Parse("0.0000000001").Raw, queue.HysteresisMinutes.Raw);
            Assert.Equal(5, queue.CapacityStanding);
        }

        [Fact]
        public void test_loader_integer_field_bounds_are_inclusive()
        {
            // ordinal is int32; bucket fields are uint32; capacity is int32 > 0.
            var min = (SizeCategoryDefinition)Load(Valid().With(Small, SizeJson("-2147483648"))).Single(d => d.Id.Value == "size.small");
            Assert.Equal(int.MinValue, min.Ordinal);
            var zero = (SizeCategoryDefinition)Load(Valid().With(Small, SizeJson("0"))).Single(d => d.Id.Value == "size.small");
            Assert.Equal(0, zero.Ordinal);

            string curve = "[ { \"minutes_before_std\": 0, \"share_permille\": 0 }, { \"minutes_before_std\": 4294967295, \"share_permille\": 1000 } ]";
            var pax = (PaxProfileDefinition)Load(Valid().With(Business, PaxJson("\"1\"", curve))).Single(d => d.Id.Value == "pax.business");
            Assert.Equal(4294967295U, pax.ShowUpCurve[1].MinutesBeforeStd);
            Assert.Equal(0U, pax.ShowUpCurve[0].SharePermille);
        }

        [Fact]
        public void test_loader_output_feeds_content_index_factory_without_further_transform()
        {
            IReadOnlyList<IContentDefinition> defs = Load(Valid());
            IContentIndex index = ContentIndexFactory.Create(defs);

            Assert.True(index.TryGet(new ContentId("size.medium"), out SizeCategoryDefinition medium));
            Assert.Equal(2, medium.Ordinal);
            Assert.True(index.TryGet(new ContentId("aircraft.a320"), out AircraftDefinition a320));
            Assert.Equal("size.medium", a320.SizeCategory.Value);
            Assert.True(index.TryGet(new ContentId("pax.business"), out PaxProfileDefinition business));
            Assert.Equal(Fx.Parse("1.34"), business.WalkSpeedMps);
            Assert.True(index.TryGet(new ContentId("queue.security_main"), out QueueProfileDefinition security));
            Assert.Equal(DelayCategory.SecurityQueue, security.Category);
            Assert.False(index.TryGet(new ContentId("aircraft.a320"), out SizeCategoryDefinition _));

            Assert.Equal(new[] { "aircraft.a10", "aircraft.a2", "aircraft.a320", "aircraft.b777" },
                index.AllOf(ContentKind.Aircraft).Select(i => i.Value).ToArray());
            Assert.Equal(new[] { "size.large", "size.medium", "size.small" },
                index.AllOf(ContentKind.SizeCategory).Select(i => i.Value).ToArray());
            Assert.Equal(2, index.AllOf(ContentKind.PaxProfile).Count);
            Assert.Equal(2, index.AllOf(ContentKind.QueueProfile).Count);
        }

        [Fact]
        public void test_loader_same_source_twice_gives_identical_output()
        {
            MemorySource source = Valid();
            IReadOnlyList<IContentDefinition> first = ContentLoaderFactory.Create().Load(source);
            IReadOnlyList<IContentDefinition> again = ContentLoaderFactory.Create().Load(source);
            IContentLoader loader = ContentLoaderFactory.Create();
            IReadOnlyList<IContentDefinition> sameLoader1 = loader.Load(source);
            IReadOnlyList<IContentDefinition> sameLoader2 = loader.Load(source);
            AssertSameDefinitions(first, again);
            AssertSameDefinitions(first, sameLoader1);
            AssertSameDefinitions(first, sameLoader2);
        }

        [Fact]
        public void test_loader_failed_load_does_not_poison_later_loads()
        {
            IContentLoader loader = ContentLoaderFactory.Create();
            Assert.Throws<FormatException>(() => loader.Load(Valid().With(Small, SizeJson("1.5"))));
            AssertSameDefinitions(ExpectedValid(), loader.Load(Valid()));
        }

        // ------------------------------------------------------------ the JSON subset

        [Theory]
        [InlineData("1.5")]
        [InlineData("1.0")]
        [InlineData("0.0")]
        [InlineData("-0.0")]
        [InlineData("1e3")]
        [InlineData("1E3")]
        [InlineData("1e+3")]
        [InlineData("2e-1")]
        [InlineData("1.5e2")]
        [InlineData("0e0")]
        public void test_loader_rejects_number_with_fraction_or_exponent(string number)
        {
            AssertLoadFails(Valid().With(Small, SizeJson(number)), Small);
            string curve = "[ { \"minutes_before_std\": " + number + ", \"share_permille\": 1000 } ]";
            AssertLoadFails(Valid().With(Business, PaxJson("\"1\"", curve)), Business);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("[]")]
        [InlineData("1")]
        [InlineData("\"size.small\"")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1, }")]
        [InlineData("{ \"schema_version\": 1 \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData("{ \"schema_version\" 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData("{ schema_version: 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData("{ 'schema_version': 1, 'id': 'size.small', 'ordinal': 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 } {}")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 } x")]
        [InlineData("// comment\n{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData("{ /* c */ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small, \"ordinal\": 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size\nsmall\", \"ordinal\": 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 01 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": +1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": - 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": -- 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 0x1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": NaN }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": Infinity }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": null }")]
        [InlineData("{ \"schema_version\": 1, \"id\": null, \"ordinal\": 1 }")]
        [InlineData("{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 }\u0000")]
        [InlineData("\u000C{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        public void test_loader_rejects_malformed_json(string text)
        {
            AssertLoadFails(Valid().With(Small, text), Small);
        }

        [Fact]
        public void test_loader_rejects_trailing_comma_in_array()
        {
            string curve = "[ { \"minutes_before_std\": 30, \"share_permille\": 1000 }, ]";
            AssertLoadFails(Valid().With(Business, PaxJson("\"1\"", curve)), Business);
        }

        [Fact]
        public void test_loader_rejects_invalid_utf8()
        {
            byte[] good = Utf8(SizeJson("1"));
            var bad = new List<byte>(good);
            int idAt = Array.IndexOf(good, (byte)'s', 30);
            bad.Insert(idAt, 0xC3);             // a lead byte with no continuation
            AssertLoadFails(Valid().With(Small, bad.ToArray()), Small);

            var overlong = new List<byte>(good);
            overlong.InsertRange(idAt, new byte[] { 0xC0, 0xAF });
            AssertLoadFails(Valid().With(Small, overlong.ToArray()), Small);
        }

        [Theory]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1, \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 1, \"colour\": \"red\" }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"Ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal \": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\" }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"id\": \"size.small\", \"ordinal\": 1 }")]
        [InlineData(Small, "{ }")]
        [InlineData(A320, "{ \"schema_version\": 1, \"id\": \"aircraft.a320\" }")]
        [InlineData(A320, "{ \"schema_version\": 1, \"id\": \"aircraft.a320\", \"size_category\": \"size.medium\", \"ordinal\": 1 }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\" }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"show_up_curve\": [ { \"minutes_before_std\": 30, \"share_permille\": 1000 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"minutes_before_std\": 30 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"share_permille\": 1000 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"minutes_before_std\": 30, \"share_permille\": 1000, \"x\": 1 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"minutes_before_std\": 30, \"minutes_before_std\": 30, \"share_permille\": 1000 } ] }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"delay_category\": \"security_queue\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\", \"delay_category\": \"security_queue\", \"category\": \"security_queue\" }")]
        public void test_loader_rejects_duplicate_unknown_or_missing_key(string path, string text)
        {
            AssertLoadFails(Valid().With(path, text), path);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("2")]
        [InlineData("-1")]
        [InlineData("10")]
        [InlineData("4294967297")]
        [InlineData("\"1\"")]
        [InlineData("true")]
        [InlineData("[1]")]
        public void test_loader_rejects_schema_version_other_than_one(string version)
        {
            string text = "{ \"schema_version\": " + version + ", \"id\": \"size.small\", \"ordinal\": 1 }";
            AssertLoadFails(Valid().With(Small, text), Small);
            string aircraft = "{ \"schema_version\": " + version + ", \"id\": \"aircraft.a320\", \"size_category\": \"size.medium\" }";
            AssertLoadFails(Valid().With(A320, aircraft), A320);
        }

        [Theory]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": 5, \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": true, \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": [\"size.small\"], \"ordinal\": 1 }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": \"1\" }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": false }")]
        [InlineData(Small, "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": {} }")]
        [InlineData(A320, "{ \"schema_version\": 1, \"id\": \"aircraft.a320\", \"size_category\": 2 }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": 1, \"show_up_curve\": [ { \"minutes_before_std\": 30, \"share_permille\": 1000 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": true, \"show_up_curve\": [ { \"minutes_before_std\": 30, \"share_permille\": 1000 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": { \"minutes_before_std\": 30, \"share_permille\": 1000 } }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ [30, 1000] ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"minutes_before_std\": \"30\", \"share_permille\": 1000 } ] }")]
        [InlineData(Business, "{ \"schema_version\": 1, \"id\": \"pax.business\", \"walk_speed_mps\": \"1\", \"show_up_curve\": \"none\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": 1, \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\", \"delay_category\": \"security_queue\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": \"1\", \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\", \"delay_category\": \"security_queue\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": 2, \"hysteresis_minutes\": \"1\", \"delay_category\": \"security_queue\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": 0, \"delay_category\": \"security_queue\" }")]
        [InlineData(Security, "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\", \"delay_category\": 3 }")]
        public void test_loader_rejects_value_of_wrong_type(string path, string text)
        {
            AssertLoadFails(Valid().With(path, text), path);
        }

        [Theory]
        [InlineData(Small, "2147483648")]
        [InlineData(Small, "-2147483649")]
        [InlineData(Small, "99999999999999999999999")]
        [InlineData(Business, "-1")]
        [InlineData(Business, "4294967296")]
        [InlineData(Security, "2147483648")]
        public void test_loader_rejects_integer_outside_field_range(string path, string value)
        {
            string text = path == Small ? SizeJson(value)
                : path == Business ? PaxJson("\"1\"", "[ { \"minutes_before_std\": " + value + ", \"share_permille\": 1000 } ]")
                : QueueJson("\"1\"", value, "\"2\"", "\"1\"", "\"security_queue\"");
            AssertLoadFails(Valid().With(path, text), path);
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("4294967296")]
        public void test_loader_rejects_share_permille_outside_uint32(string value)
        {
            string curve = "[ { \"minutes_before_std\": 30, \"share_permille\": " + value + " } ]";
            AssertLoadFails(Valid().With(Business, PaxJson("\"1\"", curve)), Business);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("1,5")]
        [InlineData(" 1.5")]
        [InlineData("1.5 ")]
        [InlineData("+1.5")]
        [InlineData("01.5")]
        [InlineData("1e3")]
        [InlineData("1.00000000001")]
        [InlineData("abc")]
        [InlineData("1_000")]
        [InlineData("99999999999")]
        [InlineData("-99999999999")]
        public void test_loader_rejects_malformed_or_out_of_range_decimal_string(string value)
        {
            // Fx.Parse's own FormatException or OverflowException becomes a load failure (Q-030).
            AssertLoadFails(Valid().With(Business, PaxJson("\"" + value + "\"", CurveOk)), Business);
            AssertLoadFails(Valid().With(Security, QueueJson("\"1\"", "1", "\"" + value + "\"", "\"0\"", "\"security_queue\"")), Security);
        }

        [Fact]
        public void test_loader_no_bom_and_ordinal_string_comparison_throughout()
        {
            // No BOM: a valid file with EF BB BF prepended fails, in any kind directory.
            foreach (string path in new[] { Small, A320, Business, Security })
            {
                byte[] body = Valid().ReadAll(path);
                byte[] withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
                AssertLoadFails(Valid().With(path, withBom), path);
            }

            // Directory and extension matching is ordinal: these are not kind files.
            MemorySource source = Valid()
                .With("Aircraft/x.json", "garbage")
                .With("SIZE_CATEGORIES/x.json", "garbage")
                .With("aircraft/x.JSON", "garbage")
                .With("aircraft/y.Json", "garbage");
            AssertSameDefinitions(ExpectedValid(), Load(source));

            // Ids are compared ordinally: ids differing only in case are distinct,
            // and a size_category reference must match exactly.
            MemorySource cased = Valid()
                .With("size_categories/upper.json", "{ \"schema_version\": 1, \"id\": \"SIZE.SMALL\", \"ordinal\": 40 }")
                .With("aircraft/z.json", "{ \"schema_version\": 1, \"id\": \"aircraft.z\", \"size_category\": \"SIZE.SMALL\" }");
            IReadOnlyList<IContentDefinition> defs = Load(cased);
            Assert.Equal(ExpectedValid().Length + 2, defs.Count);
            var z = (AircraftDefinition)defs.Single(d => d.Id.Value == "aircraft.z");
            Assert.Equal("SIZE.SMALL", z.SizeCategory.Value);

            MemorySource miscased = Valid().With("aircraft/z.json", "{ \"schema_version\": 1, \"id\": \"aircraft.z\", \"size_category\": \"Size.Small\" }");
            AssertLoadFails(miscased, "aircraft/z.json", "aircraft.z", "Size.Small");
        }

        // ------------------------------------------------------------ cross-file validation

        [Theory]
        [InlineData("aircraft/dup.json", "{ \"schema_version\": 1, \"id\": \"size.small\", \"size_category\": \"size.medium\" }", "size.small")]
        [InlineData("queue_profiles/dup.json", "{ \"schema_version\": 1, \"id\": \"pax.business\", \"service_rate_per_server_per_minute\": \"1\", \"capacity_standing\": 1, \"threshold_wait_minutes\": \"2\", \"hysteresis_minutes\": \"1\", \"delay_category\": \"security_queue\" }", "pax.business")]
        [InlineData("pax_profiles/dup.json", "{ \"schema_version\": 1, \"id\": \"aircraft.a2\", \"walk_speed_mps\": \"1\", \"show_up_curve\": [ { \"minutes_before_std\": 30, \"share_permille\": 1000 } ] }", "aircraft.a2")]
        [InlineData("size_categories/dup.json", "{ \"schema_version\": 1, \"id\": \"queue.security_main\", \"ordinal\": 77 }", "queue.security_main")]
        public void test_loader_rejects_duplicate_id_across_kinds(string path, string text, string id)
        {
            string[] originals =
            {
                "size_categories/small.json", "aircraft/a2.json", "pax_profiles/business.json", "queue_profiles/security_main.json",
            };
            AssertLoadFails(Valid().With(path, text), originals.Concat(new[] { path }).ToArray(), id);
        }

        [Fact]
        public void test_loader_rejects_duplicate_id_within_kind()
        {
            MemorySource source = Valid().With("size_categories/small2.json", "{ \"schema_version\": 1, \"id\": \"size.small\", \"ordinal\": 9 }");
            AssertLoadFails(source, new[] { Small, "size_categories/small2.json" }, "size.small");
        }

        [Theory]
        [InlineData("size.huge")]
        [InlineData("")]
        [InlineData("pax.business")]
        [InlineData("aircraft.a2")]
        [InlineData("size.small ")]
        public void test_loader_rejects_aircraft_with_unresolved_size_category(string sizeCategory)
        {
            // "size_category: id of a size category" (04): an id of another kind does not resolve.
            string text = "{ \"schema_version\": 1, \"id\": \"aircraft.a320\", \"size_category\": \"" + sizeCategory + "\" }";
            string[] ids = sizeCategory.Length > 0 ? new[] { "aircraft.a320", sizeCategory } : new[] { "aircraft.a320" };
            AssertLoadFails(Valid().With(A320, text), A320, ids);
        }

        [Fact]
        public void test_loader_aircraft_resolves_size_category_read_after_it()
        {
            // aircraft/ sorts before size_categories/, so resolution must wait for all files.
            MemorySource source = Valid()
                .With("size_categories/zz_last.json", "{ \"schema_version\": 1, \"id\": \"size.zz\", \"ordinal\": 50 }")
                .With("aircraft/0_first.json", "{ \"schema_version\": 1, \"id\": \"aircraft.first\", \"size_category\": \"size.zz\" }");
            var first = (AircraftDefinition)Load(source)[0];
            Assert.Equal("aircraft.first", first.Id.Value);
            Assert.Equal("size.zz", first.SizeCategory.Value);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("2")]
        [InlineData("-3")]
        public void test_loader_rejects_duplicate_size_ordinal(string ordinal)
        {
            MemorySource source = Valid().With("size_categories/xl.json", "{ \"schema_version\": 1, \"id\": \"size.xl\", \"ordinal\": " + ordinal + " }");
            string[] paths = { "size_categories/xl.json", Small, "size_categories/medium.json", "size_categories/large.json" };
            AssertLoadFails(source, paths, "size.xl", "size.small", "size.medium", "size.large");
        }

        [Theory]
        [InlineData("[ { \"minutes_before_std\": 60, \"share_permille\": 500 }, { \"minutes_before_std\": 30, \"share_permille\": 500 } ]")]
        [InlineData("[ { \"minutes_before_std\": 30, \"share_permille\": 500 }, { \"minutes_before_std\": 30, \"share_permille\": 500 } ]")]
        [InlineData("[ { \"minutes_before_std\": 10, \"share_permille\": 1 }, { \"minutes_before_std\": 20, \"share_permille\": 998 }, { \"minutes_before_std\": 15, \"share_permille\": 1 } ]")]
        [InlineData("[ { \"minutes_before_std\": 30, \"share_permille\": 999 } ]")]
        [InlineData("[ { \"minutes_before_std\": 30, \"share_permille\": 1001 } ]")]
        [InlineData("[ { \"minutes_before_std\": 30, \"share_permille\": 0 } ]")]
        [InlineData("[ ]")]
        [InlineData("[ { \"minutes_before_std\": 30, \"share_permille\": 4294967295 }, { \"minutes_before_std\": 60, \"share_permille\": 1001 } ]")]
        public void test_loader_rejects_show_up_curve_not_ascending_distinct_summing_to_1000(string curve)
        {
            // 11 §11.6. The last case sums to 1000 only modulo 2^32.
            AssertLoadFails(Valid().With(Business, PaxJson("\"1\"", curve)), Business, "pax.business");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-0")]
        [InlineData("-0.0")]
        [InlineData("-0.5")]
        [InlineData("-0.0000000001")]
        public void test_loader_rejects_walk_speed_not_positive(string walk)
        {
            AssertLoadFails(Valid().With(Business, PaxJson("\"" + walk + "\"", CurveOk)), Business, "pax.business");
        }

        [Theory]
        [InlineData("-0.0000000001")]
        [InlineData("-1")]
        public void test_loader_rejects_negative_service_rate(string rate)
        {
            AssertLoadFails(Valid().With(Security, QueueJson("\"" + rate + "\"", "1", "\"2\"", "\"1\"", "\"security_queue\"")), Security, "queue.security_main");
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("-2147483648")]
        public void test_loader_rejects_capacity_not_positive(string capacity)
        {
            AssertLoadFails(Valid().With(Security, QueueJson("\"1\"", capacity, "\"2\"", "\"1\"", "\"security_queue\"")), Security, "queue.security_main");
        }

        [Theory]
        [InlineData("10", "10")]
        [InlineData("10", "10.5")]
        [InlineData("10", "10.0000000001")]
        [InlineData("0", "0")]
        [InlineData("2", "-0.1")]
        [InlineData("-1", "0")]
        [InlineData("-1", "-2")]
        public void test_loader_rejects_queue_profile_hysteresis_not_less_than_threshold(string threshold, string hysteresis)
        {
            // 0 ≤ HysteresisMinutes < ThresholdWaitMinutes.
            string text = QueueJson("\"1\"", "1", "\"" + threshold + "\"", "\"" + hysteresis + "\"", "\"security_queue\"");
            AssertLoadFails(Valid().With(Security, text), Security, "queue.security_main");
        }

        [Fact]
        public void test_loader_accepts_hysteresis_just_below_threshold()
        {
            string text = QueueJson("\"1\"", "1", "\"10\"", "\"9.9999999999\"", "\"security_queue\"");
            var q = (QueueProfileDefinition)Load(Valid().With(Security, text)).Single(d => d.Id.Value == "queue.security_main");
            Assert.Equal(Fx.Parse("9.9999999999"), q.HysteresisMinutes);
        }

        [Theory]
        [InlineData("ground_handling")]
        [InlineData("fuel")]
        [InlineData("Security_queue")]
        [InlineData("SECURITY_QUEUE")]
        [InlineData("security_queue ")]
        [InlineData(" immigration_queue")]
        [InlineData("SecurityQueue")]
        [InlineData("securityQueue")]
        [InlineData("security-queue")]
        [InlineData("immigration")]
        [InlineData("")]
        [InlineData("0")]
        public void test_loader_rejects_queue_profile_category_outside_security_or_immigration(string category)
        {
            string text = QueueJson("\"1\"", "1", "\"2\"", "\"1\"", "\"" + category + "\"");
            AssertLoadFails(Valid().With(Security, text), Security, "queue.security_main");
        }

        [Theory]
        [InlineData("security_queue")]
        [InlineData("immigration_queue")]
        public void test_loader_accepts_both_queue_categories(string category)
        {
            string text = QueueJson("\"1\"", "1", "\"2\"", "\"1\"", "\"" + category + "\"");
            var q = (QueueProfileDefinition)Load(Valid().With(Security, text)).Single(d => d.Id.Value == "queue.security_main");
            Assert.Equal(category == "security_queue" ? DelayCategory.SecurityQueue : DelayCategory.ImmigrationQueue, q.Category);
        }
    }
}
