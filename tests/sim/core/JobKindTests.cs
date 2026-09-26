using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>JobKind, 13 §13.3, relocated to sim.core by T-026.</summary>
    public sealed class JobKindTests
    {
        [Fact]
        public void test_job_kind_enum_matches_spec_eight_values()
        {
            Assert.Empty(PayloadShapes.EnumViolations(
                typeof(JobKind),
                new[] { "Deboard", "BaggageUnload", "CabinClean", "Catering", "Fuel", "BaggageLoad", "PushbackPrep", "Boarding" }));
            Assert.Equal(8, Enum.GetValues(typeof(JobKind)).Length);
            Assert.Equal(7, (int)JobKind.Boarding);
        }
    }
}
