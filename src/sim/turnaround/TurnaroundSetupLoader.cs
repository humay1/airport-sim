using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// The one <see cref="ITurnaroundSetupLoader"/>. The file format is this
    /// module's own choice (13-interfaces-turnaround.md §13.11): UTF-8 without
    /// a byte order mark, LF line endings, a final newline, no blank lines and
    /// no comments. Each line is one of
    /// <c>job,&lt;JobKind&gt;,&lt;VehicleKind or empty&gt;,&lt;ticks&gt;,&lt;category&gt;</c> or
    /// <c>vehicle,&lt;id&gt;,&lt;VehicleKind&gt;</c>. Kinds use their enum names;
    /// the category uses its snake_case name (ground_handling, fuel, ...).
    /// </summary>
    internal sealed class TurnaroundSetupLoader : ITurnaroundSetupLoader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public TurnaroundSetup Load(ReadOnlySpan<byte> file, string sourceName)
        {
            if (file.Length == 0)
            {
                throw Fail(sourceName, 1, "the file is empty");
            }

            if (file.Length >= 3 && file[0] == 0xEF && file[1] == 0xBB && file[2] == 0xBF)
            {
                throw Fail(sourceName, 1, "a UTF-8 byte order mark is not allowed");
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(file.ToArray());
            }
            catch (ArgumentException)
            {
                throw Fail(sourceName, 1, "the file is not valid UTF-8");
            }

            if (text[text.Length - 1] != '\n')
            {
                throw Fail(sourceName, CountLines(text) + 1, "the file must end with a final newline");
            }

            string[] lines = text.Split('\n');
            var jobs = new List<JobDef>();
            var vehicles = new List<VehicleDef>();
            for (int i = 0; i < lines.Length - 1; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i];
                if (line.Length == 0)
                {
                    throw Fail(sourceName, lineNumber, "blank lines are not allowed");
                }

                if (line.IndexOf('\r') >= 0)
                {
                    throw Fail(sourceName, lineNumber, "lines end in LF only");
                }

                string[] f = line.Split(',');
                if (f[0] == "job")
                {
                    jobs.Add(ParseJob(f, sourceName, lineNumber));
                }
                else if (f[0] == "vehicle")
                {
                    vehicles.Add(ParseVehicle(f, sourceName, lineNumber));
                }
                else
                {
                    throw Fail(sourceName, lineNumber, "a line must start with 'job' or 'vehicle'");
                }
            }

            var setup = new TurnaroundSetup(new TurnaroundCatalogue(jobs), new TurnaroundFleet(vehicles));
            TurnaroundSetupValidator.Validate(setup, sourceName);
            return setup;
        }

        private static JobDef ParseJob(string[] f, string sourceName, int line)
        {
            if (f.Length != 5)
            {
                throw Fail(sourceName, line, "a job line has 5 fields");
            }

            JobKind kind = ParseEnum<JobKind>(f[1], sourceName, line, "JobKind");
            VehicleKind? requires = null;
            if (f[2].Length > 0)
            {
                requires = ParseEnum<VehicleKind>(f[2], sourceName, line, "VehicleKind");
            }

            if (!uint.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint ticks))
            {
                throw Fail(sourceName, line, "the duration must be a non-negative integer");
            }

            DelayCategory category = ParseCategory(f[4], sourceName, line);
            return new JobDef(kind, requires, ticks, category);
        }

        private static VehicleDef ParseVehicle(string[] f, string sourceName, int line)
        {
            if (f.Length != 3)
            {
                throw Fail(sourceName, line, "a vehicle line has 3 fields");
            }

            if (!ushort.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort id))
            {
                throw Fail(sourceName, line, "the vehicle id must be an integer in 0..65535");
            }

            return new VehicleDef(new VehicleId(id), ParseEnum<VehicleKind>(f[2], sourceName, line, "VehicleKind"));
        }

        private static T ParseEnum<T>(string name, string sourceName, int line, string what)
            where T : struct, Enum
        {
            foreach (T value in (T[])Enum.GetValues(typeof(T)))
            {
                if (string.Equals(value.ToString(), name, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            throw Fail(sourceName, line, "'" + name + "' is not a " + what);
        }

        private static DelayCategory ParseCategory(string name, string sourceName, int line)
        {
            foreach (DelayCategory value in (DelayCategory[])Enum.GetValues(typeof(DelayCategory)))
            {
                if (string.Equals(Snake(value.ToString()), name, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            throw Fail(sourceName, line, "'" + name + "' is not a delay category");
        }

        private static string Snake(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (c >= 'A' && c <= 'Z')
                {
                    if (i > 0)
                    {
                        sb.Append('_');
                    }

                    sb.Append((char)(c + ('a' - 'A')));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static int CountLines(string text)
        {
            int n = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    n++;
                }
            }

            return n;
        }

        private static FormatException Fail(string sourceName, int line, string message)
        {
            return new FormatException(sourceName + ": line " + line.ToString(CultureInfo.InvariantCulture) + ": " + message);
        }
    }
}
