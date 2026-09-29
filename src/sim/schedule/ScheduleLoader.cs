using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Schedule
{
    /// <summary>
    /// The one <see cref="IScheduleLoader"/> implementation. Spec: 11-interfaces-schedule.md
    /// §11.4: strict CSV, no quoting, no comments, hard failure naming file and line.
    /// </summary>
    internal sealed class ScheduleLoader : IScheduleLoader
    {
        // 11-interfaces-schedule.md §11.2: FLIGHT_ID_DAY_STRIDE - 1 (Q-039).
        private const int MaxFixtureRows = 99999;

        // A string is immutable (07-conventions.md L10); the header's expected bytes are
        // recomputed from it locally in Load rather than cached in a static byte[], which
        // would be mutable shared state (review finding 9).
        private const string HeaderText =
            "flight_ref,day,repeat_daily,movement,airline,aircraft_type,sched_hhmm,rotation_ref,min_turnaround_minutes,pax,pax_profile,hold_bag_permille,assist_permille,entry_node";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public ScheduleTable Load(ReadOnlySpan<byte> csv, string sourceName)
        {
            byte[] bytes = csv.ToArray();
            ulong fixtureHash = Fnv1a64(bytes);

            if (bytes.Length == 0)
            {
                throw Fail(sourceName, 1, "the file is empty");
            }

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                throw Fail(sourceName, 1, "a UTF-8 byte order mark is not allowed");
            }

            int newlineCount = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n')
                {
                    newlineCount++;
                }
            }

            if (bytes[bytes.Length - 1] != (byte)'\n')
            {
                throw Fail(sourceName, newlineCount + 1, "the file must end with a final newline");
            }

            List<byte[]> lines = SplitLines(bytes);
            // The mandatory final newline produces one trailing empty element; drop it.
            lines.RemoveAt(lines.Count - 1);

            byte[] headerBytes = new UTF8Encoding(false).GetBytes(HeaderText);
            if (lines.Count == 0 || !BytesEqual(lines[0], headerBytes))
            {
                throw Fail(sourceName, 1, "the header must equal the fixed schema, byte for byte");
            }

            var rows = new List<RowData>();
            var byRef = new Dictionary<string, RowData>(StringComparer.Ordinal);
            var airlineHashes = new Dictionary<uint, string>();

            for (int i = 1; i < lines.Count; i++)
            {
                int lineNumber = i + 1;
                byte[] lineBytes = lines[i];

                if (lineBytes.Length == 0)
                {
                    throw Fail(sourceName, lineNumber, "blank lines are not allowed");
                }

                for (int b = 0; b < lineBytes.Length; b++)
                {
                    if (lineBytes[b] == (byte)'\r')
                    {
                        throw Fail(sourceName, lineNumber, "CRLF line endings are not allowed; the fixture uses LF only");
                    }
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(lineBytes);
                }
                catch (DecoderFallbackException)
                {
                    throw Fail(sourceName, lineNumber, "the line is not valid UTF-8");
                }

                RowData row = ParseRow(sourceName, lineNumber, text);

                if (byRef.ContainsKey(row.FlightRef))
                {
                    throw Fail(sourceName, lineNumber, "duplicate flight_ref: " + row.FlightRef);
                }

                byRef.Add(row.FlightRef, row);
                rows.Add(row);

                uint airlineHash = Fnv1a32(row.AirlineCode);
                if (airlineHashes.TryGetValue(airlineHash, out string? existingCode) && !string.Equals(existingCode, row.AirlineCode, StringComparison.Ordinal))
                {
                    throw Fail(sourceName, lineNumber, "airline codes '" + existingCode + "' and '" + row.AirlineCode + "' collide under FNV-1a-32");
                }

                airlineHashes[airlineHash] = row.AirlineCode;

                // Q-039: the bound (MAX_FIXTURE_ROWS, 11 §11.2) is on the file's total data
                // row count, not per day — RowOrdinal indexes the whole file (§11.3), so
                // that is what keeps it under FLIGHT_ID_DAY_STRIDE. Checked immediately, at
                // the row that first breaches it, so the failure names a line (§11.4) and
                // does not depend on any aggregation afterwards (02-determinism.md rule 5).
                if (rows.Count > MaxFixtureRows)
                {
                    throw Fail(sourceName, lineNumber, "the file has more than " + MaxFixtureRows.ToString(CultureInfo.InvariantCulture) + " data rows (MAX_FIXTURE_ROWS)");
                }
            }

            ValidateRotations(sourceName, rows, byRef);

            rows.Sort((a, b) => string.CompareOrdinal(a.FlightRef, b.FlightRef));
            var ordinalOf = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                ordinalOf[rows[i].FlightRef] = i;
            }

            var templates = new FlightTemplate[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                RowData row = rows[i];
                int rotationOrdinal = row.HasRotation ? ordinalOf[row.RotationRef] : -1;
                templates[i] = new FlightTemplate(
                    row.FlightRef,
                    i,
                    row.FirstDay,
                    row.RepeatDaily,
                    row.Kind,
                    new AirlineId(Fnv1a32(row.AirlineCode)),
                    new ContentId(row.AircraftType),
                    row.MinuteOfDay,
                    rotationOrdinal,
                    row.HasRotation,
                    Fx.FromInt(row.MinTurnaroundMinutes),
                    new ContentId(row.PaxProfile),
                    row.Pax,
                    row.HoldBagPermille,
                    row.AssistPermille,
                    new NodeId(row.EntryNode));
            }

            return new ScheduleTable(templates, fixtureHash);
        }

        private static void ValidateRotations(string sourceName, List<RowData> rows, Dictionary<string, RowData> byRef)
        {
            foreach (RowData row in rows)
            {
                if (!row.HasRotation)
                {
                    continue;
                }

                if (string.Equals(row.RotationRef, row.FlightRef, StringComparison.Ordinal))
                {
                    throw Fail(sourceName, row.LineNumber, "rotation_ref cannot name its own row");
                }

                if (!byRef.TryGetValue(row.RotationRef, out RowData? other))
                {
                    throw Fail(sourceName, row.LineNumber, "rotation_ref '" + row.RotationRef + "' does not match any flight_ref");
                }

                if (other.Kind == row.Kind)
                {
                    throw Fail(sourceName, row.LineNumber, "a rotation must link an Arrival to a Departure, not two rows of the same movement");
                }

                if (!string.Equals(other.RotationRef, row.FlightRef, StringComparison.Ordinal) || !other.HasRotation)
                {
                    throw Fail(sourceName, row.LineNumber, "rotation_ref must be mutual");
                }

                if (other.FirstDay != row.FirstDay)
                {
                    throw Fail(sourceName, row.LineNumber, "a rotation pair must share the same day");
                }

                if (other.RepeatDaily != row.RepeatDaily)
                {
                    throw Fail(sourceName, row.LineNumber, "a rotation pair must agree on repeat_daily");
                }

                RowData arr = row.Kind == MovementKind.Arrival ? row : other;
                RowData dep = row.Kind == MovementKind.Arrival ? other : row;
                if (!(arr.MinuteOfDay < dep.MinuteOfDay))
                {
                    throw Fail(sourceName, row.LineNumber, "a rotation pair must satisfy STA < STD on the same day");
                }
            }
        }

        private static RowData ParseRow(string sourceName, int lineNumber, string text)
        {
            string[] fields = text.Split(',');
            if (fields.Length != 14)
            {
                throw Fail(sourceName, lineNumber, "expected 14 comma-separated fields, found " + fields.Length.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < fields.Length; i++)
            {
                string f = fields[i];
                if (f.Length > 0 && (char.IsWhiteSpace(f[0]) || char.IsWhiteSpace(f[f.Length - 1])))
                {
                    throw Fail(sourceName, lineNumber, "field " + i.ToString(CultureInfo.InvariantCulture) + " has leading or trailing whitespace");
                }

                if (f.IndexOf('"') >= 0)
                {
                    throw Fail(sourceName, lineNumber, "quoting is not supported: field " + i.ToString(CultureInfo.InvariantCulture) + " contains a quote character");
                }
            }

            string flightRef = fields[0];

            if (!TryParseUInt32(fields[1], out uint day))
            {
                throw Fail(sourceName, lineNumber, "malformed day: '" + fields[1] + "'");
            }

            bool repeatDaily;
            if (fields[2] == "1")
            {
                repeatDaily = true;
            }
            else if (fields[2] == "0")
            {
                repeatDaily = false;
            }
            else
            {
                throw Fail(sourceName, lineNumber, "repeat_daily must be 0 or 1: '" + fields[2] + "'");
            }

            MovementKind kind;
            if (fields[3] == "A")
            {
                kind = MovementKind.Arrival;
            }
            else if (fields[3] == "D")
            {
                kind = MovementKind.Departure;
            }
            else
            {
                throw Fail(sourceName, lineNumber, "movement must be A or D: '" + fields[3] + "'");
            }

            string airline = fields[4];
            string aircraftType = fields[5];

            if (!TryParseHhMm(fields[6], out uint minuteOfDay))
            {
                throw Fail(sourceName, lineNumber, "malformed sched_hhmm: '" + fields[6] + "'");
            }

            string rotationRef = fields[7];
            bool hasRotation = rotationRef.Length > 0;

            if (!TryParseUInt32(fields[8], out uint minTurnaround))
            {
                throw Fail(sourceName, lineNumber, "malformed min_turnaround_minutes: '" + fields[8] + "'");
            }

            if (minTurnaround > int.MaxValue)
            {
                throw Fail(sourceName, lineNumber, "min_turnaround_minutes out of range: '" + fields[8] + "'");
            }

            if (!TryParseInt32(fields[9], out int pax))
            {
                throw Fail(sourceName, lineNumber, "malformed pax: '" + fields[9] + "'");
            }

            string paxProfile = fields[10];

            if (!TryParseInt32(fields[11], out int holdBagPermille) || holdBagPermille > 1000)
            {
                throw Fail(sourceName, lineNumber, "hold_bag_permille out of range: '" + fields[11] + "'");
            }

            if (!TryParseInt32(fields[12], out int assistPermille) || assistPermille > 1000)
            {
                throw Fail(sourceName, lineNumber, "assist_permille out of range: '" + fields[12] + "'");
            }

            string entryField = fields[13];
            uint entryNode = 0;
            if (entryField.Length > 0)
            {
                if (!TryParseUInt32(entryField, out entryNode))
                {
                    throw Fail(sourceName, lineNumber, "malformed entry_node: '" + entryField + "'");
                }
            }

            if (kind == MovementKind.Arrival)
            {
                if (pax != 0)
                {
                    throw Fail(sourceName, lineNumber, "an Arrival must carry 0 pax");
                }

                if (entryField.Length > 0)
                {
                    throw Fail(sourceName, lineNumber, "an Arrival must not name an entry_node");
                }
            }
            else
            {
                if (pax > 0 && entryField.Length == 0)
                {
                    throw Fail(sourceName, lineNumber, "a Departure with pax > 0 must name an entry_node");
                }
            }

            return new RowData(
                lineNumber,
                flightRef,
                day,
                repeatDaily,
                kind,
                airline,
                aircraftType,
                minuteOfDay,
                rotationRef,
                hasRotation,
                (int)minTurnaround,
                pax,
                paxProfile,
                holdBagPermille,
                assistPermille,
                entryNode);
        }

        private static List<byte[]> SplitLines(byte[] bytes)
        {
            var result = new List<byte[]>();
            int start = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n')
                {
                    var line = new byte[i - start];
                    Array.Copy(bytes, start, line, 0, line.Length);
                    result.Add(line);
                    start = i + 1;
                }
            }

            var tail = new byte[bytes.Length - start];
            Array.Copy(bytes, start, tail, 0, tail.Length);
            result.Add(tail);
            return result;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseUInt32(string s, out uint value)
        {
            value = 0;
            if (s.Length == 0)
            {
                return false;
            }

            ulong acc = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                acc = (acc * 10) + (uint)(c - '0');
                if (acc > uint.MaxValue)
                {
                    return false;
                }
            }

            value = (uint)acc;
            return true;
        }

        private static bool TryParseInt32(string s, out int value)
        {
            value = 0;
            if (s.Length == 0)
            {
                return false;
            }

            long acc = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }

                acc = (acc * 10) + (c - '0');
                if (acc > int.MaxValue)
                {
                    return false;
                }
            }

            value = (int)acc;
            return true;
        }

        private static bool TryParseHhMm(string s, out uint minuteOfDay)
        {
            minuteOfDay = 0;
            if (s.Length != 5 || s[2] != ':')
            {
                return false;
            }

            if (!IsDigit(s[0]) || !IsDigit(s[1]) || !IsDigit(s[3]) || !IsDigit(s[4]))
            {
                return false;
            }

            int hh = ((s[0] - '0') * 10) + (s[1] - '0');
            int mm = ((s[3] - '0') * 10) + (s[4] - '0');
            if (hh > 23 || mm > 59)
            {
                return false;
            }

            minuteOfDay = (uint)((hh * 60) + mm);
            return true;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static uint Fnv1a32(string s)
        {
            uint h = 0x811C9DC5U;
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            for (int i = 0; i < bytes.Length; i++)
            {
                h = unchecked((h ^ bytes[i]) * 0x01000193U);
            }

            return h;
        }

        private static ulong Fnv1a64(byte[] data)
        {
            ulong h = 0xCBF29CE484222325UL;
            for (int i = 0; i < data.Length; i++)
            {
                h = unchecked((h ^ data[i]) * 0x100000001B3UL);
            }

            return h;
        }

        private static FormatException Fail(string sourceName, int line, string message)
        {
            return new FormatException(sourceName + ": line " + line.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }

        private sealed class RowData
        {
            public RowData(
                int lineNumber,
                string flightRef,
                uint firstDay,
                bool repeatDaily,
                MovementKind kind,
                string airlineCode,
                string aircraftType,
                uint minuteOfDay,
                string rotationRef,
                bool hasRotation,
                int minTurnaroundMinutes,
                int pax,
                string paxProfile,
                int holdBagPermille,
                int assistPermille,
                uint entryNode)
            {
                LineNumber = lineNumber;
                FlightRef = flightRef;
                FirstDay = firstDay;
                RepeatDaily = repeatDaily;
                Kind = kind;
                AirlineCode = airlineCode;
                AircraftType = aircraftType;
                MinuteOfDay = minuteOfDay;
                RotationRef = rotationRef;
                HasRotation = hasRotation;
                MinTurnaroundMinutes = minTurnaroundMinutes;
                Pax = pax;
                PaxProfile = paxProfile;
                HoldBagPermille = holdBagPermille;
                AssistPermille = assistPermille;
                EntryNode = entryNode;
            }

            public int LineNumber { get; }

            public string FlightRef { get; }

            public uint FirstDay { get; }

            public bool RepeatDaily { get; }

            public MovementKind Kind { get; }

            public string AirlineCode { get; }

            public string AircraftType { get; }

            public uint MinuteOfDay { get; }

            public string RotationRef { get; }

            public bool HasRotation { get; }

            public int MinTurnaroundMinutes { get; }

            public int Pax { get; }

            public string PaxProfile { get; }

            public int HoldBagPermille { get; }

            public int AssistPermille { get; }

            public uint EntryNode { get; }
        }
    }
}
