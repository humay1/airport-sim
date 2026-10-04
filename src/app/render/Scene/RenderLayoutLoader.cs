using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;

namespace AirportSim.App.Render
{
    /// <summary>
    /// Hand-parses the layout file (15 §15.4, Q-094): the strict JSON subset of 08 §8.11 with 18 §18.2's rules,
    /// then validates it in the order the spec gives.
    /// </summary>
    internal sealed class RenderLayoutLoader : IRenderLayoutLoader
    {
        private static readonly string[] TaxiKeys = { "node", "x", "y" };
        private static readonly long[] TaxiMin = { ushort.MinValue, int.MinValue, int.MinValue };
        private static readonly long[] TaxiMax = { ushort.MaxValue, int.MaxValue, int.MaxValue };

        private static readonly string[] RunwayKeys = { "runway", "x0", "y0", "x1", "y1", "width" };
        private static readonly long[] RunwayMin = { ushort.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private static readonly long[] RunwayMax = { ushort.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };

        private static readonly string[] BoxKeys = { "node", "min_x", "min_y", "max_x", "max_y", "fill_capacity" };
        private static readonly long[] BoxMin = { uint.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private static readonly long[] BoxMax = { uint.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };

        public RenderLayout Load(ReadOnlySpan<byte> file, string sourceName, in AirsideLayout? airside)
        {
            if (sourceName == null)
            {
                throw new ArgumentNullException(nameof(sourceName));
            }

            var parser = new Parser(file.ToArray(), sourceName);
            Parsed p = parser.ParseFile();
            return Validate(p, sourceName, airside);
        }

        private static FormatException Invalid(string source, string what)
        {
            return new FormatException(source + ": " + what);
        }

        private static RenderLayout Validate(Parsed p, string source, in AirsideLayout? airside)
        {
            // 1. sizes, in key order.
            string[] sizeKeys = { "stand_size", "aircraft_size", "agent_size", "taxiway_width" };
            long[] sizes = { p.StandSize, p.AircraftSize, p.AgentSize, p.TaxiwayWidth };
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i] <= 0)
                {
                    throw Invalid(source, sizeKeys[i] + " must be greater than 0");
                }
            }

            var taxi = p.Taxi.ToArray();
            var runways = p.Runways.ToArray();
            var boxes = p.Boxes.ToArray();
            Array.Sort(taxi, (a, b) => a.Node.Value.CompareTo(b.Node.Value));
            Array.Sort(runways, (a, b) => a.Runway.Value.CompareTo(b.Runway.Value));
            Array.Sort(boxes, (a, b) => a.Node.Value.CompareTo(b.Node.Value));

            // 2. unique taxi node and runway ids.
            for (int i = 1; i < taxi.Length; i++)
            {
                if (taxi[i].Node.Value == taxi[i - 1].Node.Value)
                {
                    throw Invalid(source, "taxi node " + Dec(taxi[i].Node.Value) + " is positioned more than once");
                }
            }

            for (int i = 1; i < runways.Length; i++)
            {
                if (runways[i].Runway.Value == runways[i - 1].Runway.Value)
                {
                    throw Invalid(source, "runway " + Dec(runways[i].Runway.Value) + " has geometry more than once");
                }
            }

            // 3. runway widths, then the boxes in ascending node id.
            for (int i = 0; i < runways.Length; i++)
            {
                if (runways[i].Width <= 0)
                {
                    throw Invalid(source, "runway " + Dec(runways[i].Runway.Value) + " width must be greater than 0");
                }
            }

            for (int i = 0; i < boxes.Length; i++)
            {
                FlowNodeBox b = boxes[i];
                string id = Dec(b.Node.Value);
                if (b.Node.Value < 1)
                {
                    throw Invalid(source, "flow node " + id + " must have an id of at least 1");
                }

                if (i > 0 && boxes[i - 1].Node.Value == b.Node.Value)
                {
                    throw Invalid(source, "flow node " + id + " has a box more than once");
                }

                if (b.MinX >= b.MaxX || b.MinY >= b.MaxY)
                {
                    throw Invalid(source, "flow node " + id + " box must have min_x < max_x and min_y < max_y");
                }

                if (b.FillCapacity <= 0)
                {
                    throw Invalid(source, "flow node " + id + " fill_capacity must be greater than 0");
                }
            }

            // 4. against the airside layout, when given.
            if (airside.HasValue)
            {
                CheckAgainstAirside(source, airside.Value, taxi, runways);
            }

            return new RenderLayout(taxi, runways, boxes, (int)p.StandSize, (int)p.AircraftSize, (int)p.AgentSize, (int)p.TaxiwayWidth);
        }

