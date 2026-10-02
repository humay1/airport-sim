using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AirportSim.Tools.SimHarness
{
    /// <summary>
    /// The strict JSON subset of 08-interfaces-core.md §8.11 "The loader", enough to read
    /// <c>bundle.json</c> (16-interfaces-host.md §16.3, Q-069): UTF-8 without a BOM, objects,
    /// arrays, strings, integers, <c>true</c> and <c>false</c>. A fraction or exponent, a
    /// duplicate key, a bad escape, a surrogate <c>\u</c> escape, a raw control character in a
    /// string and invalid UTF-8 are all failures. Parsed values are <c>Dictionary&lt;string,
    /// object&gt;</c>, <c>List&lt;object&gt;</c>, <c>string</c>, <c>long</c> and <c>bool</c>.
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

        /// <summary>Parses one document; any failure is a <see cref="FormatException"/>.</summary>
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
            parser.SkipWhitespace();
            object value = parser.ParseValue(0);
            parser.SkipWhitespace();
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

        private void SkipWhitespace()
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

        private object ParseValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Fail("nesting too deep");
            }
            if (_pos >= _text.Length)
            {
                throw Fail("unexpected end of document");
            }

            char c = _text[_pos];
            if (c == '{')
            {
                return ParseObject(depth);
            }
            if (c == '[')
            {
                return ParseArray(depth);
            }
            if (c == '"')
            {
                return ParseString();
            }
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                return ParseInteger();
            }
            if (ConsumeWord("true"))
            {
                return true;
            }
            if (ConsumeWord("false"))
            {
                return false;
            }
            throw Fail("unexpected character '" + c + "'");
        }

        private bool ConsumeWord(string word)
        {
            if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
            {
                return false;
            }
            _pos += word.Length;
            return true;
        }

        private Dictionary<string, object> ParseObject(int depth)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            _pos++;
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == '}')
            {
                _pos++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != '"')
                {
                    throw Fail("expected a key");
                }
                string key = ParseString();
                if (result.ContainsKey(key))
                {
                    throw Fail("duplicate key '" + key + "'");
                }
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ':')
                {
                    throw Fail("expected ':'");
                }
                _pos++;
                SkipWhitespace();
                result.Add(key, ParseValue(depth + 1));
                SkipWhitespace();
                if (_pos >= _text.Length)
                {
                    throw Fail("unexpected end of document");
                }
                if (_text[_pos] == ',')
                {
                    _pos++;
                    continue;
                }
                if (_text[_pos] == '}')
                {
                    _pos++;
                    return result;
                }
                throw Fail("expected ',' or '}'");
            }
        }

        private List<object> ParseArray(int depth)
        {
            var result = new List<object>();
            _pos++;
            SkipWhitespace();
            if (_pos < _text.Length && _text[_pos] == ']')
            {
                _pos++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                result.Add(ParseValue(depth + 1));
                SkipWhitespace();
                if (_pos >= _text.Length)
                {
                    throw Fail("unexpected end of document");
                }
                if (_text[_pos] == ',')
                {
                    _pos++;
                    continue;
                }
                if (_text[_pos] == ']')
                {
                    _pos++;
                    return result;
                }
                throw Fail("expected ',' or ']'");
            }
        }

        private long ParseInteger()
        {
            int start = _pos;
            if (_text[_pos] == '-')
            {
                _pos++;
            }
            int digitsStart = _pos;
            while (_pos < _text.Length && _text[_pos] >= '0' && _text[_pos] <= '9')
            {
                _pos++;
            }
            int digits = _pos - digitsStart;
            if (digits == 0 || (digits > 1 && _text[digitsStart] == '0'))
            {
                throw Fail("malformed number");
            }
            if (_pos < _text.Length && (_text[_pos] == '.' || _text[_pos] == 'e' || _text[_pos] == 'E'))
            {
                throw Fail("a number with a fraction or exponent");
            }
            if (!long.TryParse(_text.AsSpan(start, _pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                throw Fail("integer out of range");
            }
            return value;
        }

        private string ParseString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length)
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

                if (_pos >= _text.Length)
                {
                    throw Fail("unterminated string");
                }
                char e = _text[_pos++];
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
                        throw Fail("bad escape '\\" + e + "'");
                }
            }
        }

        private char ParseUnicodeEscape()
        {
            if (_pos + 4 > _text.Length)
            {
                throw Fail("bad \\u escape");
            }
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                char h = _text[_pos + i];
                int d;
                if (h >= '0' && h <= '9')
                {
                    d = h - '0';
                }
                else if (h >= 'a' && h <= 'f')
                {
                    d = h - 'a' + 10;
                }
                else if (h >= 'A' && h <= 'F')
                {
                    d = h - 'A' + 10;
                }
                else
                {
                    throw Fail("bad \\u escape");
                }
                value = (value * 16) + d;
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
