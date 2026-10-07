using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// 15 §15.16 "Looks data": RenderFactory.LoadLooks over an IContentSource,
    /// each fault it rejects, the shipped data/looks/looks.json, and
    /// RenderFactory.DefaultLooks(). Tests assert the exception type and the
    /// "looks/looks.json: " prefix, and nothing else in the message.
    /// </summary>
    public sealed class RenderLooksTests
    {
        private const string Path = "looks/looks.json";
        private const string Prefix = "looks/looks.json: ";

        private const string Valid =
            "{\n" +
            "  \"schema_version\": 1,\n" +
            "  \"id\": \"looks\",\n" +
            "  \"default_livery\": { \"fuselage\": \"#F4F5F7\", \"tail\": \"#2F5D9E\", \"cheatline\": \"#2F5D9F\", \"engines\": \"#9AA1A9\", \"logo\": \"#F4F5F8\", \"mark\": \"none\" },\n" +
            "  \"liveries\": {\n" +
            "    \"ZZ9\": { \"fuselage\": \"#112233\", \"tail\": \"#445566\", \"cheatline\": \"#778899\", \"engines\": \"#AABBCC\", \"logo\": \"#DDEEFF\", \"mark\": \"star\" },\n" +
            "    \"AB\": { \"fuselage\": \"#010203\", \"tail\": \"#040506\", \"cheatline\": \"#070809\", \"engines\": \"#0A0B0C\", \"logo\": \"#0D0E0F\", \"mark\": \"crescent\" },\n" +
            "    \"Q\": { \"fuselage\": \"#FFFFFF\", \"tail\": \"#000000\", \"cheatline\": \"#FF0000\", \"engines\": \"#00FF00\", \"logo\": \"#0000FF\", \"mark\": \"disc\" },\n" +
            "    \"ABCDEFGH\": { \"fuselage\": \"#123456\", \"tail\": \"#654321\", \"cheatline\": \"#ABCDEF\", \"engines\": \"#FEDCBA\", \"logo\": \"#0F0F0F\", \"mark\": \"bars\" }\n" +
            "  },\n" +
            "  \"passengers\": {\n" +
            "    \"tops\": [ \"#300000\", \"#100000\", \"#200000\" ],\n" +
            "    \"bottoms\": [ \"#000010\" ],\n" +
            "    \"skins\": [ \"#C68E6B\", \"#8D5524\" ],\n" +
            "    \"hairs\": [ \"#3A2A1F\" ],\n" +
            "    \"bags\": [ \"#5A4A3A\", \"#000001\" ]\n" +
            "  }\n" +
            "}\n";

        private static byte[] Utf8(string text)
        {
            return new UTF8Encoding(false).GetBytes(text);
        }

        private static string Replace(string find, string with)
        {
            Assert.True(Valid.Contains(find, StringComparison.Ordinal), "test bug: the valid file lacks " + find);
            return Valid.Replace(find, with, StringComparison.Ordinal);
        }

        private static RenderLooks Load(string text)
        {
            return RenderFactory.LoadLooks(new FakeContentSource(Utf8(text)));
        }

        private static void AssertRejects(byte[]? bytes, string what)
        {
            var source = new FakeContentSource(bytes);
            FormatException ex = Assert.Throws<FormatException>(() => RenderFactory.LoadLooks(source));
            Assert.True(ex.Message.StartsWith(Prefix, StringComparison.Ordinal), what + ": message does not start with \"" + Prefix + "\": " + ex.Message);
            Assert.Empty(source.Guard.Violations);
        }

        private static void AssertRejects(string text, string what)
        {
            AssertRejects(Utf8(text), what);
        }

        private static string Pass(int count)
        {
            var parts = new List<string>();
            for (int i = 0; i < count; i++)
            {
                parts.Add("\"#" + i.ToString("X6", System.Globalization.CultureInfo.InvariantCulture) + "\"");
            }

            return "[ " + string.Join(", ", parts) + " ]";
        }

        private static void AssertLivery(in Livery l, string fuselage, string tail, string cheatline, string engines, string logo, LogoMark mark, string what)
        {
            Assert.True(Prims.Show(l.Fuselage) == fuselage, what + " fuselage " + Prims.Show(l.Fuselage));
            Assert.True(Prims.Show(l.Tail) == tail, what + " tail " + Prims.Show(l.Tail));
            Assert.True(Prims.Show(l.Cheatline) == cheatline, what + " cheatline " + Prims.Show(l.Cheatline));
            Assert.True(Prims.Show(l.Engines) == engines, what + " engines " + Prims.Show(l.Engines));
            Assert.True(Prims.Show(l.Logo) == logo, what + " logo " + Prims.Show(l.Logo));
            Assert.True(l.Mark == mark, what + " mark " + l.Mark + ", expected " + mark);
        }

        private static List<string> Colours(IReadOnlyList<Rgb> list)
        {
            var result = new List<string>();
            foreach (Rgb c in list)
            {
                result.Add(Prims.Show(c));
            }

            return result;
        }

        [Fact]
        public void test_render_looks_load_the_file_and_reject_each_fault()
        {
            // A valid file: ReadAll once, of looks/looks.json; Files never.
            var source = new FakeContentSource(Utf8(Valid));
            RenderLooks looks = RenderFactory.LoadLooks(source);
            Assert.Equal(new[] { Path }, source.Reads);
            Assert.Empty(source.Guard.Violations);

            AssertLivery(looks.DefaultLivery, "#F4F5F7", "#2F5D9E", "#2F5D9F", "#9AA1A9", "#F4F5F8", LogoMark.None, "default_livery");

            // Airlines are sorted by AirlineId, the FNV-1a-32 of the code (11 §11.4).
            var byId = new SortedDictionary<uint, string>
            {
                { Fnv.Airline("ZZ9").Value, "ZZ9" },
                { Fnv.Airline("AB").Value, "AB" },
                { Fnv.Airline("Q").Value, "Q" },
                { Fnv.Airline("ABCDEFGH").Value, "ABCDEFGH" },
            };
            Assert.Equal(4, byId.Count);
            Assert.Equal(4, looks.Airlines.Count);
            int k = 0;
            foreach (KeyValuePair<uint, string> e in byId)
            {
                AirlineLivery a = looks.Airlines[k++];
                Assert.True(a.Airline.Value == e.Key, "Airlines[" + (k - 1) + "] is " + a.Airline.Value + ", expected " + e.Value + " = " + e.Key);
                switch (e.Value)
                {
                    case "ZZ9":
                        AssertLivery(a.Livery, "#112233", "#445566", "#778899", "#AABBCC", "#DDEEFF", LogoMark.Star, "ZZ9");
                        break;
                    case "AB":
                        AssertLivery(a.Livery, "#010203", "#040506", "#070809", "#0A0B0C", "#0D0E0F", LogoMark.Crescent, "AB");
                        break;
                    case "Q":
                        AssertLivery(a.Livery, "#FFFFFF", "#000000", "#FF0000", "#00FF00", "#0000FF", LogoMark.Disc, "Q");
                        break;
                    default:
                        AssertLivery(a.Livery, "#123456", "#654321", "#ABCDEF", "#FEDCBA", "#0F0F0F", LogoMark.Bars, "ABCDEFGH");
                        break;
                }
            }

            // The lists keep file order.
            Assert.Equal(new[] { "#300000", "#100000", "#200000" }, Colours(looks.Tops));
            Assert.Equal(new[] { "#000010" }, Colours(looks.Bottoms));
            Assert.Equal(new[] { "#C68E6B", "#8D5524" }, Colours(looks.Skins));
            Assert.Equal(new[] { "#3A2A1F" }, Colours(looks.Hairs));
            Assert.Equal(new[] { "#5A4A3A", "#000001" }, Colours(looks.Bags));

            // Every mark name, and keys in any order.
            (string Name, LogoMark Mark)[] marks =
            {
                ("none", LogoMark.None), ("disc", LogoMark.Disc), ("ring", LogoMark.Ring), ("chevron", LogoMark.Chevron),
                ("star", LogoMark.Star), ("bars", LogoMark.Bars), ("diamond", LogoMark.Diamond), ("crescent", LogoMark.Crescent),
            };
            foreach ((string name, LogoMark mark) in marks)
            {
                RenderLooks m = Load(Replace("\"mark\": \"none\"", "\"mark\": \"" + name + "\""));
                Assert.Equal(mark, m.DefaultLivery.Mark);
            }

            RenderLooks reordered = Load(Replace("\"schema_version\": 1,\n  \"id\": \"looks\",", "\"id\": \"looks\",\n  \"schema_version\": 1,"));
            Assert.Equal(4, reordered.Airlines.Count);

            // Empty liveries and 64-entry lists are valid.
            RenderLooks none = Load(Valid.Substring(0, Valid.IndexOf("\"liveries\"", StringComparison.Ordinal)) + "\"liveries\": {},\n" + Valid.Substring(Valid.IndexOf("  \"passengers\"", StringComparison.Ordinal)));
            Assert.Empty(none.Airlines);
            Assert.Equal(64, Load(Replace("[ \"#300000\", \"#100000\", \"#200000\" ]", Pass(64))).Tops.Count);

            // Each fault.
            (string Text, string What)[] faults =
            {
                (Replace("\"schema_version\": 1", "\"schema_version\": 1.0"), "a fraction"),
                (Replace("\"schema_version\": 1", "\"schema_version\": 1e0"), "an exponent"),
                (Replace("\"schema_version\": 1", "\"schema_version\": true"), "true"),
                (Replace("\"schema_version\": 1", "\"schema_version\": null"), "null"),
                (Replace("\"bags\": [ \"#5A4A3A\", \"#000001\" ]", "\"bags\": [ \"#5A4A3A\", \"#000001\", ]"), "a trailing comma"),
                (Replace("{\n  \"schema_version\"", "{\n  // comment\n  \"schema_version\""), "a comment"),
                (Valid.Substring(0, Valid.Length - 3), "truncated"),
                (Replace("\"id\": \"looks\",", "\"id\": \"looks\",\n  \"id\": \"looks\","), "a duplicate key"),
                (Replace("\"id\": \"looks\",", "\"id\": \"looks\",\n  \"extra\": 1,"), "an unknown key"),
                (Replace("\"id\": \"looks\",\n", string.Empty), "a missing id"),
                (Replace(",\n    \"bags\": [ \"#5A4A3A\", \"#000001\" ]", string.Empty), "missing bags"),
                (Replace("\"logo\": \"#F4F5F8\", ", string.Empty), "a livery without logo"),
                (Replace("\"mark\": \"none\"", "\"mark\": \"none\", \"wing\": \"#000000\""), "a livery with an unknown key"),
                (Replace("\"schema_version\": 1", "\"schema_version\": 2"), "schema_version 2"),
                (Replace("\"schema_version\": 1", "\"schema_version\": 0"), "schema_version 0"),
                (Replace("\"id\": \"looks\"", "\"id\": \"look\""), "id other than looks"),
                (Replace("\"#F4F5F7\"", "\"#f4f5f7\""), "a lower-case colour"),
                (Replace("\"#F4F5F7\"", "\"#F4F5F\""), "a five-digit colour"),
                (Replace("\"#F4F5F7\"", "\"#F4F5F7A\""), "a seven-digit colour"),
                (Replace("\"#F4F5F7\"", "\"F4F5F7\""), "a colour without #"),
                (Replace("\"#F4F5F7\"", "\"#GGGGGG\""), "a colour that is not hexadecimal"),
                (Replace("\"#F4F5F7\"", "16053495"), "a colour as an integer"),
                (Replace("\"#300000\"", "\"#30000\""), "a malformed passenger colour"),
                (Replace("\"mark\": \"none\"", "\"mark\": \"square\""), "an unknown mark"),
                (Replace("\"mark\": \"none\"", "\"mark\": \"None\""), "a mark in the wrong case"),
                (Replace("\"ZZ9\":", "\"\":"), "an empty airline code"),
                (Replace("\"ZZ9\":", "\"ABCDEFGHI\":"), "a nine-character airline code"),
                (Replace("\"ZZ9\":", "\"zz9\":"), "a lower-case airline code"),
                (Replace("\"ZZ9\":", "\"Z-9\":"), "an airline code with a hyphen"),
                (Replace("\"ZZ9\":", "\"Z 9\":"), "an airline code with a space"),
                (Replace("\"AB\":", "\"ZZ9\":"), "the same airline code twice"),
                (Replace("\"ZZ9\":", "\"NCL\":").Replace("\"AB\":", "\"WV82\":", StringComparison.Ordinal), "two codes with the same AirlineId"),
                (Replace("[ \"#300000\", \"#100000\", \"#200000\" ]", "[]"), "an empty tops list"),
                (Replace("[ \"#000010\" ]", "[ ]"), "an empty bottoms list"),
                (Replace("[ \"#5A4A3A\", \"#000001\" ]", Pass(65)), "65 bags"),
            };

            // The colliding pair really collides, and neither code alone is a fault.
            Assert.Equal(Fnv.Airline("NCL").Value, Fnv.Airline("WV82").Value);
            Assert.Equal(4, Load(Replace("\"ZZ9\":", "\"NCL\":")).Airlines.Count);
            Assert.Equal(4, Load(Replace("\"AB\":", "\"WV82\":")).Airlines.Count);

            foreach ((string text, string what) in faults)
            {
                AssertRejects(text, what);
            }

            // Bytes outside the subset: a BOM, and invalid UTF-8.
            var bom = new List<byte> { 0xEF, 0xBB, 0xBF };
            bom.AddRange(Utf8(Valid));
            AssertRejects(bom.ToArray(), "a BOM");
            byte[] bad = Utf8(Replace("\"id\": \"looks\"", "\"id\": \"lo?ks\""));
            bad[Array.IndexOf(bad, (byte)'?')] = 0xFF;
            AssertRejects(bad, "invalid UTF-8");

            // A null ReadAll result is the loader's FormatException.
            AssertRejects((byte[]?)null, "ReadAll returned null");

            // An exception ReadAll throws passes through unchanged.
            var missing = new FileNotFoundException("no looks/looks.json in StreamingAssets/Content");
            var throwing = new FakeContentSource(missing);
            Exception thrown = Assert.ThrowsAny<Exception>(() => RenderFactory.LoadLooks(throwing));
            Assert.Same(missing, thrown);

            AssertShippedFile();
        }

        /// <summary>The shipped data/looks/looks.json loads, with the four Phase 1 fixture airlines.</summary>
        private static void AssertShippedFile()
        {
            var source = new FakeContentSource(Repo.Read("data", "looks", "looks.json"));
            RenderLooks looks = RenderFactory.LoadLooks(source);
            Assert.Empty(source.Guard.Violations);

            // A livery for each Phase 1 fixture airline, with distinct fuselage, tail and mark.
            var fuselages = new HashSet<string>();
            var tails = new HashSet<string>();
            var marks = new HashSet<LogoMark>();
            foreach (string code in new[] { "BRW", "CTX", "DLN", "NVA" })
            {
                uint id = Fnv.Airline(code).Value;
                AirlineLivery? found = null;
                foreach (AirlineLivery a in looks.Airlines)
                {
                    if (a.Airline.Value == id)
                    {
                        found = a;
                    }
                }

                Assert.True(found.HasValue, "data/looks/looks.json has no livery for " + code);
                Assert.True(fuselages.Add(Prims.Show(found!.Value.Livery.Fuselage)), code + " shares its fuselage colour");
                Assert.True(tails.Add(Prims.Show(found.Value.Livery.Tail)), code + " shares its tail colour");
                Assert.True(marks.Add(found.Value.Livery.Mark), code + " shares its mark");
            }

            for (int i = 1; i < looks.Airlines.Count; i++)
            {
                Assert.True(looks.Airlines[i - 1].Airline.Value < looks.Airlines[i].Airline.Value, "Airlines not strictly ascending by AirlineId");
            }

            (string Name, IReadOnlyList<Rgb> List, int Min)[] lists =
            {
                ("tops", looks.Tops, 8), ("bottoms", looks.Bottoms, 4), ("skins", looks.Skins, 5), ("hairs", looks.Hairs, 5), ("bags", looks.Bags, 4),
            };
            foreach ((string name, IReadOnlyList<Rgb> list, int min) in lists)
            {
                Assert.True(list.Count >= min && list.Count <= 64, name + ": " + list.Count + " entries, expected " + min + " to 64");
            }
        }

        [Fact]
        public void test_render_looks_defaults_match_spec()
        {
            RenderLooks d = RenderFactory.DefaultLooks();
            AssertLivery(d.DefaultLivery, "#F4F5F7", "#2F5D9E", "#2F5D9E", "#9AA1A9", "#F4F5F7", LogoMark.None, "DefaultLooks livery");
            Assert.Empty(d.Airlines);
            Assert.Equal(new[] { "#3B6EA5" }, Colours(d.Tops));
            Assert.Equal(new[] { "#2E3440" }, Colours(d.Bottoms));
            Assert.Equal(new[] { "#C68E6B" }, Colours(d.Skins));
            Assert.Equal(new[] { "#3A2A1F" }, Colours(d.Hairs));
            Assert.Equal(new[] { "#5A4A3A" }, Colours(d.Bags));

            // A fresh value per call: no list is shared between two calls.
            RenderLooks e = RenderFactory.DefaultLooks();
            Assert.False(ReferenceEquals(d.Tops, e.Tops), "DefaultLooks() returned the same Tops list twice");
            Assert.False(ReferenceEquals(d.Bottoms, e.Bottoms), "DefaultLooks() returned the same Bottoms list twice");
            Assert.False(ReferenceEquals(d.Skins, e.Skins), "DefaultLooks() returned the same Skins list twice");
            Assert.False(ReferenceEquals(d.Hairs, e.Hairs), "DefaultLooks() returned the same Hairs list twice");
            Assert.False(ReferenceEquals(d.Bags, e.Bags), "DefaultLooks() returned the same Bags list twice");
            AssertLivery(e.DefaultLivery, "#F4F5F7", "#2F5D9E", "#2F5D9E", "#9AA1A9", "#F4F5F7", LogoMark.None, "second DefaultLooks livery");
        }
    }
}