        private static void CheckAgainstAirside(string source, AirsideLayout airside, TaxiNodePosition[] taxi, RunwayGeometry[] runways)
        {
            var nodeIds = new HashSet<ushort>();
            for (int i = 0; i < airside.Nodes.Count; i++)
            {
                nodeIds.Add(airside.Nodes[i].Id.Value);
            }

            for (int i = 0; i < taxi.Length; i++)
            {
                if (!nodeIds.Contains(taxi[i].Node.Value))
                {
                    throw Invalid(source, "position names taxi node " + Dec(taxi[i].Node.Value) + ", which the airside layout lacks");
                }
            }

            var positioned = new HashSet<ushort>();
            for (int i = 0; i < taxi.Length; i++)
            {
                positioned.Add(taxi[i].Node.Value);
            }

            int missing = -1;
            for (int i = 0; i < airside.Nodes.Count; i++)
            {
                ushort id = airside.Nodes[i].Id.Value;
                if (!positioned.Contains(id) && (missing < 0 || id < missing))
                {
                    missing = id;
                }
            }

            if (missing >= 0)
            {
                throw Invalid(source, "taxi node " + Dec((uint)missing) + " has no position");
            }

            var runwayIds = new HashSet<ushort>();
            for (int i = 0; i < airside.Runways.Count; i++)
            {
                runwayIds.Add(airside.Runways[i].Id.Value);
            }

            for (int i = 0; i < runways.Length; i++)
            {
                if (!runwayIds.Contains(runways[i].Runway.Value))
                {
                    throw Invalid(source, "geometry names runway " + Dec(runways[i].Runway.Value) + ", which the airside layout lacks");
                }
            }

            var shaped = new HashSet<ushort>();
            for (int i = 0; i < runways.Length; i++)
            {
                shaped.Add(runways[i].Runway.Value);
            }

            missing = -1;
            for (int i = 0; i < airside.Runways.Count; i++)
            {
                ushort id = airside.Runways[i].Id.Value;
                if (!shaped.Contains(id) && (missing < 0 || id < missing))
                {
                    missing = id;
                }
            }

            if (missing >= 0)
            {
                throw Invalid(source, "runway " + Dec((uint)missing) + " has no geometry");
            }
        }

