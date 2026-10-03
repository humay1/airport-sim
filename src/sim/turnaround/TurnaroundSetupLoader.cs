using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// The one <see cref="ITurnaroundSetupLoader"/>. Spec: 13-interfaces-turnaround.md
    /// §13.10a "File format" (Q-086): the strict JSON subset of 08 §8.11, hand-parsed.
    /// A parse failure is a FormatException that starts with the source name and contains
    /// <c>line n</c>; a §13.4 failure is a FormatException that starts with the source name
    /// and names the job kind or vehicle id.
    /// </summary>
    internal sealed class TurnaroundSetupLoader : ITurnaroundSetupLoader
    {
        public TurnaroundSetup Load(ReadOnlySpan<byte> file, string sourceName)
        {
            if (sourceName is null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }

            var parser = new Parser(file.ToArray(), sourceName);
            var jobs = new List<JobDef>();
            var vehicles = new List<VehicleDef>();
            parser.Document(jobs, vehicles);

            // Ascending JobKind and ascending VehicleId; stable, so duplicates stay for the checks.
            StableSort(jobs, (a, b) => ((int)a.Kind).CompareTo((int)b.Kind));
            StableSort(vehicles, (a, b) => a.Id.Value.CompareTo(b.Id.Value));

            var setup = new TurnaroundSetup(new TurnaroundCatalogue(jobs), new TurnaroundFleet(vehicles));
            string? failure = TurnaroundSetupValidator.Check(setup);
            if (failure != null)
            {
                throw new FormatException(sourceName + ": " + failure);
            }

            return setup;
        }

        private static void StableSort<T>(List<T> list, Comparison<T> compare)
        {
            for (int i = 1; i < list.Count; i++)
            {
                T item = list[i];
                int p = i - 1;
                while (p >= 0 && compare(list[p], item) > 0)
                {
                    list[p + 1] = list[p];
                    p--;
                }

                list[p + 1] = item;
            }
        }

        private sealed class Parser
        {
            private const char TUInt16 = 'h';
            private const char TUInt32 = 'u';
            private const char TString = 's';

            private static readonly string[] JobKeys = { "kind", "nominal_duration_ticks", "category" };
            private static readonly char[] JobTypes = { TString, TUInt32, TString };
            private static readonly string[] VehicleKeys = { "id", "kind" };
            private static readonly char[] VehicleTypes = { TUInt16, TString };

            private readonly byte[] _file;
            private readonly string _source;
            private int _pos;
            private int _line = 1;

            public Parser(byte[] file, string source)
            {
                _file = file;
                _source = source;
            }

            public void Document(List<JobDef> jobs, List<VehicleDef> vehicles)
            {
                bool hasSchema = false;
                bool hasJobs = false;
                bool hasVehicles = false;

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
                                if (ParseInteger() != 1)
                                {
                                    throw FailAt(versionLine, "schema_version must be 1");
                                }

                                break;
                            case "jobs":
                                if (hasJobs)
                                {
                                    throw Fail("duplicate key 'jobs'");
                                }

                                hasJobs = true;
                                ParseArray(JobKeys, JobTypes, v => jobs.Add(JobOf(v)));
                                break;
                            case "vehicles":
                                if (hasVehicles)
                                {
                                    throw Fail("duplicate key 'vehicles'");
                                }

                                hasVehicles = true;
                                ParseArray(VehicleKeys, VehicleTypes, v => vehicles.Add(VehicleOf(v)));
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

                if (!hasSchema || !hasJobs || !hasVehicles)
                {
                    throw Fail("missing required key (schema_version, jobs or vehicles)");
                }
            }

            private JobDef JobOf(Values v)
            {
                if (!TurnaroundNames.TryParse(v.S[0]!, out JobKind kind))
                {
                    throw FailAt(v.Line, "unknown job kind '" + v.S[0] + "'");
                }

                if (!TurnaroundNames.TryParse(v.S[2]!, out DelayCategory category))
                {
                    throw FailAt(v.Line, "unknown delay category '" + v.S[2] + "'");
                }

                return new JobDef(kind, TurnaroundNames.RequiredVehicle(kind), (uint)v.I[1], category);
            }

            private VehicleDef VehicleOf(Values v)
            {
                if (!TurnaroundNames.TryParse(v.S[1]!, out VehicleKind kind))
                {
                    throw FailAt(v.Line, "unknown vehicle kind '" + v.S[1] + "'");
                }

                return new VehicleDef(new VehicleId((ushort)v.I[0]), kind);
            }

            /// <summary>The values of one parsed object, by key position.</summary>
            private sealed class Values
            {
                public readonly long[] I;
                public readonly string?[] S;
                public int Line;

                public Values(int n)
                {
                    I = new long[n];
                    S = new string?[n];
                }
            }

            private void ParseArray(string[] keys, char[] types, Action<Values> add)
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
                    add(ParseObject(keys, types));
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

            private Values ParseObject(string[] keys, char[] types)
            {
                var values = new Values(keys.Length);
                var seen = new bool[keys.Length];
                values.Line = _line;
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

                for (int k = 0; k < keys.Length; k++)
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
                if (type == TString)
                {
                    if (Peek() != '"')
                    {
                        throw Fail("'" + key + "' must be a string");
                    }

                    values.S[k] = ParseString();
                    return;
                }

                long n = ParseInteger();
                long max = type == TUInt16 ? ushort.MaxValue : uint.MaxValue;
                if (n < 0 || n > max)
                {
                    throw FailAt(startLine, "'" + key + "' out of range for its type");
                }

                values.I[k] = n;
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
}
