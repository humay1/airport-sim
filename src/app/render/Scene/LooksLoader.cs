using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.App.Render
{
    /// <summary>
    /// Reads <c>looks/looks.json</c> (15 §15.16, Q-130) with the scene layer's own reader: the strict JSON subset and
    /// string rules of 08 §8.11 (Q-033), objects, arrays, strings and integers only. Every fault is a
    /// <see cref="FormatException"/> whose message starts with the file's path.
    /// </summary>
    internal static class LooksLoader
    {
        private const string Path = "looks/looks.json";
        private const int MaxPassengerColours = 64;

        private static readonly string[] RootKeys = { "schema_version", "id", "default_livery", "liveries", "passengers" };
        private static readonly string[] LiveryKeys = { "fuselage", "tail", "cheatline", "engines", "logo", "mark" };
        private static readonly string[] PassengerKeys = { "tops", "bottoms", "skins", "hairs", "bags" };
        private static readonly string[] MarkNames = { "none", "disc", "ring", "chevron", "star", "bars", "diamond", "crescent" };

        public static RenderLooks Load(IContentSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            byte[] bytes = source.ReadAll(Path);
            if (bytes == null)
            {
                throw Invalid("the source returned no bytes");
            }

            Node root = Parse(bytes);
            Node[] top = Members(root, "the file", RootKeys);
            if (!(top[0] is IntNode version) || version.Value != 1)
            {
                throw Invalid("schema_version must be 1");
            }

            if (!(top[1] is StringNode id) || id.Value != "looks")
            {
                throw Invalid("id must be \"looks\"");
            }

            Livery defaultLivery = ReadLivery(top[2], "default_livery");
            IReadOnlyList<AirlineLivery> airlines = ReadAirlines(top[3]);

            Node[] pax = Members(top[4], "passengers", PassengerKeys);
            return new RenderLooks(
                defaultLivery,
                airlines,
                ReadColours(pax[0], "tops"),
                ReadColours(pax[1], "bottoms"),
                ReadColours(pax[2], "skins"),
                ReadColours(pax[3], "hairs"),
                ReadColours(pax[4], "bags"));
        }

        /// <summary>The defaults of 15 §15.16: a fresh value per call.</summary>
        public static RenderLooks Defaults()
        {
            var livery = new Livery(
                new Rgb(0xF4, 0xF5, 0xF7),
                new Rgb(0x2F, 0x5D, 0x9E),
                new Rgb(0x2F, 0x5D, 0x9E),
                new Rgb(0x9A, 0xA1, 0xA9),
                new Rgb(0xF4, 0xF5, 0xF7),
                LogoMark.None);
            return new RenderLooks(
                livery,
                new AirlineLivery[0],
                new[] { new Rgb(0x3B, 0x6E, 0xA5) },
                new[] { new Rgb(0x2E, 0x34, 0x40) },
                new[] { new Rgb(0xC6, 0x8E, 0x6B) },
                new[] { new Rgb(0x3A, 0x2A, 0x1F) },
                new[] { new Rgb(0x5A, 0x4A, 0x3A) });
        }

        private static FormatException Invalid(string what)
        {
            return new FormatException(Path + ": " + what);
        }

        private static Livery ReadLivery(Node node, string what)
        {
            Node[] m = Members(node, what, LiveryKeys);
            string markName = (m[5] as StringNode)?.Value ?? throw Invalid(what + ".mark must be a string");
            int mark = Array.IndexOf(MarkNames, markName);
            if (mark < 0)
            {
                throw Invalid(what + ".mark \"" + markName + "\" is not a mark");
            }

            return new Livery(
                ReadColour(m[0], what + ".fuselage"),
                ReadColour(m[1], what + ".tail"),
                ReadColour(m[2], what + ".cheatline"),
                ReadColour(m[3], what + ".engines"),
                ReadColour(m[4], what + ".logo"),
                (LogoMark)mark);
        }

        private static IReadOnlyList<AirlineLivery> ReadAirlines(Node node)
        {
            if (!(node is ObjectNode o))
            {
                throw Invalid("liveries must be an object");
            }

            var list = new List<AirlineLivery>();
            for (int i = 0; i < o.Keys.Count; i++)
            {
                string code = o.Keys[i];
                if (!IsAirlineCode(code))
                {
                    throw Invalid("liveries: \"" + code + "\" is not an airline code of 1 to 8 characters A-Z and 0-9");
                }

                list.Add(new AirlineLivery(new AirlineId(Fnv1a32(code)), ReadLivery(o.Values[i], "liveries." + code)));
            }

            list.Sort((a, b) => a.Airline.Value.CompareTo(b.Airline.Value));
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].Airline.Value == list[i - 1].Airline.Value)
                {
                    throw Invalid("liveries: two airline codes have the airline id " + list[i].Airline.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            return list.ToArray();
        }

        private static bool IsAirlineCode(string code)
        {
            if (code.Length < 1 || code.Length > 8)
            {
                return false;
            }

            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                {
                    return false;
                }
            }

            return true;
        }

        // FNV-1a-32 over the code's UTF-8 bytes, which are its characters (11 §11.4).
        private static uint Fnv1a32(string code)
        {
            uint h = 0x811C9DC5U;
            for (int i = 0; i < code.Length; i++)
            {
                h ^= code[i];
                unchecked
                {
                    h *= 0x01000193U;
                }
            }

            return h;
        }

        private static IReadOnlyList<Rgb> ReadColours(Node node, string what)
        {
            if (!(node is ArrayNode a))
            {
                throw Invalid("passengers." + what + " must be an array");
            }

            if (a.Items.Count < 1 || a.Items.Count > MaxPassengerColours)
            {
                throw Invalid("passengers." + what + " must have 1 to " + MaxPassengerColours.ToString(CultureInfo.InvariantCulture) + " colours");
            }

            var list = new Rgb[a.Items.Count];
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = ReadColour(a.Items[i], "passengers." + what);
            }

            return list;
        }

        // "#RRGGBB", upper-case hexadecimal.
        private static Rgb ReadColour(Node node, string what)
        {
            if (!(node is StringNode s) || s.Value.Length != 7 || s.Value[0] != '#')
            {
                throw Invalid(what + " must be a colour written #RRGGBB");
            }

            int[] part = new int[3];
            for (int i = 0; i < 3; i++)
            {
                int hi = HexDigit(s.Value[1 + (2 * i)]);
                int lo = HexDigit(s.Value[2 + (2 * i)]);
                if (hi < 0 || lo < 0)
                {
                    throw Invalid(what + " must be a colour written #RRGGBB in upper-case hexadecimal");
                }

                part[i] = (hi * 16) + lo;
            }

            return new Rgb((byte)part[0], (byte)part[1], (byte)part[2]);
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            return c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
        }

        // The members of an object with exactly the given keys, in the order of keys.
        private static Node[] Members(Node node, string what, string[] keys)
        {
            if (!(node is ObjectNode o))
            {
                throw Invalid(what + " must be an object");
            }

            var found = new Node?[keys.Length];
            for (int i = 0; i < o.Keys.Count; i++)
            {
                int index = Array.IndexOf(keys, o.Keys[i]);
                if (index < 0)
                {
                    throw Invalid(what + " has the unknown key \"" + o.Keys[i] + "\"");
                }

                found[index] = o.Values[i];
            }

            var result = new Node[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                result[i] = found[i] ?? throw Invalid(what + " lacks the key \"" + keys[i] + "\"");
            }

            return result;
        }

        private static Node Parse(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                throw Invalid("a byte order mark is not allowed");
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                throw Invalid("the file is not valid UTF-8");
            }

            var reader = new Reader(text);
            Node root = reader.ReadFile();
            return root;
        }

        private abstract class Node
        {
        }

        private sealed class ObjectNode : Node
        {
            public readonly List<string> Keys = new List<string>();
            public readonly List<Node> Values = new List<Node>();
        }

        private sealed class ArrayNode : Node
        {
            public readonly List<Node> Items = new List<Node>();
        }

        private sealed class StringNode : Node
        {
            public StringNode(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        private sealed class IntNode : Node
        {
            public IntNode(long value)
            {
                Value = value;
            }

            public long Value { get; }
        }

        // Recursive-descent reader for the strict subset: objects, arrays, strings and integers.
        private sealed class Reader
        {
            private const int MaxDepth = 16;

            private readonly string _text;
            private int _pos;
            private int _line = 1;

            public Reader(string text)
            {
                _text = text;
            }

            public Node ReadFile()
            {
                SkipSpace();
                Node root = ReadValue(0);
                SkipSpace();
                if (_pos != _text.Length)
                {
                    throw Fail("unexpected content after the top-level value");
                }

                return root;
            }

            private Node ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Fail("nesting is too deep");
                }

                int c = Peek();
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return new StringNode(ReadString());
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return new IntNode(ReadInteger());
                        }

                        throw Fail("expected an object, array, string or integer");
                }
            }

            private Node ReadObject(int depth)
            {
                var o = new ObjectNode();
                _pos++;
                SkipSpace();
                if (Peek() == '}')
                {
                    _pos++;
                    return o;
                }

                while (true)
                {
                    SkipSpace();
                    string key = ReadString();
                    if (o.Keys.Contains(key))
                    {
                        throw Fail("duplicate key \"" + key + "\"");
                    }

                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    o.Keys.Add(key);
                    o.Values.Add(ReadValue(depth + 1));
                    SkipSpace();
                    int c = Next();
                    if (c == '}')
                    {
                        return o;
                    }

                    if (c != ',')
                    {
                        throw Fail("expected ',' or '}'");
                    }
                }
            }

            private Node ReadArray(int depth)
            {
                var a = new ArrayNode();
                _pos++;
                SkipSpace();
                if (Peek() == ']')
                {
                    _pos++;
                    return a;
                }

                while (true)
                {
                    SkipSpace();
                    a.Items.Add(ReadValue(depth + 1));
                    SkipSpace();
                    int c = Next();
                    if (c == ']')
                    {
                        return a;
                    }

                    if (c != ',')
                    {
                        throw Fail("expected ',' or ']'");
                    }
                }
            }

            // 0 or -?[1-9][0-9]*, with no fraction or exponent.
            private long ReadInteger()
            {
                bool negative = false;
                if (Peek() == '-')
                {
                    negative = true;
                    _pos++;
                }

                int first = Peek();
                if (first < '0' || first > '9')
                {
                    throw Fail("expected an integer");
                }

                if (first == '0' && negative)
                {
                    throw Fail("not a valid integer");
                }

                long magnitude = 0;
                int digits = 0;
                while (Peek() >= '0' && Peek() <= '9')
                {
                    if (first == '0' && digits > 0)
                    {
                        throw Fail("an integer has a leading zero");
                    }

                    if (++digits > 18)
                    {
                        throw Fail("an integer is out of range");
                    }

                    magnitude = (magnitude * 10) + (_text[_pos] - '0');
                    _pos++;
                }

                int after = Peek();
                if (after == '.' || after == 'e' || after == 'E')
                {
                    throw Fail("a number must be an integer, with no fraction or exponent");
                }

                return negative ? -magnitude : magnitude;
            }

            private string ReadString()
            {
                if (Peek() != '"')
                {
                    throw Fail("expected a string");
                }

                _pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    int c = Next();
                    if (c < 0)
                    {
                        throw Fail("unterminated string");
                    }

                    if (c == '"')
                    {
                        return sb.ToString();
                    }

                    if (c < 0x20)
                    {
                        throw Fail("a control character is not allowed in a string");
                    }

                    if (c != '\\')
                    {
                        sb.Append((char)c);
                        continue;
                    }

                    int e = Next();
                    switch (e)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append((char)e);
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'u':
                            sb.Append(ReadUnicodeEscape());
                            break;
                        default:
                            throw Fail("an unknown escape in a string");
                    }
                }
            }

            private char ReadUnicodeEscape()
            {
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    int c = Next();
                    int digit = c >= '0' && c <= '9' ? c - '0' : (c >= 'a' && c <= 'f' ? c - 'a' + 10 : (c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1));
                    if (digit < 0)
                    {
                        throw Fail("\\u needs exactly four hexadecimal digits");
                    }

                    value = (value * 16) + digit;
                }

                if (value >= 0xD800 && value <= 0xDFFF)
                {
                    throw Fail("a surrogate escape is not allowed");
                }

                return (char)value;
            }

            private void SkipSpace()
            {
                while (_pos < _text.Length)
                {
                    char c = _text[_pos];
                    if (c == '\n')
                    {
                        _line++;
                    }
                    else if (c != ' ' && c != '\t' && c != '\r')
                    {
                        return;
                    }

                    _pos++;
                }
            }

            private int Peek()
            {
                return _pos < _text.Length ? _text[_pos] : -1;
            }

            private int Next()
            {
                return _pos < _text.Length ? _text[_pos++] : -1;
            }

            private void Expect(char c)
            {
                if (Next() != c)
                {
                    throw Fail("expected '" + c + "'");
                }
            }

            private FormatException Fail(string what)
            {
                return new FormatException(Path + ": line " + _line.ToString(CultureInfo.InvariantCulture) + ": " + what);
            }
        }
    }
}