        private static string Dec(uint v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class Parsed
        {
            public readonly List<TaxiNodePosition> Taxi = new List<TaxiNodePosition>();
            public readonly List<RunwayGeometry> Runways = new List<RunwayGeometry>();
            public readonly List<FlowNodeBox> Boxes = new List<FlowNodeBox>();
            public long StandSize;
            public long AircraftSize;
            public long AgentSize;
            public long TaxiwayWidth;
        }

        // Strict-subset JSON reader: objects, arrays, strings without escapes, and integers only.
        private sealed class Parser
        {
            private readonly byte[] _data;
            private readonly string _source;
            private int _pos;
            private int _line = 1;

            public Parser(byte[] data, string source)
            {
                _data = data;
                _source = source;
            }

            public Parsed ParseFile()
            {
                if (_data.Length >= 3 && _data[0] == 0xEF && _data[1] == 0xBB && _data[2] == 0xBF)
                {
                    throw Fail("a byte order mark is not allowed");
                }

                var result = new Parsed();
                string[] keys =
                {
                    "schema_version", "taxi_nodes", "runways", "flow_nodes",
                    "stand_size", "aircraft_size", "agent_size", "taxiway_width",
                };
                var seen = new bool[keys.Length];

                SkipSpace();
                Expect('{');
                SkipSpace();
                if (Peek() == '}')
                {
                    throw Fail("missing key \"" + keys[0] + "\"");
                }

                while (true)
                {
                    SkipSpace();
                    string key = ReadString();
                    int index = Array.IndexOf(keys, key);
                    if (index < 0)
                    {
                        throw Fail("unknown key \"" + key + "\"");
                    }

                    if (seen[index])
                    {
                        throw Fail("duplicate key \"" + key + "\"");
                    }

                    seen[index] = true;
                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    switch (index)
                    {
                        case 0:
                            if (ReadInteger(1, 1, key) != 1)
                            {
                                throw Fail("schema_version must be 1");
                            }

                            break;
                        case 1:
                            ReadArray(TaxiKeys, TaxiMin, TaxiMax, v => result.Taxi.Add(new TaxiNodePosition(new TaxiNodeId((ushort)v[0]), (int)v[1], (int)v[2])));
                            break;
                        case 2:
                            ReadArray(RunwayKeys, RunwayMin, RunwayMax, v => result.Runways.Add(new RunwayGeometry(new RunwayId((ushort)v[0]), (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5])));
                            break;
                        case 3:
                            ReadArray(BoxKeys, BoxMin, BoxMax, v => result.Boxes.Add(new FlowNodeBox(new NodeId((uint)v[0]), (int)v[1], (int)v[2], (int)v[3], (int)v[4], (int)v[5])));
                            break;
                        case 4:
                            result.StandSize = ReadInteger(int.MinValue, int.MaxValue, key);
                            break;
                        case 5:
                            result.AircraftSize = ReadInteger(int.MinValue, int.MaxValue, key);
                            break;
                        case 6:
                            result.AgentSize = ReadInteger(int.MinValue, int.MaxValue, key);
                            break;
                        default:
                            result.TaxiwayWidth = ReadInteger(int.MinValue, int.MaxValue, key);
                            break;
                    }

                    SkipSpace();
                    int c = Next();
                    if (c == '}')
                    {
                        break;
                    }

                    if (c != ',')
                    {
                        throw Fail("expected ',' or '}'");
                    }
                }

                for (int i = 0; i < keys.Length; i++)
                {
                    if (!seen[i])
                    {
                        throw Fail("missing key \"" + keys[i] + "\"");
                    }
                }

                SkipSpace();
                if (_pos != _data.Length)
                {
                    throw Fail("unexpected content after the top-level object");
                }

                return result;
            }

            private void ReadArray(string[] keys, long[] min, long[] max, Action<long[]> add)
            {
                Expect('[');
                SkipSpace();
                if (Peek() == ']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    SkipSpace();
                    add(ReadRow(keys, min, max));
                    SkipSpace();
                    int c = Next();
                    if (c == ']')
                    {
                        return;
                    }

                    if (c != ',')
                    {
                        throw Fail("expected ',' or ']'");
                    }
                }
            }

            private long[] ReadRow(string[] keys, long[] min, long[] max)
            {
                var values = new long[keys.Length];
                var seen = new bool[keys.Length];
                Expect('{');
                SkipSpace();
                if (Peek() == '}')
                {
                    throw Fail("missing key \"" + keys[0] + "\"");
                }

                while (true)
                {
                    SkipSpace();
                    string key = ReadString();
                    int index = Array.IndexOf(keys, key);
                    if (index < 0)
                    {
                        throw Fail("unknown key \"" + key + "\"");
                    }

                    if (seen[index])
                    {
                        throw Fail("duplicate key \"" + key + "\"");
                    }

                    seen[index] = true;
                    SkipSpace();
                    Expect(':');
                    SkipSpace();
                    values[index] = ReadInteger(min[index], max[index], key);
                    SkipSpace();
                    int c = Next();
                    if (c == '}')
                    {
                        break;
                    }

                    if (c != ',')
                    {
                        throw Fail("expected ',' or '}'");
                    }
                }

                for (int i = 0; i < keys.Length; i++)
                {
                    if (!seen[i])
                    {
                        throw Fail("missing key \"" + keys[i] + "\"");
                    }
                }

                return values;
            }

            private long ReadInteger(long min, long max, string key)
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
                    throw Fail("expected an integer for \"" + key + "\"");
                }

                if (first == '0' && negative)
                {
                    throw Fail("\"" + key + "\" is not a valid integer");
                }

                long magnitude = 0;
                int digits = 0;
                while (Peek() >= '0' && Peek() <= '9')
                {
                    if (first == '0' && digits > 0)
                    {
                        throw Fail("\"" + key + "\" has a leading zero");
                    }

                    if (++digits > 18)
                    {
                        throw Fail("\"" + key + "\" is out of range");
                    }

                    magnitude = (magnitude * 10) + (_data[_pos] - '0');
                    _pos++;
                }

                int after = Peek();
                if (after == '.' || after == 'e' || after == 'E')
                {
                    throw Fail("\"" + key + "\" must be an integer, with no fraction or exponent");
                }

                long value = negative ? -magnitude : magnitude;
                if (value < min || value > max)
                {
                    throw Fail("\"" + key + "\" is out of range");
                }

                return value;
            }

            private string ReadString()
            {
                if (Peek() != '"')
                {
                    throw Fail("expected a string key");
                }

                _pos++;
                int start = _pos;
                while (true)
                {
                    int c = Next();
                    if (c < 0)
                    {
                        throw Fail("unterminated string");
                    }

                    if (c == '"')
                    {
                        break;
                    }

                    if (c == '\\' || c < 0x20)
                    {
                        throw Fail("escapes and control characters are not allowed in strings");
                    }
                }

                return System.Text.Encoding.UTF8.GetString(_data, start, _pos - 1 - start);
            }

            private void SkipSpace()
            {
                while (_pos < _data.Length)
                {
                    byte c = _data[_pos];
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
                return _pos < _data.Length ? _data[_pos] : -1;
            }

            private int Next()
            {
                return _pos < _data.Length ? _data[_pos++] : -1;
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
                return new FormatException(_source + ": line " + _line.ToString(CultureInfo.InvariantCulture) + ": " + what);
            }
        }
    }
}
