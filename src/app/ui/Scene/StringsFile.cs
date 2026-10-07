using System;
using System.Collections.Generic;
using System.Text;

namespace AirportSim.App.Ui
{
    /// <summary>
    /// Reads <c>strings/en.json</c> with 08 §8.11's strict JSON subset and Q-033's string
    /// rules, then checks it against 17 §17.4b. Every failure is a
    /// <see cref="FormatException"/> whose message starts with the path.
    /// </summary>
    internal sealed class StringsFile
    {
        internal const string Path = "strings/en.json";

        private const string Prefix = Path + ": ";
        private const int MaxDepth = 16;

        private enum Kind
        {
            Object,
            Array,
            Str,
            Integer,
            Bool,
        }

        private sealed class Node
        {
            internal Kind Kind;
            internal string Text = string.Empty;
            internal readonly List<KeyValuePair<string, Node>> Members = new List<KeyValuePair<string, Node>>();

            internal Node? Get(string key)
            {
                for (int i = 0; i < Members.Count; i++)
                {
                    if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                    {
                        return Members[i].Value;
                    }
                }

                return null;
            }
        }

        private readonly string _s;
        private int _pos;

        private StringsFile(string s)
        {
            _s = s;
        }

        /// <summary>Parses and validates <paramref name="bytes"/>; returns the twelve texts in slot order.</summary>
        internal static string[] Read(byte[]? bytes)
        {
            if (bytes is null)
            {
                throw Fail("the file is missing");
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                throw Fail("the file is not valid UTF-8");
            }

            var reader = new StringsFile(text);
            Node root = reader.ParseDocument();
            return Validate(root);
        }

        private static string[] Validate(Node root)
        {
            if (root.Kind != Kind.Object)
            {
                throw Fail("the document is not an object");
            }

            for (int i = 0; i < root.Members.Count; i++)
            {
                string k = root.Members[i].Key;
                if (k != "schema_version" && k != "id" && k != "strings")
                {
                    throw Fail("unknown key '" + k + "'");
                }
            }

            Node? version = root.Get("schema_version");
            Node? id = root.Get("id");
            Node? strings = root.Get("strings");
            if (version is null)
            {
                throw Fail("missing key 'schema_version'");
            }

            if (id is null)
            {
                throw Fail("missing key 'id'");
            }

            if (strings is null)
            {
                throw Fail("missing key 'strings'");
            }

            if (version.Kind != Kind.Integer || version.Text != "1")
            {
                throw Fail("schema_version must be 1");
            }

            if (id.Kind != Kind.Str || id.Text != "en")
            {
                throw Fail("id must be \"en\"");
            }

            if (strings.Kind != Kind.Object)
            {
                throw Fail("'strings' is not an object");
            }

            var texts = new string[StringTable.KeyCount];
            int found = 0;
            for (int i = 0; i < strings.Members.Count; i++)
            {
                string key = strings.Members[i].Key;
                Node value = strings.Members[i].Value;
                int slot = StringTable.SlotOf(key);
                if (slot < 0)
                {
                    throw Fail("unknown string key '" + key + "'");
                }

                if (value.Kind != Kind.Str)
                {
                    throw Fail("the text of '" + key + "' is not a string");
                }

                if (value.Text.Length == 0)
                {
                    throw Fail("the text of '" + key + "' is empty");
                }

                for (int c = 0; c < value.Text.Length; c++)
                {
                    if (value.Text[c] < ' ' || value.Text[c] > '~')
                    {
                        throw Fail("the text of '" + key + "' holds a character outside U+0020 to U+007E");
                    }
                }

                texts[slot] = value.Text;
                found++;
            }

            if (found != StringTable.KeyCount)
            {
                throw Fail("'strings' must hold exactly the " + StringTable.KeyCount + " Phase 1 keys");
            }

            return texts;
        }

        private static FormatException Fail(string message)
        {
            return new FormatException(Prefix + message);
        }

        private Node ParseDocument()
        {
            Node root = ParseValue(0);
            SkipWhitespace();
            if (_pos != _s.Length)
            {
                throw Fail("unexpected content after the JSON value");
            }

            return root;
        }

        private Node ParseValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Fail("nesting is too deep");
            }

            SkipWhitespace();
            if (_pos >= _s.Length)
            {
                throw Fail("unexpected end of input");
            }

