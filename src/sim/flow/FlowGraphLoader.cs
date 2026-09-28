using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;

namespace AirportSim.Sim.Flow
{
    /// <summary>
    /// Hand-parses the §9.11 flow graph file (Q-032): the same strict JSON subset
    /// as `sim.world`'s §18.2 loader, with no package (07-conventions.md "Runtime
    /// portability" rule 7), and its own line tracking, since sim.core's parser
    /// is internal (07 L5) and untracked by line. A syntax or shape failure
    /// throws naming the 1-based line; a validation failure throws naming the
    /// offending node id in decimal.
    /// </summary>
    internal sealed class FlowGraphLoader : IFlowGraphLoader
    {
        private const long MaxNodeId = uint.MaxValue;

        public FlowGraph Load(ReadOnlySpan<byte> file, string sourceName, IWorldSystem world)
        {
            if (sourceName is null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }
            if (world is null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            int pos = 0;
            int line = 1;

            SkipWs(file, ref pos, ref line);
            RequireChar(file, ref pos, ref line, sourceName, '{');

            bool hasSchema = false;
            bool hasNodes = false;
            long schemaVersion = 0;
            List<RawNode> rawNodes = new List<RawNode>();

            SkipWs(file, ref pos, ref line);
            if (!TryConsumeChar(file, ref pos, ref line, '}'))
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
                                throw FailShape(sourceName, line, "duplicate key 'schema_version'");
                            }

                            hasSchema = true;
                            schemaVersion = ParseInteger(file, ref pos, ref line, sourceName);
                            break;

                        case "nodes":
                            if (hasNodes)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'nodes'");
                            }

                            hasNodes = true;
                            rawNodes = ParseNodesArray(file, ref pos, ref line, sourceName);
                            break;

                        default:
                            throw FailShape(sourceName, line, "unknown key '" + key + "'");
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

