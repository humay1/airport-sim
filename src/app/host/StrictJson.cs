using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AirportSim.App.Host
{
    /// <summary>
    /// The strict JSON subset of 08 §8.11 "The loader", enough for bundle.json and
    /// airside_rules.json: UTF-8 without a BOM, objects, arrays, strings, integers,
    /// true and false. A fraction or exponent, a duplicate key, a bad escape, a
    /// surrogate \u escape, a raw control character in a string and invalid UTF-8
    /// are failures. Values are Dictionary of string to object, List of object,
    /// string, long and bool. Every failure is a <see cref="FormatException"/>.
    /// </summary>
    internal sealed class StrictJson
    {
        private const int MaxDepth = 64;

        private readonly string _text;
        private int _pos;

        private StrictJson(string text)
        {
            _text = text;
        }

        internal static object Parse(byte[] bytes)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (ArgumentException)
            {
                throw new FormatException("invalid UTF-8");
            }

            var parser = new StrictJson(text);
            parser.SkipSpace();
            object value = parser.ReadValue(0);
            parser.SkipSpace();
            if (parser._pos != text.Length)
            {
                throw parser.Fail("unexpected content after the document");
            }

            return value;
        }

        private FormatException Fail(string what)
        {
            return new FormatException(what + " at offset " + _pos.ToString(CultureInfo.InvariantCulture));
        }

        private void SkipSpace()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r')
                {
                    return;
                }

                _pos++;
            }
        }

        private bool More()
        {
            return _pos < _text.Length;
        }

        private object ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Fail("nesting too deep");
            }

            if (!More())
            {
                throw Fail("unexpected end of document");
            }

            char c = _text[_pos];
            switch (c)
            {
                case '{':
                    return ReadObject(depth);
                case '[':
                    return ReadArray(depth);
                case '"':
                    return ReadString();
            }

            if (c == '-' || (c >= '0' && c <= '9'))
            {
                return ReadInteger();
            }

            if (TakeWord("true"))
            {
                return true;
            }

            if (TakeWord("false"))
            {
                return false;
            }

            throw Fail("unexpected character '" + c + "'");
        }

        private bool TakeWord(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
            {
                return false;
            }

            _pos += word.Length;
            return true;
        }

        private Dictionary<string, object> ReadObject(int depth)
        {
            var members = new Dictionary<string, object>(StringComparer.Ordinal);
            _pos++;
            SkipSpace();
            if (More() && _text[_pos] == '}')
            {
                _pos++;
                return members;
            }

            while (true)
            {
                SkipSpace();
                if (!More() || _text[_pos] != '"')
                {
                    throw Fail("expected a key");
                }

                string key = ReadString();
                if (members.ContainsKey(key))
                {
                    throw Fail("duplicate key '" + key + "'");
                }

                SkipSpace();
                if (!More() || _text[_pos] != ':')
                {
                    throw Fail("expected ':'");
                }

                _pos++;
                SkipSpace();
                members.Add(key, ReadValue(depth + 1));
                SkipSpace();
                if (!More())
                {
                    throw Fail("unexpected end of document");
                }

                char next = _text[_pos++];
                if (next == '}')
                {
                    return members;
                }

                if (next != ',')
                {
                    _pos--;
                    throw Fail("expected ',' or '}'");
                }
            }
        }

        private List<object> ReadArray(int depth)
        {
            var items = new List<object>();
            _pos++;
            SkipSpace();
            if (More() && _text[_pos] == ']')
            {
                _pos++;
                return items;
            }

            while (true)
            {
                SkipSpace();
                items.Add(ReadValue(depth + 1));
                SkipSpace();
                if (!More())
                {
                    throw Fail("unexpected end of document");
                }

                char next = _text[_pos++];
                if (next == ']')
                {
                    return items;
                }

                if (next != ',')
                {
                    _pos--;
                    throw Fail("expected ',' or ']'");
                }
            }
        }

        private long ReadInteger()
        {
            int start = _pos;
            if (_text[_pos] == '-')
            {
                _pos++;
            }

            int firstDigit = _pos;
            while (More() && _text[_pos] >= '0' && _text[_pos] <= '9')
            {
                _pos++;
            }

            int digits = _pos - firstDigit;
            if (digits == 0 || (digits > 1 && _text[firstDigit] == '0'))
            {
                throw Fail("malformed number");
            }

            if (More() && (_text[_pos] == '.' || _text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                throw Fail("a number with a fraction or exponent");
            }

            if (!long.TryParse(_text.Substring(start, _pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                throw Fail("integer out of range");
            }

            return value;
        }

        private string ReadString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (!More())
                {
                    throw Fail("unterminated string");
                }

                char c = _text[_pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    throw Fail("raw control character in a string");
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (!More())
                {
                    throw Fail("unterminated string");
                }

                char escape = _text[_pos++];
                switch (escape)
                {
                    case '"':
                        sb.Append('"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '/':
                        sb.Append('/');
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
                        throw Fail("bad escape '\\" + escape + "'");
                }
            }
        }

        private char ReadUnicodeEscape()
        {
            if (_pos + 4 > _text.Length)
            {
                throw Fail("bad \\u escape");
            }

            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char h = _text[_pos + i];
                int digit;
                if (h >= '0' && h <= '9')
                {
                    digit = h - '0';
                }
                else if (h >= 'a' && h <= 'f')
                {
                    digit = h - 'a' + 10;
                }
                else if (h >= 'A' && h <= 'F')
                {
                    digit = h - 'A' + 10;
                }
                else
                {
                    throw Fail("bad \\u escape");
                }

                value = (value * 16) + digit;
            }

            if (value >= 0xD800 && value <= 0xDFFF)
            {
                throw Fail("\\u escape of a surrogate");
            }

            _pos += 4;
            return (char)value;
        }
    }
}
