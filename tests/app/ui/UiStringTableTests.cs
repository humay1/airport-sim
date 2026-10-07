using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// 17-interfaces-ui.md §17.4b (Q-125), written from the spec only: the
    /// LocalisedKey, the twelve Phase 1 keys and their English text,
    /// data/strings/en.json, UiFactory.LoadStringTable over 08 §8.11's
    /// IContentSource and strict JSON subset (Q-033 string rules included),
    /// IStringTable.Resolve, and §17.9's string-table budget. 17 §17.10 names
    /// the done-condition tests.
    ///
    /// The table is loaded once at scene start and is never saved, so it has
    /// no save/load round trip and no headless day of its own. Its
    /// determinism is that the same bytes give the same texts, and that two
    /// tables never share state.
    /// </summary>
    public sealed class UiStringTableTests
    {
        private const string EnPath = "strings/en.json";
        private const string Prefix = "strings/en.json: ";

        /// <summary>§17.4b's table of keys and English text, in its order.</summary>
        internal static readonly (string Key, string Text)[] English =
        {
            ("ui.settings.title", "Graphics settings"),
            ("ui.settings.preset.low", "Low"),
            ("ui.settings.preset.medium", "Medium"),
            ("ui.settings.preset.high", "High"),
            ("ui.settings.knob.draw_agents", "Show passengers"),
            ("ui.settings.knob.max_drawn_agents_per_node", "Max passengers drawn per area"),
            ("ui.settings.knob.frame_rate_cap", "Frame rate cap (fps)"),
            ("ui.settings.knob.resolution_scale_percent", "Resolution scale (%)"),
            ("ui.settings.knob.anti_aliasing", "Anti-aliasing"),
            ("ui.settings.value.on", "On"),
            ("ui.settings.value.off", "Off"),
            ("ui.settings.value.uncapped", "Uncapped"),
        };

        private static string Q(string rawJsonText)
        {
            return "\"" + rawJsonText + "\"";
        }

        private static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        /// <summary>The document with the one <c>@@</c> in its text replaced by <paramref name="raw"/> bytes.</summary>
        private static byte[] Splice(EnDoc doc, params byte[] raw)
        {
            string[] parts = doc.Render().Split(new[] { "@@" }, StringSplitOptions.None);
            Assert.True(parts.Length == 2, "test bug: the document must hold exactly one @@ marker");
            return Utf8(parts[0]).Concat(raw).Concat(Utf8(parts[1])).ToArray();
        }

        private static string Texts(IStringTable table)
        {
            return string.Join(" | ", English.Select(e => e.Key + "=" + table.Resolve(new LocalisedKey(e.Key))));
        }

        /// <summary>Every key resolves to <paramref name="expected"/>'s text; mismatches are listed.</summary>
        private static void AssertTexts(IStringTable table, IReadOnlyList<string> expected, string what)
        {
            var wrong = new List<string>();
            for (int i = 0; i < English.Length; i++)
            {
                string actual = table.Resolve(new LocalisedKey(English[i].Key));
                if (!string.Equals(actual, expected[i], StringComparison.Ordinal))
                {
                    wrong.Add(English[i].Key + ": expected \"" + expected[i] + "\", got " + (actual == null ? "null" : "\"" + actual + "\""));
                }
            }

            Assert.True(wrong.Count == 0, what + ":\n " + string.Join("\n ", wrong));
        }

        private static string[] EnglishTexts()
        {
            return English.Select(e => e.Text).ToArray();
        }

        // ------------------------------------------------------------ the repository file

        [Fact]
        public void test_ui_string_table_loads_repository_en_file()
        {
            var source = new DataSource();
            Assert.True(File.Exists(source.PathOf(EnPath)), "data/strings/en.json does not exist (17 §17.4b)");

            IStringTable table = UiFactory.LoadStringTable(source);
            Assert.NotNull(table);
            AssertTexts(table, EnglishTexts(), "data/strings/en.json against 17 §17.4b's table");
        }

        // ------------------------------------------------------------ what it reads

        [Fact]
        public void test_ui_string_table_reads_only_strings_en_json()
        {
            byte[] bytes = EnDoc.Valid().Bytes();
            StringsSource source = StringsSource.Of(bytes);
            IStringTable table = UiFactory.LoadStringTable(source);

            Assert.True(source.Reads.Count == 1 && source.Reads[0] == EnPath, "LoadStringTable must call ReadAll(\"strings/en.json\") exactly once, got [" + string.Join(", ", source.Reads) + "]");
            Assert.True(source.FilesCalls == 0, "LoadStringTable called Files() " + source.FilesCalls + " times");

            // The table never changes after loading: it does not read again,
            // and it does not keep the caller's bytes.
            Array.Clear(bytes, 0, bytes.Length);
            for (int pass = 0; pass < 3; pass++)
            {
                AssertTexts(table, EnglishTexts(), "pass " + pass + " after the source's bytes were cleared");
            }

            Assert.True(source.Reads.Count == 1 && source.FilesCalls == 0, "Resolve touched the source: " + source.Reads.Count + " reads, " + source.FilesCalls + " Files() calls");
        }

        [Fact]
        public void test_ui_string_table_read_all_exception_passes_through()
        {
            // 17 §17.4b: an exception thrown by ReadAll passes through unchanged,
            // a FormatException included (not re-wrapped with the path).
            Exception[] thrown =
            {
                new IOException("disk gone"),
                new InvalidOperationException("source closed"),
                new FormatException("the source's own message"),
                new ArgumentException("no such path", "path"),
            };

            foreach (Exception e in thrown)
            {
                StringsSource source = StringsSource.Throwing(e);
                Exception? caught = Record.Exception(() => UiFactory.LoadStringTable(source));
                Assert.True(ReferenceEquals(caught, e), e.GetType().Name + " from ReadAll did not pass through unchanged, got " + (caught == null ? "no exception" : caught.GetType().Name + ": " + caught.Message));
                Assert.True(source.Reads.Count == 1 && source.FilesCalls == 0, e.GetType().Name + ": " + source.Reads.Count + " reads, " + source.FilesCalls + " Files() calls");
            }
        }

        // ------------------------------------------------------------ accepted files

        [Fact]
        public void test_ui_string_table_accepts_valid_variants_and_returns_file_text()
        {
            var variants = new List<(string What, byte[] Bytes, string[] Expected)>();
            string[] english = EnglishTexts();

            variants.Add(("the table's order", EnDoc.Valid().Bytes(), english));
            variants.Add(("reversed members, no whitespace", EnDoc.Valid().Reversed().Bytes("", ":"), english));
            variants.Add(("CR LF, tabs and spaces around the colons", EnDoc.Valid().Bytes("\r\n\t ", " \t: "), english));

            // Resolve returns the file's text, not a built-in English one.
            string[] numbered = English.Select((e, i) => "Text " + i).ToArray();
            variants.Add(("other texts", EnDoc.WithTexts(i => numbered[i]).Bytes(), numbered));

            // The text range is U+0020 to U+007E, both ends included.
            var printable = new StringBuilder();
            var printableJson = new StringBuilder();
            for (char c = ' '; c <= '~'; c++)
            {
                printable.Append(c);
                printableJson.Append(c == '"' || c == '\\' ? "\\" + c : c.ToString());
            }

            string[] range = (string[])english.Clone();
            range[0] = printable.ToString();
            range[1] = " ";
            range[2] = "~";
            variants.Add((
                "every character U+0020 to U+007E, a lone space and a lone tilde",
                EnDoc.Valid().Text(English[0].Key, Q(printableJson.ToString())).Text(English[1].Key, Q(" ")).Text(English[2].Key, Q("~")).Bytes(),
                range));

            // Escapes decode first, then the range applies to the decoded text.
            string[] escaped = (string[])english.Clone();
            escaped[1] = "A\"B\\C/D";
            variants.Add((
                "escapes that decode to printable ASCII",
                EnDoc.Valid()
                    .Text(English[0].Key, Q("\\u0047raphics\\u0020settings"))
                    .Text(English[1].Key, Q("A\\\"B\\\\C\\/D"))
                    .Text(English[2].Key, Q("\\u004D\\u0065dium"))
                    .Text(English[3].Key, Q("\\u0048\\u0069\\u0067\\u0068"))
                    .Bytes(),
                escaped));

            // Keys compare ordinally after unescaping (Q-033), top level and in strings.
            variants.Add((
                "escaped keys and an escaped id",
                EnDoc.Valid()
                    .RenameText(English[0].Key, Q("ui.settings.titl\\u0065"))
                    .RenameText(English[11].Key, Q("ui\\u002esettings.value.uncapped"))
                    .Top("id", Q("e\\u006E"))
                    .RenameTop("id", Q("\\u0069d"))
                    .RenameTop("schema_version", Q("schema\\u005Fversion"))
                    .Bytes(),
                english));

            foreach ((string what, byte[] bytes, string[] expected) in variants)
            {
                IStringTable table;
                try
                {
                    table = UiFactory.LoadStringTable(StringsSource.Of(bytes));
                }
                catch (Exception e)
                {
                    Assert.Fail(what + ": a valid file failed to load: " + e.GetType().Name + ": " + e.Message + "\n" + Encoding.UTF8.GetString(bytes));
                    return;
                }

                AssertTexts(table, expected, what);
            }

            // The same bytes give the same texts, and two tables share nothing.
            byte[] a = EnDoc.WithTexts(i => "A" + i).Bytes();
            byte[] b = EnDoc.WithTexts(i => "B" + i).Bytes();
            IStringTable first = UiFactory.LoadStringTable(StringsSource.Of(a));
            IStringTable second = UiFactory.LoadStringTable(StringsSource.Of(b));
            IStringTable again = UiFactory.LoadStringTable(StringsSource.Of(a));
            AssertTexts(first, English.Select((e, i) => "A" + i).ToArray(), "the first table after a second was loaded");
            AssertTexts(second, English.Select((e, i) => "B" + i).ToArray(), "the second table");
            Assert.Equal(Texts(first), Texts(again));
        }

        // ------------------------------------------------------------ rejected files

        [Fact]
        public void test_ui_string_table_rejects_malformed_file()
        {
            // Control: the baseline every case is built from loads.
            AssertTexts(UiFactory.LoadStringTable(StringsSource.Of(EnDoc.Valid().Bytes())), EnglishTexts(), "the baseline document");

            // A null source.
            ArgumentNullException nullSource = Assert.Throws<ArgumentNullException>(() => UiFactory.LoadStringTable(null!));
            Assert.Equal("source", nullSource.ParamName);

            var cases = new List<(string What, byte[]? Bytes)>();
            void Add(string what, EnDoc doc)
            {
                cases.Add((what, doc.Bytes()));
            }

            string valid = EnDoc.Valid().Render();
            string title = English[0].Key;

            // A null result from ReadAll.
            cases.Add(("ReadAll returned null", null));

            // Bytes outside the strict subset (08 §8.11, Q-033).
            cases.Add(("a UTF-8 BOM", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8(valid)).ToArray()));
            cases.Add(("an empty file", Array.Empty<byte>()));
            cases.Add(("whitespace only", Utf8(" \n\t")));
            cases.Add(("a truncated document", Utf8(valid.Substring(0, valid.Length - 1))));
            cases.Add(("a trailing comma in strings", Utf8(valid.Replace(Q("Uncapped") + "\n}", Q("Uncapped") + ",\n}"))));
            cases.Add(("a trailing comma at the top level", Utf8(valid.Substring(0, valid.Length - 2) + ",\n}")));
            cases.Add(("a line comment", Utf8("// strings\n" + valid)));
            cases.Add(("a block comment", Utf8(valid.Replace(Q("id"), "/* c */ " + Q("id")))));
            cases.Add(("a second value after the object", Utf8(valid + "\n{}")));
            cases.Add(("a trailing character", Utf8(valid + "x")));
            cases.Add(("single-quoted key", Utf8(valid.Replace(Q("id"), "'id'"))));
            Add("an unquoted key", EnDoc.Valid().RenameTop("id", "id"));
            cases.Add(("the document is an array", Utf8("[" + valid + "]")));
            cases.Add(("the document is a string", Utf8(Q("en"))));
            foreach (string n in new[] { "1.0", "1e0", "1E0", "01", "+1", "0x1", "NaN", "null", "1.", ".1" })
            {
                Add("schema_version " + n, EnDoc.Valid().Top("schema_version", n));
            }

            Add("strings null", EnDoc.Valid().Top("strings", "null"));
            Add("a text null", EnDoc.Valid().Text(title, "null"));
            foreach (byte[] bad in new[]
            {
                new byte[] { 0xFF },
                new byte[] { 0xC3 },
                new byte[] { 0xC0, 0xAF },
                new byte[] { 0xED, 0xA0, 0x80 },
                new byte[] { 0xF4, 0x90, 0x80, 0x80 },
                new byte[] { 0x80 },
            })
            {
                string hex = string.Join(" ", bad.Select(x => x.ToString("X2")));
                cases.Add(("invalid UTF-8 " + hex + " in a text", Splice(EnDoc.Valid().Text(title, Q("Graphics @@settings")), bad)));
                cases.Add(("invalid UTF-8 " + hex + " in a key", Splice(EnDoc.Valid().RenameText(title, Q("ui.settings.ti@@tle")), bad)));
            }

            foreach (byte control in new byte[] { 0x00, 0x09, 0x0A, 0x0D, 0x1F })
            {
                cases.Add(("raw control 0x" + control.ToString("X2") + " in a text", Splice(EnDoc.Valid().Text(title, Q("Graphics@@settings")), control)));
            }

            foreach (string escape in new[] { "\\x41", "\\u00", "\\u00g1", "\\uD800", "\\udfff", "\\uDBFF\\uDFFF", "\\'", "\\a", "\\U0041", "\\0", "\\" })
            {
                Add("the escape " + escape + " in a text", EnDoc.Valid().Text(title, Q("Graphics settings" + escape)));
            }

            // Duplicate, unknown or missing keys at the top level.
            Add("a duplicate id", EnDoc.Valid().PlusTop(Q("id"), Q("en")));
            Add("a duplicate schema_version", EnDoc.Valid().PlusTop(Q("schema_version"), "1"));
            Add("a duplicate strings", EnDoc.Valid().PlusTop(Q("strings"), null));
            Add("a duplicate id after unescaping", EnDoc.Valid().PlusTop(Q("i\\u0064"), Q("en")));
            Add("an unknown top-level key", EnDoc.Valid().PlusTop(Q("lang"), Q("en")));
            Add("an unknown top-level key differing in case", EnDoc.Valid().PlusTop(Q("Id"), Q("en")));
            Add("an empty top-level key", EnDoc.Valid().PlusTop(Q(""), "1"));
            Add("a top-level key with a trailing space", EnDoc.Valid().PlusTop(Q("schema_version "), "1"));
            foreach (string top in new[] { "schema_version", "id", "strings" })
            {
                Add("no " + top, EnDoc.Valid().NoTop(top));
                Add(top + " in upper case", EnDoc.Valid().RenameTop(top, Q(top.ToUpperInvariant())));
            }

            cases.Add(("an empty object", Utf8("{}")));

            // Duplicate, unknown or missing keys in strings.
            foreach ((string key, string text) in English)
            {
                Add("a duplicate " + key, EnDoc.Valid().PlusText(Q(key), Q(text)));
                Add("no " + key, EnDoc.Valid().NoText(key));
                Add(key + " in upper case", EnDoc.Valid().RenameText(key, Q(key.ToUpperInvariant())));
            }

            Add("a duplicate key with another text", EnDoc.Valid().PlusText(Q(title), Q("Settings")));
            Add("a duplicate key after unescaping", EnDoc.Valid().PlusText(Q("ui.settings.titl\\u0065"), Q("Graphics settings")));
            foreach (string unknown in new[] { "ui.settings.preset.custom", "ui.settings.extra", "ui.settings.title ", "ui.settings.value.uncapped.x", "ui.settings", "" })
            {
                Add("the unknown key \"" + unknown + "\" in strings", EnDoc.Valid().PlusText(Q(unknown), Q("Text")));
            }

            Add("strings empty", EnDoc.Valid().Top("strings", "{}"));

            // schema_version other than 1, and id other than "en".
            foreach (string v in new[] { "0", "2", "-1", "-0", "10", "4294967297", "18446744073709551617", Q("1"), "true", "false", "[1]", "{}" })
            {
                Add("schema_version " + v, EnDoc.Valid().Top("schema_version", v));
            }

            foreach (string v in new[] { Q("EN"), Q("En"), Q("en-GB"), Q("en_GB"), Q(""), Q("fr"), Q("en "), Q(" en"), "1", "true", "[" + Q("en") + "]", "{}" })
            {
                Add("id " + v, EnDoc.Valid().Top("id", v));
            }

            // strings not an object.
            foreach (string v in new[] { "[]", Q("Graphics settings"), "1", "true", "false", "[" + Q(title) + "]" })
            {
                Add("strings " + v, EnDoc.Valid().Top("strings", v));
            }

            // A text that is not a string, is empty, or holds a character outside U+0020 to U+007E.
            string[] notStrings = { "1", "0", "-1", "true", "false", "[]", "{}", "[" + Q("Low") + "]", "{" + Q("text") + ": " + Q("Low") + "}" };
            for (int i = 0; i < notStrings.Length; i++)
            {
                string key = English[i % English.Length].Key;
                Add(key + " " + notStrings[i], EnDoc.Valid().Text(key, notStrings[i]));
            }

            foreach ((string key, string _) in English)
            {
                Add(key + " empty", EnDoc.Valid().Text(key, Q("")));
            }

            foreach (string escape in new[] { "\\u00e9", "\\u00E9", "\\u007F", "\\u007f", "\\u001F", "\\u0000", "\\t", "\\n", "\\r", "\\b", "\\f", "\\u00A0", "\\uFFFD", "\\u2014" })
            {
                Add("the escaped character " + escape + " in a text", EnDoc.Valid().Text(English[7].Key, Q("Resolution " + escape + " scale")));
                Add("a text that is only " + escape, EnDoc.Valid().Text(English[9].Key, Q(escape)));
            }

            foreach (byte[] raw in new[]
            {
                new byte[] { 0x7F },
                new byte[] { 0xC3, 0xA9 },
                new byte[] { 0xC2, 0xA0 },
                new byte[] { 0xC2, 0x80 },
                new byte[] { 0xE2, 0x80, 0x94 },
                new byte[] { 0xEF, 0xBB, 0xBF },
                new byte[] { 0xF0, 0x9F, 0x98, 0x80 },
            })
            {
                string hex = string.Join(" ", raw.Select(x => x.ToString("X2")));
                cases.Add(("the raw character " + hex + " in a text", Splice(EnDoc.Valid().Text(English[4].Key, Q("Show @@passengers")), raw)));
            }

            var wrong = new List<string>();
            foreach ((string what, byte[]? bytes) in cases)
            {
                StringsSource source = StringsSource.Of(bytes);
                Exception? e = Record.Exception(() => UiFactory.LoadStringTable(source));
                if (e == null)
                {
                    wrong.Add(what + ": loaded");
                }
                else if (e.GetType() != typeof(FormatException))
                {
                    wrong.Add(what + ": " + e.GetType().Name + " instead of FormatException: " + e.Message);
                }
                else if (!e.Message.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    wrong.Add(what + ": the message does not start with \"" + Prefix + "\": " + e.Message);
                }
            }

            Assert.True(wrong.Count == 0, wrong.Count + " of " + cases.Count + " malformed files were not a FormatException starting \"" + Prefix + "\" (17 §17.4b):\n " + string.Join("\n ", wrong));
        }

        // ------------------------------------------------------------ Resolve

        [Fact]
        public void test_ui_string_table_resolve_rejects_unknown_key()
        {
            IStringTable table = UiFactory.LoadStringTable(StringsSource.Of(EnDoc.Valid().Bytes()));
            string before = Texts(table);

            var bad = new List<(string What, LocalisedKey Key)>
            {
                ("default(LocalisedKey)", default),
                ("new LocalisedKey(null)", new LocalisedKey(null!)),
                ("an empty Value", new LocalisedKey("")),
                ("ui.settings", new LocalisedKey("ui.settings")),
                ("ui.settings.preset.custom", new LocalisedKey("ui.settings.preset.custom")),
                ("ui.settings.title.x", new LocalisedKey("ui.settings.title.x")),
                ("ui.settings.titl", new LocalisedKey("ui.settings.titl")),
                ("a trailing space", new LocalisedKey("ui.settings.title ")),
                ("a leading space", new LocalisedKey(" ui.settings.title")),
                ("a trailing dot", new LocalisedKey("ui.settings.title.")),
                ("a hyphen for an underscore", new LocalisedKey("ui.settings.knob.draw-agents")),
                ("a Cyrillic e", new LocalisedKey("ui.settings.titlе")),
                ("a text instead of a key", new LocalisedKey("Graphics settings")),
                ("UI.Settings.Title", new LocalisedKey("UI.Settings.Title")),
                ("ui.settings.Title", new LocalisedKey("ui.settings.Title")),
            };

            // A key differing only in case, for every key.
            foreach ((string key, string _) in English)
            {
                bad.Add((key.ToUpperInvariant(), new LocalisedKey(key.ToUpperInvariant())));
                bad.Add(("capitalised " + key, new LocalisedKey(char.ToUpperInvariant(key[0]) + key.Substring(1))));
                bad.Add(("last letter upper " + key, new LocalisedKey(key.Substring(0, key.Length - 1) + char.ToUpperInvariant(key[key.Length - 1]))));
            }

            var wrong = new List<string>();
            foreach ((string what, LocalisedKey key) in bad)
            {
                Exception? e = Record.Exception(() => table.Resolve(key));
                if (e == null)
                {
                    wrong.Add(what + ": resolved");
                }
                else if (e.GetType() != typeof(ArgumentException))
                {
                    wrong.Add(what + ": " + e.GetType().Name + " instead of ArgumentException");
                }
                else if (((ArgumentException)e).ParamName != "key")
                {
                    wrong.Add(what + ": ParamName " + ((ArgumentException)e).ParamName + " instead of key");
                }
            }

            Assert.True(wrong.Count == 0, "17 §17.4b: Resolve of a key not among the twelve throws ArgumentException (key):\n " + string.Join("\n ", wrong));

            // Nothing changed, and keys compare by value, not by reference.
            Assert.Equal(before, Texts(table));
            foreach ((string key, string text) in English)
            {
                string copy = new string(key.ToCharArray());
                Assert.False(ReferenceEquals(copy, key));
                Assert.Equal(text, table.Resolve(new LocalisedKey(copy)));
                Assert.Equal(text, table.Resolve(new LocalisedKey(new StringBuilder(key).ToString())));
            }
        }

        [Fact]
        public void test_ui_localised_key_matches_spec()
        {
            // 17 §17.4b/§17.7: readonly struct LocalisedKey { string Value },
            // built with new LocalisedKey(value), compared ordinally.
            Type t = typeof(LocalisedKey);
            Assert.True(t.IsValueType && !t.IsEnum, "LocalisedKey is not a struct");
            Assert.True(t.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"), "LocalisedKey is not a readonly struct");
            Assert.NotNull(t.GetConstructor(new[] { typeof(string) }));

            MemberInfo[] value = t.GetMember("Value", BindingFlags.Public | BindingFlags.Instance);
            Assert.True(value.Length == 1, "LocalisedKey must have one public instance member Value");
            Type? valueType = value[0] is PropertyInfo p ? p.PropertyType : value[0] is FieldInfo f ? f.FieldType : null;
            Assert.True(valueType == typeof(string), "LocalisedKey.Value is not a string");

            foreach ((string key, string _) in English)
            {
                Assert.Equal(key, new LocalisedKey(key).Value);
                Assert.True(new LocalisedKey(key).Equals(new LocalisedKey(new string(key.ToCharArray()))), key + ": equal Values must compare equal");
                Assert.False(new LocalisedKey(key).Equals(new LocalisedKey(key.ToUpperInvariant())), key + ": comparison must be ordinal, not case-insensitive");
            }

            Assert.Null(default(LocalisedKey).Value);
        }

        // ------------------------------------------------------------ budget (§17.9)

        [Fact]
        [Trait("Category", "Budget")]
        public void test_ui_string_table_resolve_allocates_nothing()
        {
            IStringTable table = UiFactory.LoadStringTable(StringsSource.Of(EnDoc.Valid().Bytes()));
            LocalisedKey[] keys = English.Select(e => new LocalisedKey(e.Key)).ToArray();
            string[] first = keys.Select(k => table.Resolve(k)).ToArray();
            AssertTexts(table, EnglishTexts(), "the table under test");

            long length = 0;
            int same = 0;
            long start = Allocation.Start();
            for (int round = 0; round < 1000; round++)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    string s = table.Resolve(keys[i]);
                    length += s.Length;
                    same += ReferenceEquals(s, first[i]) ? 1 : 0;
                }
            }

            long allocated = Allocation.Since(start);
            Assert.True(allocated == 0, "Resolve allocated " + allocated + " bytes over 12000 calls (17 §17.9)");
            Assert.True(same == 1000 * keys.Length, "Resolve must return the string it loaded, the same instance every call: " + same + " of " + (1000 * keys.Length));
            Assert.Equal(1000L * EnglishTexts().Sum(s => (long)s.Length), length);
        }
    }

    /// <summary>
    /// An en.json built member by member. Each member is its raw JSON key,
    /// quotes included, and its raw JSON value. A null top-level value stands
    /// for the strings object built from the strings members.
    /// </summary>
    internal sealed class EnDoc
    {
        private readonly List<(string Key, string? Value)> _top = new List<(string Key, string? Value)>();
        private readonly List<(string Key, string Value)> _strings = new List<(string Key, string Value)>();

        public static EnDoc Valid()
        {
            return WithTexts(i => UiStringTableTests.English[i].Text);
        }

        /// <summary>The valid document with text i written as the raw JSON string content <paramref name="rawText"/>(i).</summary>
        public static EnDoc WithTexts(Func<int, string> rawText)
        {
            var doc = new EnDoc();
            doc._top.Add((Q("schema_version"), "1"));
            doc._top.Add((Q("id"), Q("en")));
            doc._top.Add((Q("strings"), null));
            for (int i = 0; i < UiStringTableTests.English.Length; i++)
            {
                doc._strings.Add((Q(UiStringTableTests.English[i].Key), Q(rawText(i))));
            }

            return doc;
        }

        private static string Q(string s)
        {
            return "\"" + s + "\"";
        }

        private static int Find<T>(List<(string Key, T Value)> members, string rawKey)
        {
            int i = members.FindIndex(m => m.Key == rawKey);
            Assert.True(i >= 0, "test bug: no member " + rawKey);
            return i;
        }

        public EnDoc Top(string name, string? rawValue)
        {
            int i = Find(_top, Q(name));
            _top[i] = (_top[i].Key, rawValue);
            return this;
        }

        public EnDoc RenameTop(string name, string rawKey)
        {
            int i = Find(_top, Q(name));
            _top[i] = (rawKey, _top[i].Value);
            return this;
        }

        public EnDoc NoTop(string name)
        {
            _top.RemoveAt(Find(_top, Q(name)));
            return this;
        }

        public EnDoc PlusTop(string rawKey, string? rawValue)
        {
            _top.Add((rawKey, rawValue));
            return this;
        }

        public EnDoc Text(string key, string rawValue)
        {
            int i = Find(_strings, Q(key));
            _strings[i] = (_strings[i].Key, rawValue);
            return this;
        }

        public EnDoc RenameText(string key, string rawKey)
        {
            int i = Find(_strings, Q(key));
            _strings[i] = (rawKey, _strings[i].Value);
            return this;
        }

        public EnDoc NoText(string key)
        {
            _strings.RemoveAt(Find(_strings, Q(key)));
            return this;
        }

        public EnDoc PlusText(string rawKey, string rawValue)
        {
            _strings.Add((rawKey, rawValue));
            return this;
        }

        public EnDoc Reversed()
        {
            _top.Reverse();
            _strings.Reverse();
            return this;
        }

        public string Render(string sep = "\n", string colon = ": ")
        {
            string strings = "{" + sep + string.Join("," + sep, _strings.Select(m => m.Key + colon + m.Value)) + sep + "}";
            return "{" + sep + string.Join("," + sep, _top.Select(m => m.Key + colon + (m.Value ?? strings))) + sep + "}";
        }

        /// <summary>UTF-8 without a BOM.</summary>
        public byte[] Bytes(string sep = "\n", string colon = ": ")
        {
            return Encoding.UTF8.GetBytes(Render(sep, colon));
        }
    }

    /// <summary>A fake 08 §8.11 IContentSource that records every call.</summary>
    internal sealed class StringsSource : IContentSource
    {
        private readonly byte[]? _bytes;
        private readonly Exception? _throw;

        private StringsSource(byte[]? bytes, Exception? toThrow)
        {
            _bytes = bytes;
            _throw = toThrow;
        }

        public List<string> Reads { get; } = new List<string>();

        public int FilesCalls { get; private set; }

        /// <summary>ReadAll returns <paramref name="bytes"/>, null included, for any path.</summary>
        public static StringsSource Of(byte[]? bytes)
        {
            return new StringsSource(bytes, null);
        }

        public static StringsSource Throwing(Exception e)
        {
            return new StringsSource(null, e);
        }

        public IReadOnlyList<string> Files()
        {
            FilesCalls++;
            return new[] { "strings/en.json" };
        }

        public byte[] ReadAll(string path)
        {
            Reads.Add(path);
            if (_throw != null)
            {
                throw _throw;
            }

            return _bytes!;
        }
    }

    /// <summary>An 08 §8.11 IContentSource over the repository's data/ directory (07 "Fixture location").</summary>
    internal sealed class DataSource : IContentSource
    {
        private readonly string _root;

        public DataSource()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "AirportSim.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            Assert.True(dir != null, "no ancestor of " + AppContext.BaseDirectory + " contains AirportSim.sln");
            _root = Path.Combine(dir!, "data");
        }

        public string PathOf(string path)
        {
            return Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        }

        public IReadOnlyList<string> Files()
        {
            return Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(_root, f).Replace(Path.DirectorySeparatorChar, '/'))
                .ToList();
        }

        public byte[] ReadAll(string path)
        {
            return File.ReadAllBytes(PathOf(path));
        }
    }
}
