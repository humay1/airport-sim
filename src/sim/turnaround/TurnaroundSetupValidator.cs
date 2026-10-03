using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Turnaround
{
    /// <summary>
    /// The five checks of 13-interfaces-turnaround.md §13.4 "Validation", in
    /// that order; the first failure is reported (Q-086). The setup loader
    /// and CreateSystem share it.
    /// </summary>
    internal static class TurnaroundSetupValidator
    {
        /// <summary>Returns the first failure's message, or null when the setup is valid.</summary>
        public static string? Check(in TurnaroundSetup setup)
        {
            IReadOnlyList<JobDef>? jobs = setup.Catalogue.Jobs;
            IReadOnlyList<VehicleDef>? vehicles = setup.Fleet.Vehicles;
            if (jobs == null)
            {
                return "the catalogue has no job list";
            }

            if (vehicles == null)
            {
                return "the fleet has no vehicle list";
            }

            JobKind[] kinds = (JobKind[])Enum.GetValues(typeof(JobKind));
            var count = new int[kinds.Length];
            var def = new JobDef[kinds.Length];
            for (int i = 0; i < jobs.Count; i++)
            {
                int ordinal = (int)jobs[i].Kind;
                if (ordinal < 0 || ordinal >= kinds.Length)
                {
                    return "the catalogue names an unknown job kind (" + ordinal.ToString(CultureInfo.InvariantCulture) + ")";
                }

                count[ordinal]++;
                def[ordinal] = jobs[i];
            }

            // 1. Duplicates first, then omissions, each the lowest ordinal.
            for (int k = 0; k < kinds.Length; k++)
            {
                if (count[k] > 1)
                {
                    return "job kind " + TurnaroundNames.Spelling(kinds[k]) + " appears more than once in the catalogue";
                }
            }

            for (int k = 0; k < kinds.Length; k++)
            {
                if (count[k] == 0)
                {
                    return "the catalogue has no entry for job kind " + TurnaroundNames.Spelling(kinds[k]);
                }
            }

            // 2. RequiresVehicle matches the fixed table.
            for (int k = 0; k < kinds.Length; k++)
            {
                if (def[k].RequiresVehicle != TurnaroundNames.RequiredVehicle(kinds[k]))
                {
                    return "job kind " + TurnaroundNames.Spelling(kinds[k]) + " has a RequiresVehicle that differs from the fixed table";
                }
            }

            // 3. Durations.
            for (int k = 0; k < kinds.Length; k++)
            {
                if (def[k].NominalDurationTicks < 1U)
                {
                    return "job kind " + TurnaroundNames.Spelling(kinds[k]) + " has a nominal duration below 1 tick";
                }
            }

            // 4. Categories; PushbackPrep is always ground_handling.
            for (int k = 0; k < kinds.Length; k++)
            {
                DelayCategory c = def[k].Category;
                bool allowed = c == DelayCategory.GroundHandling || c == DelayCategory.Fuel || c == DelayCategory.Catering
                    || c == DelayCategory.Cleaning || c == DelayCategory.Loading;
                if (!allowed)
                {
                    return "job kind " + TurnaroundNames.Spelling(kinds[k]) + " has a category that is not allowed for a job";
                }

                if (kinds[k] == JobKind.PushbackPrep && c != DelayCategory.GroundHandling)
                {
                    return "job kind pushback_prep must have category ground_handling";
                }
            }

            // 5. Vehicle ids: 0 first, then the lowest duplicate.
            for (int i = 0; i < vehicles.Count; i++)
            {
                if (vehicles[i].Id.Value == 0)
                {
                    return "vehicle id 0 is not allowed";
                }
            }

            bool found = false;
            ushort lowest = 0;
            for (int i = 0; i < vehicles.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    if (vehicles[j].Id == vehicles[i].Id && (!found || vehicles[i].Id.Value < lowest))
                    {
                        found = true;
                        lowest = vehicles[i].Id.Value;
                    }
                }
            }

            if (found)
            {
                return "vehicle id " + lowest.ToString(CultureInfo.InvariantCulture) + " appears more than once in the fleet";
            }

            return null;
        }
    }
}
