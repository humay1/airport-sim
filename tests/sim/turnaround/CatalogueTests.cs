using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 13 §13.4 "Validation" through ITurnaroundSetupLoader.Load on bytes in
    /// 13 §13.10a's format (Q-086): a FormatException whose message starts
    /// with sourceName and ": " and contains the named JobKind in its file
    /// spelling. Tests assert the type, the prefix and the token only.
    /// </summary>
    public sealed class CatalogueTests
    {
        private const string Source = "catalogue-under-test.json";

        private static void AssertRejects(byte[] file, string token)
        {
            ITurnaroundSetupLoader loader = TurnaroundFactory.CreateSetupLoader();
            FormatException e = Assert.Throws<FormatException>(() => loader.Load(file, Source));
            Assert.StartsWith(Source + ": ", e.Message, StringComparison.Ordinal);
            Assert.Contains(token, e.Message, StringComparison.Ordinal);
        }

        private static List<JobDef> Without(List<JobDef> jobs, params JobKind[] kinds)
        {
            return jobs.FindAll(j => Array.IndexOf(kinds, j.Kind) < 0);
        }

        private static List<JobDef> WithDuration(List<JobDef> jobs, JobKind kind, uint duration)
        {
            return jobs.ConvertAll(j => j.Kind == kind ? new JobDef(j.Kind, j.RequiresVehicle, duration, j.Category) : j);
        }

        [Fact]
        public void test_catalogue_rejects_missing_job_kind()
        {
            // The fixture loads (so the rejections below are about the omission).
            Assert.Equal(8, TurnaroundFactory.CreateSetupLoader().Load(SetupFile.FixtureWith(j => j), Source).Catalogue.Jobs.Count);

            // Check 1: an omission names the missing JobKind with the lowest ordinal.
            AssertRejects(SetupFile.FixtureWith(j => Without(j, JobKind.Boarding)), "boarding");
            AssertRejects(SetupFile.FixtureWith(j => Without(j, JobKind.Deboard)), "deboard");
            AssertRejects(SetupFile.FixtureWith(j => Without(j, JobKind.PushbackPrep, JobKind.BaggageUnload)), "baggage_unload");
            AssertRejects(SetupFile.FixtureWith(j => new List<JobDef>()), "deboard");
        }

        [Fact]
        public void test_catalogue_rejects_duplicate_job_kind()
        {
            // Check 1: duplicates first, naming the duplicated JobKind with the lowest ordinal.
            AssertRejects(
                SetupFile.FixtureWith(j =>
                {
                    j.Add(new JobDef(JobKind.Fuel, VehicleKind.FuelTruck, 120U, DelayCategory.Fuel));
                    j.Add(new JobDef(JobKind.Catering, VehicleKind.CateringTruck, 90U, DelayCategory.Catering));
                    return j;
                }),
                "catering");
        }

        [Fact]
        public void test_catalogue_rejects_zero_nominal_duration()
        {
            // Check 3: a duration of 0 parses (it is inside uint32) and fails
            // validation, naming the JobKind with the lowest ordinal.
            AssertRejects(SetupFile.FixtureWith(j => WithDuration(j, JobKind.Fuel, 0U)), "fuel");
            AssertRejects(SetupFile.FixtureWith(j => WithDuration(WithDuration(j, JobKind.Boarding, 0U), JobKind.CabinClean, 0U)), "cabin_clean");

            // A duration of 1 is valid.
            TurnaroundSetup ok = TurnaroundFactory.CreateSetupLoader().Load(SetupFile.FixtureWith(j => WithDuration(j, JobKind.Fuel, 1U)), Source);
            Assert.Contains(ok.Catalogue.Jobs, d => d.Kind == JobKind.Fuel && d.NominalDurationTicks == 1U);
        }

        [Fact]
        public void test_catalogue_rejects_pushback_category_for_pushback_prep()
        {
            // Check 4: PushbackPrep is always ground_handling; pushback is reserved for sim.airside.
            AssertRejects(
                SetupFile.FixtureWith(j => j.ConvertAll(d => d.Kind == JobKind.PushbackPrep ? new JobDef(d.Kind, d.RequiresVehicle, d.NominalDurationTicks, DelayCategory.Pushback) : d)),
                "pushback_prep");
        }
    }
}
