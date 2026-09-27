using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.World
{
    /// <summary>
    /// Hand-parses the §18.2 strict JSON subset with no package (07-conventions.md
    /// "Runtime portability" rule 7; `sim.core`'s own parser is internal, so
    /// sim.world cannot share it, 07 L5). Every failure throws
    /// <see cref="FormatException"/> naming <c>sourceName</c> and, for a syntax
    /// or shape failure, the 1-based line; for a validation failure, the field
    /// and offending id(s) (Q-030).
    /// </summary>
    internal sealed class WalkGraphLoader : IWalkGraphLoader
    {
        private const uint MaxId = uint.MaxValue;

        public WalkGraph Load(ReadOnlySpan<byte> file, string sourceName)
        {
            if (sourceName is null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }

            int pos = 0;
            int line = 1;

            SkipWs(file, ref pos, ref line);
            RequireChar(file, ref pos, ref line, sourceName, '{');

            bool hasSchema = false;
            bool hasNodes = false;
            bool hasEdges = false;
            long schemaVersion = 0;
            List<(uint Id, uint LengthMetres)> nodesRaw = new List<(uint, uint)>();
            List<(uint Id, uint From, uint To)> edgesRaw = new List<(uint, uint, uint)>();

            SkipWs(file, ref pos, ref line);
            if (TryConsumeChar(file, ref pos, ref line, '}'))
            {
                // An empty top-level object: every required key is missing, caught below.
            }
            else
            {
                while (true)
                {
                    SkipWs(file, ref pos, ref line);
                    string key = ParseString(file, ref pos, ref line, sourceName);
                    SkipWs(file, ref pos, ref line);
                    RequireChar(file, ref pos, ref line, sourceName, ':');
                    SkipWs(file, ref pos, ref line);

                    switch (key)
                    {
                        case "schema_version":
                            if (hasSchema)
                            {
                                throw Fail(sourceName, line, "duplicate key 'schema_version'");
                            }

                            hasSchema = true;
                            schemaVersion = ParseInteger(file, ref pos, ref line, sourceName);
                            break;

                        case "nodes":
                            if (hasNodes)
                            {
                                throw Fail(sourceName, line, "duplicate key 'nodes'");
                            }

                            hasNodes = true;
                            nodesRaw = ParseNodesArray(file, ref pos, ref line, sourceName);
                            break;

                        case "edges":
                            if (hasEdges)
                            {
                                throw Fail(sourceName, line, "duplicate key 'edges'");
                            }

                            hasEdges = true;
                            edgesRaw = ParseEdgesArray(file, ref pos, ref line, sourceName);
                            break;

                        default:
                            throw Fail(sourceName, line, "unknown key '" + key + "'");
                    }

                    SkipWs(file, ref pos, ref line);
                    char c = PeekChar(file, pos);
                    if (c == ',')
                    {
                        Advance(file, ref pos, ref line);
                        continue;
                    }

                    if (c == '}')
                    {
                        Advance(file, ref pos, ref line);
                        break;
                    }

                    throw Fail(sourceName, line, "expected ',' or '}'");
                }
            }

            SkipWs(file, ref pos, ref line);
            if (pos != file.Length)
            {
                throw Fail(sourceName, line, "trailing content after the document");
            }

            if (!hasSchema || !hasNodes || !hasEdges)
            {
                throw Fail(sourceName, line, "missing required key(s) 'schema_version', 'nodes' or 'edges'");
            }

            if (schemaVersion != 1)
            {
                throw Fail(sourceName, line, "schema_version must be 1, got " + schemaVersion.ToString(CultureInfo.InvariantCulture));
            }

            if (nodesRaw.Count == 0)
            {
                throw Fail(sourceName, line, "nodes must not be empty");
            }

            var nodeIds = new HashSet<uint>();
            foreach ((uint id, uint _) in nodesRaw)
            {
                if (!nodeIds.Add(id))
                {
                    throw Fail(sourceName, line, "duplicate node id " + id.ToString(CultureInfo.InvariantCulture));
                }
            }

            var edgeIds = new HashSet<uint>();
            foreach ((uint id, uint from, uint to) in edgesRaw)
            {
                if (!edgeIds.Add(id))
                {
                    throw Fail(sourceName, line, "duplicate edge id " + id.ToString(CultureInfo.InvariantCulture));
                }
            }

            var seenPairs = new Dictionary<(uint From, uint To), uint>();
            foreach ((uint id, uint from, uint to) in edgesRaw)
            {
                if (!nodeIds.Contains(from))
                {
                    throw Fail(sourceName, line, "edge " + id.ToString(CultureInfo.InvariantCulture)
                        + " 'from' references unknown node " + from.ToString(CultureInfo.InvariantCulture));
                }

                if (!nodeIds.Contains(to))
                {
                    throw Fail(sourceName, line, "edge " + id.ToString(CultureInfo.InvariantCulture)
                        + " 'to' references unknown node " + to.ToString(CultureInfo.InvariantCulture));
                }

                if (from == to)
                {
                    throw Fail(sourceName, line, "edge " + id.ToString(CultureInfo.InvariantCulture)
                        + " is a self-loop at node " + from.ToString(CultureInfo.InvariantCulture));
                }

                if (seenPairs.TryGetValue((from, to), out uint other))
                {
                    uint larger = Math.Max(id, other);
                    throw Fail(sourceName, line, "edge " + larger.ToString(CultureInfo.InvariantCulture)
                        + " duplicates an existing edge (from " + from.ToString(CultureInfo.InvariantCulture)
                        + ", to " + to.ToString(CultureInfo.InvariantCulture) + ")");
                }

                seenPairs.Add((from, to), id);
            }

            nodesRaw.Sort((a, b) => a.Id.CompareTo(b.Id));
            edgesRaw.Sort((a, b) => a.Id.CompareTo(b.Id));

            var nodes = new WalkNodeDef[nodesRaw.Count];
            for (int i = 0; i < nodesRaw.Count; i++)
            {
                nodes[i] = new WalkNodeDef(new NodeId(nodesRaw[i].Id), nodesRaw[i].LengthMetres);
            }

            var edges = new WalkEdgeDef[edgesRaw.Count];
            for (int i = 0; i < edgesRaw.Count; i++)
            {
                edges[i] = new WalkEdgeDef(new EdgeId(edgesRaw[i].Id), new NodeId(edgesRaw[i].From), new NodeId(edgesRaw[i].To));
            }

            ulong fixtureHash = Fnv1a64(file);
            return new WalkGraph(nodes, edges, fixtureHash);
        }

        private static List<(uint Id, uint LengthMetres)> ParseNodesArray(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            var result = new List<(uint, uint)>();
            RequireChar(file, ref pos, ref line, sourceName, '[');
            SkipWs(file, ref pos, ref line);
            if (TryConsumeChar(file, ref pos, ref line, ']'))
            {
                return result;
            }

            while (true)
            {
                SkipWs(file, ref pos, ref line);
                Dictionary<string, long> obj = ParseIntObject(file, ref pos, ref line, sourceName, new[] { "id", "length_metres" });
                uint id = ValidateRange(obj["id"], 1, MaxId, sourceName, line, "id");
                uint length = ValidateRange(obj["length_metres"], 0, MaxId, sourceName, line, "length_metres");
                result.Add((id, length));

                SkipWs(file, ref pos, ref line);
                char c = PeekChar(file, pos);
                if (c == ',')
                {
                    Advance(file, ref pos, ref line);
                    SkipWs(file, ref pos, ref line);
                    if (PeekChar(file, pos) == ']')
                    {
                        throw Fail(sourceName, line, "trailing comma in array");
                    }

                    continue;
                }

                if (c == ']')
                {
                    Advance(file, ref pos, ref line);
                    break;
                }

                throw Fail(sourceName, line, "expected ',' or ']'");
            }

            return result;
        }

        private static List<(uint Id, uint From, uint To)> ParseEdgesArray(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            var result = new List<(uint, uint, uint)>();
            RequireChar(file, ref pos, ref line, sourceName, '[');
            SkipWs(file, ref pos, ref line);
            if (TryConsumeChar(file, ref pos, ref line, ']'))
            {
                return result;
            }

            while (true)
            {
                SkipWs(file, ref pos, ref line);
                Dictionary<string, long> obj = ParseIntObject(file, ref pos, ref line, sourceName, new[] { "id", "from", "to" });
                uint id = ValidateRange(obj["id"], 1, MaxId, sourceName, line, "id");
                uint from = ValidateRange(obj["from"], 1, MaxId, sourceName, line, "from");
                uint to = ValidateRange(obj["to"], 1, MaxId, sourceName, line, "to");
                result.Add((id, from, to));

                SkipWs(file, ref pos, ref line);
                char c = PeekChar(file, pos);
                if (c == ',')
                {
                    Advance(file, ref pos, ref line);
                    SkipWs(file, ref pos, ref line);
                    if (PeekChar(file, pos) == ']')
                    {
                        throw Fail(sourceName, line, "trailing comma in array");
                    }

                    continue;
                }

                if (c == ']')
                {
                    Advance(file, ref pos, ref line);
                    break;
                }

                throw Fail(sourceName, line, "expected ',' or ']'");
            }

            return result;
        }

        /// <summary>Parses an object whose every value is a plain integer (node and edge objects).</summary>
        private static Dictionary<string, long> ParseIntObject(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName, string[] allowedKeys)
        {
            RequireChar(file, ref pos, ref line, sourceName, '{');
            var seen = new Dictionary<string, long>();
            SkipWs(file, ref pos, ref line);
            if (!TryConsumeChar(file, ref pos, ref line, '}'))
            {
                while (true)
                {
                    SkipWs(file, ref pos, ref line);
                    string key = ParseString(file, ref pos, ref line, sourceName);
                    if (seen.ContainsKey(key))
                    {
                        throw Fail(sourceName, line, "duplicate key '" + key + "'");
                    }

                    if (Array.IndexOf(allowedKeys, key) < 0)
                    {
                        throw Fail(sourceName, line, "unknown key '" + key + "'");
                    }

                    SkipWs(file, ref pos, ref line);
                    RequireChar(file, ref pos, ref line, sourceName, ':');
                    SkipWs(file, ref pos, ref line);
                    long value = ParseInteger(file, ref pos, ref line, sourceName);
                    seen[key] = value;

                    SkipWs(file, ref pos, ref line);
                    char c = PeekChar(file, pos);
                    if (c == ',')
                    {
                        Advance(file, ref pos, ref line);
                        continue;
                    }

                    if (c == '}')
                    {
                        Advance(file, ref pos, ref line);
                        break;
                    }

                    throw Fail(sourceName, line, "expected ',' or '}'");
                }
            }

            foreach (string k in allowedKeys)
            {
                if (!seen.ContainsKey(k))
                {
                    throw Fail(sourceName, line, "missing key '" + k + "'");
                }
            }

            return seen;
        }

        private static uint ValidateRange(long value, long min, long max, string sourceName, int line, string field)
        {
            if (value < min || value > max)
            {
                throw Fail(sourceName, line, field + " out of range: " + value.ToString(CultureInfo.InvariantCulture));
            }

            return (uint)value;
        }

        // -------------------------------------------------------------- lexer

        private static void Advance(ReadOnlySpan<byte> file, ref int pos, ref int line)
        {
            if (file[pos] == (byte)'\n')
            {
                line++;
            }

            pos++;
        }

        private static void SkipWs(ReadOnlySpan<byte> file, ref int pos, ref int line)
        {
            while (pos < file.Length)
            {
                byte b = file[pos];
                if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\n' || b == (byte)'\r')
                {
                    Advance(file, ref pos, ref line);
                }
                else
                {
                    break;
                }
            }
        }

        private static char PeekChar(ReadOnlySpan<byte> file, int pos)
        {
            return pos < file.Length ? (char)file[pos] : '\0';
        }

        private static void RequireChar(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName, char expected)
        {
            if (pos >= file.Length || file[pos] != (byte)expected)
            {
                throw Fail(sourceName, line, "expected '" + expected + "'");
            }

            Advance(file, ref pos, ref line);
        }

        private static bool TryConsumeChar(ReadOnlySpan<byte> file, ref int pos, ref int line, char expected)
        {
            if (pos < file.Length && file[pos] == (byte)expected)
            {
                Advance(file, ref pos, ref line);
                return true;
            }

            return false;
        }

        private static bool IsDigit(byte b)
        {
            return b >= (byte)'0' && b <= (byte)'9';
        }

        /// <summary>
        /// The standard JSON string grammar (08 §8.11's subset): the escapes
        /// <c>" \ / b f n r t</c> and <c>\uXXXX</c>, nothing else, and no raw
        /// control character. Any other escape (for example <c>\d</c>) is a
        /// syntax failure, not a literal backslash-then-letter.
        /// </summary>
        private static string ParseString(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            int startLine = line;
            RequireChar(file, ref pos, ref line, sourceName, '"');
            var chars = new List<char>();
            while (true)
            {
                if (pos >= file.Length)
                {
                    throw Fail(sourceName, startLine, "unterminated string");
                }

                byte b = file[pos];
                if (b == (byte)'"')
                {
                    Advance(file, ref pos, ref line);
                    break;
                }

                if (b == (byte)'\\')
                {
                    Advance(file, ref pos, ref line);
                    if (pos >= file.Length)
                    {
                        throw Fail(sourceName, startLine, "unterminated string");
                    }

                    byte e = file[pos];
                    switch ((char)e)
                    {
                        case '"': chars.Add('"'); Advance(file, ref pos, ref line); break;
                        case '\\': chars.Add('\\'); Advance(file, ref pos, ref line); break;
                        case '/': chars.Add('/'); Advance(file, ref pos, ref line); break;
                        case 'b': chars.Add('\b'); Advance(file, ref pos, ref line); break;
                        case 'f': chars.Add('\f'); Advance(file, ref pos, ref line); break;
                        case 'n': chars.Add('\n'); Advance(file, ref pos, ref line); break;
                        case 'r': chars.Add('\r'); Advance(file, ref pos, ref line); break;
                        case 't': chars.Add('\t'); Advance(file, ref pos, ref line); break;
                        case 'u':
                            Advance(file, ref pos, ref line);
                            chars.Add((char)ParseHex4(file, ref pos, ref line, sourceName, startLine));
                            break;
                        default:
                            throw Fail(sourceName, startLine, "invalid escape '\\" + (char)e + "'");
                    }

                    continue;
                }

                if (b < 0x20)
                {
                    throw Fail(sourceName, startLine, "control character in string literal");
                }

                chars.Add((char)b);
                Advance(file, ref pos, ref line);
            }

            return new string(chars.ToArray());
        }

        private static int ParseHex4(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName, int startLine)
        {
            if (pos + 4 > file.Length)
            {
                throw Fail(sourceName, startLine, "invalid \\u escape");
            }

            int code = 0;
            for (int k = 0; k < 4; k++)
            {
                int d = HexDigit(file[pos]);
                if (d < 0)
                {
                    throw Fail(sourceName, startLine, "invalid \\u escape");
                }

                code = (code << 4) | d;
                Advance(file, ref pos, ref line);
            }

            return code;
        }

        private static int HexDigit(byte b)
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

        private static long ParseInteger(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            int startLine = line;
            if (pos >= file.Length)
            {
                throw Fail(sourceName, startLine, "expected a number");
            }

            bool neg = false;
            if (file[pos] == (byte)'-')
            {
                neg = true;
                Advance(file, ref pos, ref line);
            }

            if (pos >= file.Length || !IsDigit(file[pos]))
            {
                throw Fail(sourceName, startLine, "expected a digit");
            }

            long value;
            if (file[pos] == (byte)'0')
            {
                if (neg)
                {
                    throw Fail(sourceName, startLine, "'-0' is not a valid integer");
                }

                Advance(file, ref pos, ref line);
                value = 0;
                if (pos < file.Length && IsDigit(file[pos]))
                {
                    throw Fail(sourceName, startLine, "a leading zero is not a valid integer");
                }
            }
            else
            {
                // Hand-rolled overflow guard (08 §8.3, CLAUDE.md): no `checked`
                // block outside Fx. long.MaxValue has 19 digits, comfortably
                // above any field's uint32 range, so a manual pre-multiply
                // bound check is enough without ever risking a silent wrap.
                value = 0;
                while (pos < file.Length && IsDigit(file[pos]))
                {
                    int digit = file[pos] - (byte)'0';
                    if (value > (long.MaxValue - digit) / 10)
                    {
                        throw Fail(sourceName, startLine, "integer out of range");
                    }

                    value = (value * 10) + digit;
                    Advance(file, ref pos, ref line);
                }

                if (neg)
                {
                    value = -value;
                }
            }

            if (pos < file.Length && (file[pos] == (byte)'.' || file[pos] == (byte)'e' || file[pos] == (byte)'E'))
            {
                throw Fail(sourceName, startLine, "fractional and exponent numbers are not allowed");
            }

            return value;
        }

        private static FormatException Fail(string sourceName, int line, string detail)
        {
            return new FormatException(sourceName + ": " + detail + " (line " + line.ToString(CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>FNV-1a-64 (08 §8.9 constants) over the exact bytes, no length prefix.</summary>
        private static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
        {
            ulong h = 0xCBF29CE484222325UL;
            for (int i = 0; i < bytes.Length; i++)
            {
                unchecked
                {
                    h = (h ^ bytes[i]) * 0x100000001B3UL;
                }
            }

            return h;
        }
    }
}