                    throw FailShape(sourceName, line, "expected ',' or '}'");
                }
            }

            SkipWs(file, ref pos, ref line);
            if (pos != file.Length)
            {
                throw FailShape(sourceName, line, "trailing content after the document");
            }

            if (!hasSchema || !hasNodes)
            {
                throw FailShape(sourceName, line, "missing required key(s) 'schema_version' or 'nodes'");
            }

            if (schemaVersion != 1)
            {
                throw FailShape(sourceName, line, "schema_version must be 1, got " + schemaVersion.ToString(CultureInfo.InvariantCulture));
            }

            return Validate(rawNodes, sourceName, world);
        }

        // -------------------------------------------------------------- validation

        private static FlowGraph Validate(List<RawNode> rawNodes, string sourceName, IWorldSystem world)
        {
            var byId = new Dictionary<uint, RawNode>();
            foreach (RawNode n in rawNodes)
            {
                if (byId.ContainsKey(n.Id))
                {
                    throw FailNode(sourceName, n.Id, "duplicate node definition");
                }

                byId.Add(n.Id, n);
            }

            IReadOnlyList<NodeId> worldNodes = world.Nodes();
            var worldNodeSet = new HashSet<uint>();
            for (int i = 0; i < worldNodes.Count; i++)
            {
                worldNodeSet.Add(worldNodes[i].Value);
            }

            foreach (uint id in worldNodeSet)
            {
                if (!byId.ContainsKey(id))
                {
                    throw FailNode(sourceName, id, "no flow definition for world node");
                }
            }

            foreach (RawNode n in rawNodes)
            {
                if (!worldNodeSet.Contains(n.Id))
                {
                    throw FailNode(sourceName, n.Id, "definition of unknown world node");
                }
            }

            var defs = new FlowNodeDef[worldNodes.Count];
            for (int i = 0; i < worldNodes.Count; i++)
            {
                RawNode n = byId[worldNodes[i].Value];
                defs[i] = new FlowNodeDef(worldNodes[i], n.Kind, (int)n.ServerCount, (int)n.ServersOpen, new ContentId(n.QueueProfile ?? string.Empty));
            }

            // Every Source can reach at least one Gate.
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].Kind != NodeKind.Source)
                {
                    continue;
                }

                bool reachesGate = false;
                for (int g = 0; g < defs.Length && !reachesGate; g++)
                {
                    if (defs[g].Kind == NodeKind.Gate && world.CanReach(defs[i].Id, defs[g].Id))
                    {
                        reachesGate = true;
                    }
                }

                if (!reachesGate)
                {
                    throw FailNode(sourceName, defs[i].Id.Value, "Source reaches no Gate");
                }
            }

            // A Gate has at least one outbound edge to a Sink; a Sink has no outbound edge.
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].Kind == NodeKind.Gate)
                {
                    IReadOnlyList<EdgeId> outs = world.OutEdges(defs[i].Id);
                    bool hasSink = false;
                    for (int e = 0; e < outs.Count && !hasSink; e++)
                    {
                        NodeId to = world.EdgeTo(outs[e]);
                        if (byId.TryGetValue(to.Value, out RawNode target) && target.Kind == NodeKind.Sink)
                        {
                            hasSink = true;
                        }
                    }

                    if (!hasSink)
                    {
                        throw FailNode(sourceName, defs[i].Id.Value, "Gate has no outbound edge to a Sink");
                    }
                }

                if (defs[i].Kind == NodeKind.Sink)
                {
                    if (world.OutEdges(defs[i].Id).Count != 0)
                    {
                        throw FailNode(sourceName, defs[i].Id.Value, "Sink has an outbound edge");
                    }
                }
            }

            return new FlowGraph(defs);
        }

        // -------------------------------------------------------------- node parsing

        private struct RawNode
        {
            public uint Id;
            public NodeKind Kind;
            public long ServerCount;
            public long ServersOpen;
            public string? QueueProfile;
        }

        private static List<RawNode> ParseNodesArray(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            var result = new List<RawNode>();
            RequireChar(file, ref pos, ref line, sourceName, '[');
            SkipWs(file, ref pos, ref line);
            if (TryConsumeChar(file, ref pos, ref line, ']'))
            {
                return result;
            }

            while (true)
            {
                SkipWs(file, ref pos, ref line);
                result.Add(ParseNodeObject(file, ref pos, ref line, sourceName));

                SkipWs(file, ref pos, ref line);
                char c = PeekChar(file, pos);
                if (c == ',')
                {
                    Advance(file, ref pos, ref line);
                    continue;
                }

                if (c == ']')
                {
                    Advance(file, ref pos, ref line);
                    break;
                }

                throw FailShape(sourceName, line, "expected ',' or ']'");
            }

            return result;
        }

        /// <summary>A switch, not a static lookup table (CLAUDE.md: no mutable statics).</summary>
        private static bool TryParseKind(string text, out NodeKind kind)
        {
            switch (text)
            {
                case "source": kind = NodeKind.Source; return true;
                case "corridor": kind = NodeKind.Corridor; return true;
                case "hall": kind = NodeKind.Hall; return true;
                case "queue": kind = NodeKind.Queue; return true;
                case "gate": kind = NodeKind.Gate; return true;
                case "sink": kind = NodeKind.Sink; return true;
                default:
                    kind = default;
                    return false;
            }
        }

        private static RawNode ParseNodeObject(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            int startLine = line;
            RequireChar(file, ref pos, ref line, sourceName, '{');

            bool hasId = false;
            bool hasKind = false;
            bool hasServerCount = false;
            bool hasServersOpen = false;
            bool hasQueueProfile = false;
            long id = 0;
            NodeKind kind = default;
            long serverCount = 0;
            long serversOpen = 0;
            string? queueProfile = null;

            SkipWs(file, ref pos, ref line);
            if (!TryConsumeChar(file, ref pos, ref line, '}'))
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
                        case "id":
                            if (hasId)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'id'");
                            }

                            hasId = true;
                            id = ParseInteger(file, ref pos, ref line, sourceName);
                            break;

                        case "kind":
                            if (hasKind)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'kind'");
                            }

                            hasKind = true;
                            string kindText = ParseString(file, ref pos, ref line, sourceName);
                            if (!TryParseKind(kindText, out kind))
                            {
                                throw FailShape(sourceName, line, "unknown node kind '" + kindText + "'");
                            }

                            break;

                        case "server_count":
                            if (hasServerCount)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'server_count'");
                            }

                            hasServerCount = true;
                            serverCount = ParseInteger(file, ref pos, ref line, sourceName);
                            break;

                        case "servers_open":
                            if (hasServersOpen)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'servers_open'");
                            }

                            hasServersOpen = true;
                            serversOpen = ParseInteger(file, ref pos, ref line, sourceName);
                            break;

                        case "queue_profile":
                            if (hasQueueProfile)
                            {
                                throw FailShape(sourceName, line, "duplicate key 'queue_profile'");
                            }

                            hasQueueProfile = true;
                            queueProfile = ParseString(file, ref pos, ref line, sourceName);
                            break;

                        default:
                            throw FailShape(sourceName, line, "unknown key '" + key + "'");
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

                    throw FailShape(sourceName, line, "expected ',' or '}'");
                }
            }

            if (!hasId)
            {
                throw FailShape(sourceName, startLine, "missing key 'id'");
            }

            if (id < 1 || id > MaxNodeId)
            {
                throw FailShape(sourceName, startLine, "id out of range: " + id.ToString(CultureInfo.InvariantCulture));
            }

            if (!hasKind)
            {
                throw FailShape(sourceName, startLine, "missing key 'kind'");
            }

            if (kind == NodeKind.Queue)
            {
                if (!hasServerCount || !hasServersOpen || !hasQueueProfile)
                {
                    throw FailShape(sourceName, startLine, "a queue node requires 'server_count', 'servers_open' and 'queue_profile'");
                }

                if (serverCount < 1 || serverCount > int.MaxValue)
                {
                    throw FailShape(sourceName, startLine, "server_count out of range: " + serverCount.ToString(CultureInfo.InvariantCulture));
                }

                if (serversOpen < 0 || serversOpen > int.MaxValue)
                {
                    throw FailShape(sourceName, startLine, "servers_open out of range: " + serversOpen.ToString(CultureInfo.InvariantCulture));
                }

                if (serversOpen > serverCount)
                {
                    throw FailNode(sourceName, (uint)id, "servers_open (" + serversOpen.ToString(CultureInfo.InvariantCulture) +
                        ") exceeds server_count (" + serverCount.ToString(CultureInfo.InvariantCulture) + ")");
                }
            }
            else
            {
                if (hasServerCount || hasServersOpen || hasQueueProfile)
                {
                    throw FailShape(sourceName, startLine, "only a queue node may have 'server_count', 'servers_open' or 'queue_profile'");
                }
            }

            return new RawNode
            {
                Id = (uint)id,
                Kind = kind,
                ServerCount = serverCount,
                ServersOpen = serversOpen,
                QueueProfile = queueProfile,
            };
        }

        // -------------------------------------------------------------- lexer (byte, UTF-8 aware, line-tracked)

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
                throw FailShape(sourceName, line, "expected '" + expected + "'");
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

        private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

        /// <summary>
        /// Parses a quoted JSON string, UTF-8 decoded byte by byte. Only the 8
        /// standard escapes plus <c>\uXXXX</c> are allowed (Q-033); a lone (unpaired)
        /// UTF-16 surrogate from <c>\u</c>, a raw control character, or invalid UTF-8
        /// is a shape failure.
        /// </summary>
        private static string ParseString(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            int startLine = line;
            RequireChar(file, ref pos, ref line, sourceName, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= file.Length)
                {
                    throw FailShape(sourceName, startLine, "unterminated string");
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
                        throw FailShape(sourceName, startLine, "unterminated escape sequence");
                    }

                    byte e = file[pos];
                    switch (e)
                    {
                        case (byte)'"': sb.Append('"'); Advance(file, ref pos, ref line); break;
                        case (byte)'\\': sb.Append('\\'); Advance(file, ref pos, ref line); break;
                        case (byte)'/': sb.Append('/'); Advance(file, ref pos, ref line); break;
                        case (byte)'b': sb.Append('\b'); Advance(file, ref pos, ref line); break;
                        case (byte)'f': sb.Append('\f'); Advance(file, ref pos, ref line); break;
                        case (byte)'n': sb.Append('\n'); Advance(file, ref pos, ref line); break;
                        case (byte)'r': sb.Append('\r'); Advance(file, ref pos, ref line); break;
                        case (byte)'t': sb.Append('\t'); Advance(file, ref pos, ref line); break;
                        case (byte)'u':
                            Advance(file, ref pos, ref line);
                            int code = ParseHex4(file, ref pos, ref line, sourceName, startLine);
                            if (code >= 0xD800 && code <= 0xDFFF)
                            {
                                throw FailShape(sourceName, startLine, "a lone UTF-16 surrogate in a \\u escape is not allowed");
                            }

                            sb.Append((char)code);
                            break;
                        default:
                            throw FailShape(sourceName, startLine, "invalid escape '\\" + (char)e + "'");
                    }

                    continue;
                }

                if (b < 0x20)
                {
                    throw FailShape(sourceName, startLine, "raw control character in string literal");
                }

                int codepoint = DecodeUtf8(file, ref pos, sourceName, startLine);
                if (codepoint <= 0xFFFF)
                {
                    sb.Append((char)codepoint);
                }
                else
                {
                    codepoint -= 0x10000;
                    sb.Append((char)(0xD800 + (codepoint >> 10)));
                    sb.Append((char)(0xDC00 + (codepoint & 0x3FF)));
                }
            }

            return sb.ToString();
        }

        /// <summary>Decodes one UTF-8 code point starting at <paramref name="pos"/>, advancing past it (no newline possible mid-sequence).</summary>
        private static int DecodeUtf8(ReadOnlySpan<byte> file, ref int pos, string sourceName, int startLine)
        {
            byte b0 = file[pos];
            int extra;
            int codepoint;
            int min;
            if ((b0 & 0x80) == 0)
            {
                pos++;
                return b0;
            }
            else if ((b0 & 0xE0) == 0xC0)
            {
                extra = 1;
                codepoint = b0 & 0x1F;
                min = 0x80;
            }
            else if ((b0 & 0xF0) == 0xE0)
            {
                extra = 2;
                codepoint = b0 & 0x0F;
                min = 0x800;
            }
            else if ((b0 & 0xF8) == 0xF0)
            {
                extra = 3;
                codepoint = b0 & 0x07;
                min = 0x10000;
            }
            else
            {
                throw FailShape(sourceName, startLine, "invalid UTF-8 lead byte");
            }

            if (pos + extra >= file.Length)
            {
                throw FailShape(sourceName, startLine, "truncated UTF-8 sequence");
            }

            for (int k = 1; k <= extra; k++)
            {
                byte cont = file[pos + k];
                if ((cont & 0xC0) != 0x80)
                {
                    throw FailShape(sourceName, startLine, "invalid UTF-8 continuation byte");
                }

                codepoint = (codepoint << 6) | (cont & 0x3F);
            }

            pos += 1 + extra;

            if (codepoint < min || codepoint > 0x10FFFF || (codepoint >= 0xD800 && codepoint <= 0xDFFF))
            {
                throw FailShape(sourceName, startLine, "invalid UTF-8 code point");
            }

            return codepoint;
        }

        private static int ParseHex4(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName, int startLine)
        {
            if (pos + 4 > file.Length)
            {
                throw FailShape(sourceName, startLine, "invalid \\u escape");
            }

            int code = 0;
            for (int k = 0; k < 4; k++)
            {
                int d = HexDigit(file[pos]);
                if (d < 0)
                {
                    throw FailShape(sourceName, startLine, "invalid \\u escape");
                }

                code = (code << 4) | d;
                Advance(file, ref pos, ref line);
            }

            return code;
        }

        private static int HexDigit(byte c)
        {
            if (c >= (byte)'0' && c <= (byte)'9')
            {
                return c - (byte)'0';
            }

            if (c >= (byte)'a' && c <= (byte)'f')
            {
                return c - (byte)'a' + 10;
            }

            if (c >= (byte)'A' && c <= (byte)'F')
            {
                return c - (byte)'A' + 10;
            }

            return -1;
        }

        private static long ParseInteger(ReadOnlySpan<byte> file, ref int pos, ref int line, string sourceName)
        {
            int startLine = line;
            if (pos >= file.Length)
            {
                throw FailShape(sourceName, startLine, "expected a number");
            }

            bool neg = false;
            if (file[pos] == (byte)'-')
            {
                neg = true;
                Advance(file, ref pos, ref line);
            }

            if (pos >= file.Length || !IsDigit(file[pos]))
            {
                throw FailShape(sourceName, startLine, "expected a digit");
            }

            long value;
            if (file[pos] == (byte)'0')
            {
                if (neg)
                {
                    throw FailShape(sourceName, startLine, "'-0' is not a valid integer");
                }

                Advance(file, ref pos, ref line);
                value = 0;
                if (pos < file.Length && IsDigit(file[pos]))
                {
                    throw FailShape(sourceName, startLine, "a leading zero is not a valid integer");
                }
            }
            else
            {
                value = 0;
                while (pos < file.Length && IsDigit(file[pos]))
                {
                    int digit = file[pos] - (byte)'0';
                    if (value > (long.MaxValue - digit) / 10)
                    {
                        throw FailShape(sourceName, startLine, "integer out of range");
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
                throw FailShape(sourceName, startLine, "fractional and exponent numbers are not allowed");
            }

            return value;
        }

        private static FormatException FailShape(string sourceName, int line, string detail)
        {
            return new FormatException(sourceName + ": " + detail + " (line " + line.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static FormatException FailNode(string sourceName, uint nodeId, string detail)
        {
            return new FormatException(sourceName + ": " + detail + " (node " + nodeId.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }
}
