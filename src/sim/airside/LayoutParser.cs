using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Airside
{
    /// <summary>
    /// Hand-parses the strict JSON subset of 12-interfaces-airside.md §12.4 "File format" (Q-046),
    /// with no package. Every failure is a <see cref="FormatException"/> starting with the source
    /// name and carrying the 1-based <c>line n</c>.
    /// </summary>
    internal sealed class LayoutParser
    {
        private const char TUInt16 = 'h';
        private const char TUInt32 = 'u';
        private const char TInt32 = 'i';
        private const char TBool = 'b';
        private const char TString = 's';

        private readonly byte[] _file;
        private readonly string _source;
        private int _pos;
        private int _line = 1;

        private LayoutParser(byte[] file, string source)
        {
            _file = file;
            _source = source;
        }

        /// <summary>Parses the file into a raw (unvalidated) layout.</summary>
        internal static AirsideLayout Parse(ReadOnlySpan<byte> file, string sourceName)
        {
            if (sourceName is null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }

            return new LayoutParser(file.ToArray(), sourceName).Document();
        }

        private AirsideLayout Document()
        {
            var runways = new List<RunwayDef>();
            var nodes = new List<TaxiNodeDef>();
            var edges = new List<TaxiEdgeDef>();
            var stands = new List<StandDef>();
            bool hasSchema = false;
            bool hasRunways = false;
            bool hasNodes = false;
            bool hasEdges = false;
            bool hasStands = false;

            SkipWs();
            Require('{');
            SkipWs();
            if (!TryConsume('}'))
            {
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    Require(':');
                    SkipWs();
                    switch (key)
                    {
                        case "schema_version":
                            if (hasSchema)
                            {
                                throw Fail("duplicate key 'schema_version'");
                            }

                            hasSchema = true;
                            int versionLine = _line;
                            long version = ParseInteger();
                            if (version != 1)
                            {
                                throw FailAt(versionLine, "schema_version must be 1");
                            }

                            break;
                        case "runways":
                            if (hasRunways)
                            {
                                throw Fail("duplicate key 'runways'");
                            }

                            hasRunways = true;
                            ParseArray(RunwayKeys, RunwayTypes, v => runways.Add(new RunwayDef(
                                new RunwayId((ushort)v.I[0]), new TaxiNodeId((ushort)v.I[1]), (int)v.I[2], (int)v.I[3], (uint)v.I[4],
                                v.Seen[5] ? new TaxiNodeId((ushort)v.I[5]) : new TaxiNodeId((ushort)v.I[1]))), 5);
                            break;
                        case "nodes":
                            if (hasNodes)
                            {
                                throw Fail("duplicate key 'nodes'");
                            }

                            hasNodes = true;
                            ParseArray(NodeKeys, NodeTypes, v => nodes.Add(new TaxiNodeDef(new TaxiNodeId((ushort)v.I[0]), KindOf(v))));
                            break;
                        case "edges":
                            if (hasEdges)
                            {
                                throw Fail("duplicate key 'edges'");
                            }

                            hasEdges = true;
                            ParseArray(EdgeKeys, EdgeTypes, v => edges.Add(new TaxiEdgeDef(
                                new TaxiEdgeId((ushort)v.I[0]), new TaxiNodeId((ushort)v.I[1]), new TaxiNodeId((ushort)v.I[2]), (uint)v.I[3], v.I[4] != 0)));
                            break;
                        case "stands":
                            if (hasStands)
                            {
                                throw Fail("duplicate key 'stands'");
                            }

                            hasStands = true;
                            ParseArray(StandKeys, StandTypes, v => stands.Add(new StandDef(
                                new StandId((ushort)v.I[0]), new TaxiNodeId((ushort)v.I[1]), new ContentId(v.S[2]!), new NodeId((uint)v.I[3]))));
                            break;
                        default:
                            throw Fail("unknown key '" + key + "'");
                    }

                    SkipWs();
                    if (TryConsume(','))
                    {
                        continue;
                    }

                    if (TryConsume('}'))
                    {
                        break;
                    }

                    throw Fail("expected ',' or '}'");
                }
            }

            SkipWs();
            if (_pos != _file.Length)
            {
                throw Fail("trailing content after the document");
            }

            if (!hasSchema || !hasRunways || !hasNodes || !hasEdges || !hasStands)
            {
                throw Fail("missing required key (schema_version, runways, nodes, edges or stands)");
            }

            return new AirsideLayout(runways, nodes, edges, stands);
        }

        private readonly string[] RunwayKeys = { "id", "threshold_node", "active_direction_deg", "declared_capacity_per_hour", "occupancy_ticks", "exit_node" };
        private readonly char[] RunwayTypes = { TUInt16, TUInt16, TInt32, TInt32, TUInt32, TUInt16 };
        private readonly string[] NodeKeys = { "id", "kind" };
        private readonly char[] NodeTypes = { TUInt16, TString };
        private readonly string[] EdgeKeys = { "id", "from", "to", "traversal_ticks", "bidirectional" };
        private readonly char[] EdgeTypes = { TUInt16, TUInt16, TUInt16, TUInt32, TBool };
        private readonly string[] StandKeys = { "id", "node", "max_aircraft_size_category", "departure_sink_node" };
        private readonly char[] StandTypes = { TUInt16, TUInt16, TString, TUInt32 };

        /// <summary>The values of one parsed object, by key position.</summary>
        private sealed class Values
        {
            public readonly long[] I;
            public readonly string?[] S;
            public bool[] Seen = Array.Empty<bool>();
            public int Line;

            public Values(int n)
            {
                I = new long[n];
                S = new string?[n];
            }
        }

        private TaxiNodeKind KindOf(Values v)
        {
            switch (v.S[1])
            {
                case "runway_threshold": return TaxiNodeKind.RunwayThreshold;
                case "junction": return TaxiNodeKind.Junction;
                case "stand_position": return TaxiNodeKind.StandPosition;
                default: throw FailAt(v.Line, "unknown node kind '" + v.S[1] + "'");
            }
        }

        private void ParseArray(string[] keys, char[] types, Action<Values> add, int required = -1)
        {
            Require('[');
            SkipWs();
            if (TryConsume(']'))
            {
                return;
            }

            while (true)
            {
                SkipWs();
                add(ParseObject(keys, types, required < 0 ? keys.Length : required));
                SkipWs();
                if (TryConsume(','))
                {
                    SkipWs();
                    if (Peek() == ']')
                    {
                        throw Fail("trailing comma in array");
                    }

                    continue;
                }

                if (TryConsume(']'))
                {
                    return;
                }

                throw Fail("expected ',' or ']'");
            }
        }

        private Values ParseObject(string[] keys, char[] types, int required)
        {
            var values = new Values(keys.Length);
            var seen = new bool[keys.Length];
            values.Line = _line;
            values.Seen = seen;
            Require('{');
            SkipWs();
            if (!TryConsume('}'))
            {
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    int k = Array.IndexOf(keys, key);
                    if (k < 0)
                    {
                        throw Fail("unknown key '" + key + "'");
                    }

                    if (seen[k])
                    {
                        throw Fail("duplicate key '" + key + "'");
                    }

                    seen[k] = true;
                    SkipWs();
                    Require(':');
                    SkipWs();
                    ParseValue(types[k], key, values, k);
                    SkipWs();
                    if (TryConsume(','))
                    {
                        continue;
                    }

                    if (TryConsume('}'))
                    {
                        break;
                    }

                    throw Fail("expected ',' or '}'");
                }
            }

            for (int k = 0; k < required; k++)
            {
                if (!seen[k])
                {
                    throw Fail("missing key '" + keys[k] + "'");
                }
            }

            return values;
        }

        private void ParseValue(char type, string key, Values values, int k)
        {
            int startLine = _line;
            switch (type)
            {
                case TString:
                    if (Peek() != '"')
                    {
                        throw Fail("'" + key + "' must be a string");
                    }

                    values.S[k] = ParseString();
                    return;
                case TBool:
                    if (TryLiteral("true"))
                    {
                        values.I[k] = 1;
                        return;
                    }

                    if (TryLiteral("false"))
                    {
                        values.I[k] = 0;
                        return;
                    }

                    throw Fail("'" + key + "' must be true or false");
                default:
                    long n = ParseInteger();
                    long min;
                    long max;
                    if (type == TUInt16)
                    {
                        min = 0;
                        max = ushort.MaxValue;
                    }
                    else if (type == TUInt32)
                    {
                        min = 0;
                        max = uint.MaxValue;
                    }
                    else
                    {
                        min = int.MinValue;
                        max = int.MaxValue;
                    }

                    if (n < min || n > max)
                    {
                        throw FailAt(startLine, "'" + key + "' out of range for its type");
                    }

                    values.I[k] = n;
                    return;
            }
        }

        private bool TryLiteral(string text)
        {
            if (_pos + text.Length > _file.Length)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (_file[_pos + i] != (byte)text[i])
                {
                    return false;
                }
            }

            int after = _pos + text.Length;
            if (after < _file.Length && (char.IsLetterOrDigit((char)_file[after]) || _file[after] == (byte)'_'))
            {
                return false;
            }

            _pos = after;
            return true;
        }

        private long ParseInteger()
        {
            int startLine = _line;
            bool neg = false;
            if (_pos < _file.Length && _file[_pos] == (byte)'-')
            {
                neg = true;
                _pos++;
            }

            if (_pos >= _file.Length || _file[_pos] < (byte)'0' || _file[_pos] > (byte)'9')
            {
                throw FailAt(startLine, "expected an integer");
            }

            long value = 0;
            if (_file[_pos] == (byte)'0')
            {
                if (neg)
                {
                    throw FailAt(startLine, "'-0' is not a valid integer");
                }

                _pos++;
                if (_pos < _file.Length && _file[_pos] >= (byte)'0' && _file[_pos] <= (byte)'9')
                {
                    throw FailAt(startLine, "a leading zero is not a valid integer");
                }
            }
            else
            {
                while (_pos < _file.Length && _file[_pos] >= (byte)'0' && _file[_pos] <= (byte)'9')
                {
                    int digit = _file[_pos] - (byte)'0';
                    if (value > (long.MaxValue - digit) / 10)
                    {
                        throw FailAt(startLine, "integer out of range");
                    }

                    value = (value * 10) + digit;
                    _pos++;
                }

                if (neg)
                {
                    value = -value;
                }
            }

            if (_pos < _file.Length && (_file[_pos] == (byte)'.' || _file[_pos] == (byte)'e' || _file[_pos] == (byte)'E'))
            {
                throw FailAt(startLine, "fractional and exponent numbers are not allowed");
            }

            return value;
        }

        private string ParseString()
        {
            int startLine = _line;
            Require('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _file.Length)
                {
                    throw FailAt(startLine, "unterminated string");
                }

                byte b = _file[_pos];
                if (b == (byte)'"')
                {
                    _pos++;
                    return sb.ToString();
                }

                if (b == (byte)'\\')
                {
                    _pos++;
                    if (_pos >= _file.Length)
                    {
                        throw FailAt(startLine, "unterminated string");
                    }

                    char e = (char)_file[_pos++];
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
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int d = _pos < _file.Length ? Hex(_file[_pos]) : -1;
                                if (d < 0)
                                {
                                    throw FailAt(startLine, "invalid \\u escape");
                                }

                                code = (code << 4) | d;
                                _pos++;
                            }

                            if (code >= 0xD800 && code <= 0xDFFF)
                            {
                                throw FailAt(startLine, "\\u surrogate escape is not allowed");
                            }

                            sb.Append((char)code);
                            break;
                        default:
                            throw FailAt(startLine, "invalid escape");
                    }

                    continue;
                }

                if (b < 0x20)
                {
                    throw FailAt(startLine, "control character in string");
                }

                if (b < 0x80)
                {
                    sb.Append((char)b);
                    _pos++;
                    continue;
                }

                AppendUtf8(sb, startLine);
            }
        }

        private void AppendUtf8(StringBuilder sb, int startLine)
        {
            byte lead = _file[_pos];
            int extra;
            int cp;
            int min;
            if (lead >= 0xC2 && lead <= 0xDF)
            {
                extra = 1;
                cp = lead & 0x1F;
                min = 0x80;
            }
            else if (lead >= 0xE0 && lead <= 0xEF)
            {
                extra = 2;
                cp = lead & 0x0F;
                min = 0x800;
            }
            else if (lead >= 0xF0 && lead <= 0xF4)
            {
                extra = 3;
                cp = lead & 0x07;
                min = 0x10000;
            }
            else
            {
                throw FailAt(startLine, "invalid UTF-8");
            }

            _pos++;
            for (int k = 0; k < extra; k++)
            {
                if (_pos >= _file.Length || (_file[_pos] & 0xC0) != 0x80)
                {
                    throw FailAt(startLine, "invalid UTF-8");
                }

                cp = (cp << 6) | (_file[_pos] & 0x3F);
                _pos++;
            }

            if (cp < min || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
            {
                throw FailAt(startLine, "invalid UTF-8");
            }

            if (cp <= 0xFFFF)
            {
                sb.Append((char)cp);
            }
            else
            {
                int v = cp - 0x10000;
                sb.Append((char)(0xD800 + (v >> 10)));
                sb.Append((char)(0xDC00 + (v & 0x3FF)));
            }
        }

        private static int Hex(byte b)
        {
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                return b - (byte)'0';
            }

            if (b >= (byte)'a' && b <= (byte)'f')
            {
                return b - (byte)'a' + 10;
            }

            if (b >= (byte)'A' && b <= (byte)'F')
            {
                return b - (byte)'A' + 10;
            }

            return -1;
        }

        private void SkipWs()
        {
            while (_pos < _file.Length)
            {
                byte b = _file[_pos];
                if (b == (byte)'\n')
                {
                    _line++;
                }
                else if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
                {
                    return;
                }

                _pos++;
            }
        }

        private char Peek()
        {
            return _pos < _file.Length ? (char)_file[_pos] : '\0';
        }

        private void Require(char c)
        {
            if (!TryConsume(c))
            {
                throw Fail("expected '" + c + "'");
            }
        }

        private bool TryConsume(char c)
        {
            if (_pos < _file.Length && _file[_pos] == (byte)c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private FormatException Fail(string detail)
        {
            return FailAt(_line, detail);
        }

        private FormatException FailAt(int line, string detail)
        {
            return new FormatException(_source + ": " + detail + " (line " + line.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }
}