            char c = _s[_pos];
            switch (c)
            {
                case '{':
                    return ParseObject(depth);
                case '[':
                    return ParseArray(depth);
                case '"':
                    return new Node { Kind = Kind.Str, Text = ParseString() };
                case 't':
                    ExpectLiteral("true");
                    return new Node { Kind = Kind.Bool, Text = "true" };
                case 'f':
                    ExpectLiteral("false");
                    return new Node { Kind = Kind.Bool, Text = "false" };
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseInteger();
                    }

                    throw Fail("unexpected character");
            }
        }

        private void ExpectLiteral(string literal)
        {
            if (_pos + literal.Length > _s.Length || string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0)
            {
                throw Fail("expected '" + literal + "'");
            }

            _pos += literal.Length;
        }

        private Node ParseObject(int depth)
        {
            _pos++;
            var obj = new Node { Kind = Kind.Object };
            SkipWhitespace();
            if (_pos < _s.Length && _s[_pos] == '}')
            {
                _pos++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _s.Length || _s[_pos] != '"')
                {
                    throw Fail("expected a string key");
                }

                string key = ParseString();
                SkipWhitespace();
                if (_pos >= _s.Length || _s[_pos] != ':')
                {
                    throw Fail("expected ':' after key '" + key + "'");
                }

                _pos++;
                Node value = ParseValue(depth + 1);
                if (obj.Get(key) != null)
                {
                    throw Fail("duplicate key '" + key + "'");
                }

                obj.Members.Add(new KeyValuePair<string, Node>(key, value));
                SkipWhitespace();
                if (_pos >= _s.Length)
                {
                    throw Fail("unterminated object");
                }

                if (_s[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                if (_s[_pos] == '}')
                {
                    _pos++;
                    return obj;
                }

                throw Fail("expected ',' or '}'");
            }
        }

        private Node ParseArray(int depth)
        {
            _pos++;
            var arr = new Node { Kind = Kind.Array };
            SkipWhitespace();
            if (_pos < _s.Length && _s[_pos] == ']')
            {
                _pos++;
                return arr;
            }

            while (true)
            {
                ParseValue(depth + 1);
                SkipWhitespace();
                if (_pos >= _s.Length)
                {
                    throw Fail("unterminated array");
                }

                if (_s[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                if (_s[_pos] == ']')
                {
                    _pos++;
                    return arr;
                }

                throw Fail("expected ',' or ']'");
            }
        }

        private string ParseString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _s.Length)
                {
                    throw Fail("unterminated string");
                }

                char c = _s[_pos];
                if (c == '"')
                {
                    _pos++;
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    throw Fail("control character in string literal");
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    _pos++;
                    continue;
                }

                _pos++;
                if (_pos >= _s.Length)
                {
                    throw Fail("unterminated escape sequence");
                }

                char e = _s[_pos];
                _pos++;
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        sb.Append(ParseUnicodeEscape());
                        break;
                    default:
                        throw Fail("invalid escape");
                }
            }
        }

        private char ParseUnicodeEscape()
        {
            if (_pos + 4 > _s.Length)
            {
                throw Fail("invalid \\u escape");
            }

            int code = 0;
            for (int k = 0; k < 4; k++)
            {
                int d = HexDigit(_s[_pos + k]);
                if (d < 0)
                {
                    throw Fail("invalid \\u escape");
                }

                code = (code << 4) | d;
            }

            if (code >= 0xD800 && code <= 0xDFFF)
            {
                throw Fail("a \\u escape may not encode a surrogate");
            }

            _pos += 4;
            return (char)code;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            return -1;
        }

        private Node ParseInteger()
        {
            int start = _pos;
            if (_s[_pos] == '-')
            {
                _pos++;
                if (_pos >= _s.Length || _s[_pos] < '0' || _s[_pos] > '9')
                {
                    throw Fail("invalid number");
                }
            }

            if (_s[_pos] == '0')
            {
                _pos++;
            }
            else
            {
                while (_pos < _s.Length && _s[_pos] >= '0' && _s[_pos] <= '9')
                {
                    _pos++;
                }
            }

            if (_pos < _s.Length)
            {
                char n = _s[_pos];
                if (n == '.' || n == 'e' || n == 'E' || (n >= '0' && n <= '9'))
                {
                    throw Fail("a number must be an integer without a leading zero");
                }
            }

            return new Node { Kind = Kind.Integer, Text = _s.Substring(start, _pos - start) };
        }

        private void SkipWhitespace()
        {
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    _pos++;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
