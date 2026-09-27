using System;
using System.Collections.Generic;
using System.Text;

namespace AirportSim.Sim.Core
{
    // A hand-written, package-free parser for the strict JSON subset content
    // files use (08-interfaces-core.md §8.11 "The loader"): objects, arrays,
    // strings, integers, true and false. No floats, no exponents, no null, no
    // comments, no trailing commas, no unquoted keys. Internal: not part of the
    // module's public spec surface (ContentLoaderFactory and IContentLoader are).

    /// <summary>One parsed JSON value in the loader's strict subset.</summary>
    internal abstract class JsonValue
    {
    }

    /// <summary>
    /// A JSON object. Preserves member order and every occurrence of a key, so a
    /// duplicate key is visible to the parser that builds it.
    /// </summary>
    internal sealed class JsonObject : JsonValue
    {
        internal readonly List<KeyValuePair<string, JsonValue>> Members = new List<KeyValuePair<string, JsonValue>>();

        internal bool TryGet(string key, out JsonValue value)
        {
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i].Key, key, StringComparison.Ordinal))
                {
                    value = Members[i].Value;
                    return true;
                }
            }

            value = default!;
            return false;
        }
    }

    /// <summary>A JSON array, in source order.</summary>
    internal sealed class JsonArray : JsonValue
    {
        internal readonly List<JsonValue> Items = new List<JsonValue>();
    }

    /// <summary>A JSON string, already unescaped.</summary>
    internal sealed class JsonString : JsonValue
    {
        internal readonly string Value;

        internal JsonString(string value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// A JSON integer: a sign and a digit string with no leading zero (except
    /// "0" itself). No fraction, no exponent — those are load failures, not a
    /// representable value.
    /// </summary>
    internal sealed class JsonInteger : JsonValue
    {
        internal readonly bool Negative;
        internal readonly string Digits;

        internal JsonInteger(bool negative, string digits)
        {
            Negative = negative;
            Digits = digits;
        }
    }

    /// <summary>A JSON boolean.</summary>
    internal sealed class JsonBool : JsonValue
    {
        internal readonly bool Value;

        internal JsonBool(bool value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Recursive-descent parser for the strict subset. Every failure throws
    /// <see cref="FormatException"/> with a message starting "&lt;path&gt;: "
    /// (07-conventions.md "Error handling", Q-030).
    /// </summary>
    internal static class JsonParser
    {
        internal static JsonValue Parse(string text, string path)
        {
            int pos = 0;
            JsonValue value = ParseValue(text, ref pos, path);
            SkipWhitespace(text, ref pos);
            if (pos != text.Length)
            {
                throw Fail(path, "unexpected content after the JSON value");
            }

            return value;
        }

        private static JsonValue ParseValue(string s, ref int pos, string path)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length)
            {
                throw Fail(path, "unexpected end of input");
            }

            char c = s[pos];
            switch (c)
            {
                case '{':
                    return ParseObject(s, ref pos, path);
                case '[':
                    return ParseArray(s, ref pos, path);
                case '"':
                    return new JsonString(ParseStringLiteral(s, ref pos, path));
                case 't':
                    ExpectLiteral(s, ref pos, "true", path);
                    return new JsonBool(true);
                case 'f':
                    ExpectLiteral(s, ref pos, "false", path);
                    return new JsonBool(false);
                case '-':
                    return ParseNumber(s, ref pos, path);
                default:
                    if (c >= '0' && c <= '9')
                    {
                        return ParseNumber(s, ref pos, path);
                    }

                    throw Fail(path, "unexpected character '" + c + "'");
            }
        }

        private static void ExpectLiteral(string s, ref int pos, string literal, string path)
        {
            if (pos + literal.Length > s.Length || string.CompareOrdinal(s, pos, literal, 0, literal.Length) != 0)
            {
                throw Fail(path, "expected '" + literal + "'");
            }

            pos += literal.Length;
        }

        private static JsonObject ParseObject(string s, ref int pos, string path)
        {
            pos++; // '{'
            var obj = new JsonObject();
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '}')
            {
                pos++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '"')
                {
                    throw Fail(path, "expected a string key");
                }

                string key = ParseStringLiteral(s, ref pos, path);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':')
                {
                    throw Fail(path, "expected ':' after key '" + key + "'");
                }

                pos++;
                JsonValue value = ParseValue(s, ref pos, path);
                if (obj.TryGet(key, out _))
                {
                    throw Fail(path, "duplicate key '" + key + "'");
                }

                obj.Members.Add(new KeyValuePair<string, JsonValue>(key, value));
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                {
                    throw Fail(path, "unterminated object");
                }

                if (s[pos] == ',')
                {
                    pos++;
                    continue;
                }

                if (s[pos] == '}')
                {
                    pos++;
                    break;
                }

                throw Fail(path, "expected ',' or '}'");
            }

            return obj;
        }

        private static JsonArray ParseArray(string s, ref int pos, string path)
        {
            pos++; // '['
            var arr = new JsonArray();
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ']')
            {
                pos++;
                return arr;
            }

            while (true)
            {
                JsonValue value = ParseValue(s, ref pos, path);
                arr.Items.Add(value);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length)
                {
                    throw Fail(path, "unterminated array");
                }

                if (s[pos] == ',')
                {
                    pos++;
                    continue;
                }

                if (s[pos] == ']')
                {
                    pos++;
                    break;
                }

                throw Fail(path, "expected ',' or ']'");
            }

            return arr;
        }

        private static string ParseStringLiteral(string s, ref int pos, string path)
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length)
                {
                    throw Fail(path, "unterminated string");
                }

                char c = s[pos];
                if (c == '"')
                {
                    pos++;
                    break;
                }

                if (c == '\\')
                {
                    pos++;
                    if (pos >= s.Length)
                    {
                        throw Fail(path, "unterminated escape sequence");
                    }

                    char e = s[pos];
                    switch (e)
                    {
                        case '"': sb.Append('"'); pos++; break;
                        case '\\': sb.Append('\\'); pos++; break;
                        case '/': sb.Append('/'); pos++; break;
                        case 'b': sb.Append('\b'); pos++; break;
                        case 'f': sb.Append('\f'); pos++; break;
                        case 'n': sb.Append('\n'); pos++; break;
                        case 'r': sb.Append('\r'); pos++; break;
                        case 't': sb.Append('\t'); pos++; break;
                        case 'u':
                            pos++;
                            if (pos + 4 > s.Length)
                            {
                                throw Fail(path, "invalid \\u escape");
                            }

                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int d = HexDigit(s[pos + k]);
                                if (d < 0)
                                {
                                    throw Fail(path, "invalid \\u escape");
                                }

                                code = (code << 4) | d;
                            }

                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default:
                            throw Fail(path, "invalid escape '\\" + e + "'");
                    }

                    continue;
                }

                if (c < 0x20)
                {
                    throw Fail(path, "control character in string literal");
                }

                sb.Append(c);
                pos++;
            }

            return sb.ToString();
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

        private static JsonInteger ParseNumber(string s, ref int pos, string path)
        {
            bool negative = false;
            if (s[pos] == '-')
            {
                negative = true;
                pos++;
                if (pos >= s.Length || s[pos] < '0' || s[pos] > '9')
                {
                    throw Fail(path, "invalid number");
                }
            }

            int digitsStart = pos;
            if (s[pos] == '0')
            {
                pos++;
            }
            else
            {
                while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
                {
                    pos++;
                }
            }

            string digits = s.Substring(digitsStart, pos - digitsStart);

            if (pos < s.Length && (s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E'))
            {
                throw Fail(path, "a number with a fraction or exponent is not supported");
            }

            if (pos < s.Length && s[pos] >= '0' && s[pos] <= '9')
            {
                throw Fail(path, "a number may not have a leading zero");
            }

            return new JsonInteger(negative, digits);
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    pos++;
                }
                else
                {
                    break;
                }
            }
        }

        internal static FormatException Fail(string path, string message)
        {
            return new FormatException(path + ": " + message);
        }
    }
}
