using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// The load-time validation of 13-interfaces-turnaround.md §13.4, shared by
    /// the setup loader and the system's construction. Each failure names the
    /// source and the offending JobKind or VehicleId.
    /// </summary>
    internal static class TurnaroundSetupValidator
    {
        public static void Validate(in TurnaroundSetup setup, string sourceName)
        {
            IReadOnlyList<JobDef>? jobs = setup.Catalogue.Jobs;
            IReadOnlyList<VehicleDef>? vehicles = setup.Fleet.Vehicles;
            if (jobs == null)
            {
                throw Fail(sourceName, "the catalogue has no job list");
            }

            if (vehicles == null)
            {
                throw Fail(sourceName, "the fleet has no vehicle list");
            }

            JobKind[] kinds = (JobKind[])Enum.GetValues(typeof(JobKind));
            var seen = new bool[kinds.Length];
            for (int i = 0; i < jobs.Count; i++)
            {
                JobDef def = jobs[i];
                int ordinal = (int)def.Kind;
                if (ordinal < 0 || ordinal >= kinds.Length)
                {
                    throw Fail(sourceName, "the catalogue names an unknown JobKind (" + ordinal.ToString(CultureInfo.InvariantCulture) + ")");
                }

                if (seen[ordinal])
                {
                    throw Fail(sourceName, "the catalogue has more than one entry for JobKind " + def.Kind);
                }

                seen[ordinal] = true;
                if (def.NominalDurationTicks < 1U)
                {
                    throw Fail(sourceName, "JobKind " + def.Kind + " has a zero NominalDurationTicks");
                }

                if (def.Kind == JobKind.PushbackPrep && def.Category == DelayCategory.Pushback)
                {
                    throw Fail(sourceName, "JobKind PushbackPrep must map to ground_handling, not pushback");
                }
            }

            for (int i = 0; i < seen.Length; i++)
            {
                if (!seen[i])
                {
                    throw Fail(sourceName, "the catalogue has no entry for JobKind " + kinds[i]);
                }
            }

            for (int i = 0; i < vehicles.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (vehicles[j].Id == vehicles[i].Id)
                    {
                        throw Fail(sourceName, "VehicleId " + vehicles[i].Id.Value.ToString(CultureInfo.InvariantCulture) + " appears more than once in the fleet");
                    }
                }
            }
        }

        private static FormatException Fail(string sourceName, string message)
        {
            return new FormatException(sourceName + ": " + message);
        }
    }
}
