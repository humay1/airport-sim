using System;
using System.Text;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// The file spellings of 13-interfaces-turnaround.md §13.10a "File format"
    /// (snake_case enum names) and the fixed job-to-vehicle table of §13.4
    /// (Q-088). Used at load time only.
    /// </summary>
    internal static class TurnaroundNames
    {
        /// <summary>The vehicle kind a job kind needs, per §13.4's table.</summary>
        public static VehicleKind? RequiredVehicle(JobKind kind)
        {
            switch (kind)
            {
                case JobKind.BaggageUnload:
                case JobKind.BaggageLoad:
                    return VehicleKind.BaggageTractor;
                case JobKind.CabinClean:
                    return VehicleKind.CleaningCrew;
                case JobKind.Catering:
                    return VehicleKind.CateringTruck;
                case JobKind.Fuel:
                    return VehicleKind.FuelTruck;
                case JobKind.PushbackPrep:
                    return VehicleKind.PushbackTug;
                default:
                    return null;
            }
        }

        public static string Spelling(JobKind kind)
        {
            return Snake(kind.ToString());
        }

        public static bool TryParse<T>(string spelling, out T value)
            where T : struct, Enum
        {
            foreach (T candidate in (T[])Enum.GetValues(typeof(T)))
            {
                if (string.Equals(Snake(candidate.ToString()), spelling, StringComparison.Ordinal))
                {
                    value = candidate;
                    return true;
                }
            }

            value = default;
            return false;
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
    }
}
