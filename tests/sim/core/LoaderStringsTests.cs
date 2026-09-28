using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Core.Tests.LoaderTestKit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// JSON strings in content files, 04-data-schemas.md "Strings" (Q-033
    /// item 7). The only escapes are \" \\ \/ \b \f \n \r \t and \uXXXX with
    /// exactly four hex digits of either case. A \u in D800-DFFF (a surrogate)
    /// fails, so non-BMP characters are raw UTF-8. Any other escape fails,
    /// and so do a raw character below 0x20 inside a string and invalid UTF-8
    /// anywhere in the file. Keys follow the same rule and compare ordinally
    /// after unescaping. Every failure is a load failure (Q-030): exactly
    /// FormatException, with the message starting with the file's path.
    ///
    /// The fixture's pax_profiles/leisure.json id is referenced by no other
    /// file, so its id string can carry any text. Each negative case has a
    /// matching positive control built the same way. A malformed key is not
    /// tested on its own, because it would also fail as an unknown key; the
    /// key rule is covered by the escaped-key positive case.
    /// </summary>
    public sealed class LoaderStringsTests
    {
        private const string Leisure = "pax_profiles/leisure.json";

        private const string LeisureTail =
            "\", \"walk_speed_mps\": \"0.0000000003\", \"show_up_curve\": [ { \"minutes_before_std\": 0, \"share_permille\": 1000 } ] }";

        /// <summary>leisure.json with <paramref name="idBytes"/> as the raw bytes between the id's quotes.</summary>
        private static byte[] LeisureWithIdBytes(byte[] idBytes)
        {
            return Utf8("{ \"schema_version\": 1, \"id\": \"").Concat(idBytes).Concat(Utf8(LeisureTail)).ToArray();
        }

        private static byte[] LeisureWithIdText(string jsonIdText)
        {
            return LeisureWithIdBytes(Utf8(jsonIdText));
        }

        private static byte[] Hex(string hex)
        {
            return Enumerable.Range(0, hex.Length / 2)
                .Select(i => byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                .ToArray();
        }

        private static PaxProfileDefinition LoadedLeisure(byte[] leisureFile, string expectedId)
        {
            IReadOnlyList<IContentDefinition> defs = Load(Valid().With(Leisure, leisureFile));
            IContentDefinition def = Assert.Single(defs, d => d.Id.Value == expectedId);
            Assert.DoesNotContain(defs, d => d.Id.Value == "pax.leisure" && expectedId != "pax.leisure");
            return Assert.IsType<PaxProfileDefinition>(def);
        }

        // ------------------------------------------------------------- accepted

        [Fact]
        public void test_loader_strings_builder_control_loads_plain_id()
        {
            PaxProfileDefinition p = LoadedLeisure(LeisureWithIdText("pax.leisure"), "pax.leisure");
            Assert.Single(p.ShowUpCurve);
        }

        [Fact]
        public void test_loader_strings_eight_standard_escapes_decode()
        {
            LoadedLeisure(LeisureWithIdText(@"pax.\""\\\/\b\f\n\r\t."), "pax.\"\\/\b\f\n\r\t.");
        }

        [Theory]
        [InlineData(@"pax.\u0041", "pax.A")]
        [InlineData(@"pax.\u00e9", "pax.\u00e9")]
        [InlineData(@"pax.\u00E9", "pax.\u00e9")]
        [InlineData(@"pax.\uD7FF", "pax.\uD7FF")]
        [InlineData(@"pax.\uE000", "pax.\uE000")]
        [InlineData(@"pax.\uFFFF", "pax.\uFFFF")]
        [InlineData(@"pax.\u0000x", "pax.\u0000x")]
        [InlineData(@"pax.\u001f", "pax.\u001f")]
        [InlineData(@"\u0070ax.leisure", "pax.leisure")]
        public void test_loader_strings_unicode_escape_outside_surrogates_decodes(string jsonId, string expected)
        {
            LoadedLeisure(LeisureWithIdText(jsonId), expected);
        }

        [Fact]
        public void test_loader_strings_non_bmp_character_as_raw_utf8_loads()
        {
            // U+1F600 as raw UTF-8 (F0 9F 98 80) is the only way to write it.
            LoadedLeisure(LeisureWithIdBytes(Utf8("pax.").Concat(Hex("F09F9880")).ToArray()), "pax.\uD83D\uDE00");
        }

        [Fact]
        public void test_loader_strings_raw_del_and_space_are_not_control_characters()
        {
            LoadedLeisure(LeisureWithIdBytes(Utf8("pax. ").Concat(new byte[] { 0x7F }).ToArray()), "pax. \u007F");
        }

        [Fact]
        public void test_loader_strings_escaped_key_compares_after_unescaping()
        {
            // "\u0069d" is the key "id" once unescaped.
            byte[] file = Utf8("{ \"schema_version\": 1, \"\\u0069d\": \"pax.leisure" + LeisureTail);
            LoadedLeisure(file, "pax.leisure");
        }

        // ------------------------------------------------------------- rejected

        [Theory]
        [InlineData(@"pax.\uD800")]
        [InlineData(@"pax.\uDBFF")]
        [InlineData(@"pax.\uDC00")]
        [InlineData(@"pax.\uDFFF")]
        [InlineData(@"pax.\udfff")]
        [InlineData(@"pax.\ud83d\ude00")]
        [InlineData(@"pax.\uD83D\uDE00")]
        public void test_loader_strings_surrogate_unicode_escape_fails_with_path(string jsonId)
        {
            AssertLoadFails(Valid().With(Leisure, LeisureWithIdText(jsonId)), Leisure);
        }

        [Theory]
        [InlineData(@"pax.\a")]
        [InlineData(@"pax.\x41")]
        [InlineData(@"pax.\U00000041")]
        [InlineData(@"pax.\0")]
        [InlineData(@"pax.\'")]
        [InlineData(@"pax.\ ")]
        [InlineData(@"pax.\u41")]
        [InlineData(@"pax.\u004")]
        [InlineData(@"pax.\u00G1")]
        [InlineData(@"pax.\u{41}")]
        [InlineData(@"pax.\u+041")]
        [InlineData(@"pax.\u-041")]
        public void test_loader_strings_other_escape_fails_with_path(string jsonId)
        {
            AssertLoadFails(Valid().With(Leisure, LeisureWithIdText(jsonId)), Leisure);
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x01)]
        [InlineData(0x08)]
        [InlineData(0x09)]
        [InlineData(0x0A)]
        [InlineData(0x0D)]
        [InlineData(0x1F)]
        public void test_loader_strings_raw_control_character_in_string_fails_with_path(int control)
        {
            byte[] id = Utf8("pax.").Concat(new[] { (byte)control }).Concat(Utf8("leisure")).ToArray();
            AssertLoadFails(Valid().With(Leisure, LeisureWithIdBytes(id)), Leisure);
        }

        [Theory]
        [InlineData("80")]
        [InlineData("BF")]
        [InlineData("C3")]
        [InlineData("C0AF")]
        [InlineData("C1BF")]
        [InlineData("E08080")]
        [InlineData("EDA080")]
        [InlineData("EDBFBF")]
        [InlineData("F09F98")]
        [InlineData("F4908080")]
        [InlineData("F5808080")]
        [InlineData("FE")]
        [InlineData("FF")]
        public void test_loader_strings_invalid_utf8_in_string_fails_with_path(string hex)
        {
            // Lone continuation, truncated sequence, overlong forms, an encoded
            // surrogate, above U+10FFFF and never-valid bytes.
            byte[] id = Utf8("pax.").Concat(Hex(hex)).Concat(Utf8("x")).ToArray();
            AssertLoadFails(Valid().With(Leisure, LeisureWithIdBytes(id)), Leisure);
        }

        [Fact]
        public void test_loader_strings_invalid_utf8_outside_a_string_fails_with_path()
        {
            byte[] file = Utf8("{ ").Concat(Hex("FF")).Concat(Utf8("\"schema_version\": 1, \"id\": \"pax.leisure" + LeisureTail)).ToArray();
            AssertLoadFails(Valid().With(Leisure, file), Leisure);
        }
    }
}
