using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// Structural rules on the compiled sim.airside assembly: 07 L1 (name and
    /// target), L5 (public iff a spec interface section names it), L6 (root
    /// namespace), L2's reference list, and 12 §12.1 (no calls into
    /// sim.world at Phase 0/1) with T-021's "without sim.turnaround".
    /// </summary>
    public sealed class AirsideAssemblyTests
    {
        private static readonly Assembly Sim = typeof(AirsideFactory).Assembly;

        // 12 §12.4, §12.9 and §12.12a name these, and nothing else, for sim.airside.
        // The four id structs are sim.core's (T-026), not this assembly's.
        private static readonly string[] SpecTypes =
        {
            "AircraftLegPhase",
            "AircraftTrack",
            "AirsideFactory",
            "AirsideLayout",
            "AirsideRules",
            "IAirsideLayoutLoader",
            "IAirsideSystem",
            "RunwayDef",
            "StandDef",
            "StandState",
            "TaxiEdgeDef",
            "TaxiNodeDef",
            "TaxiNodeKind",
        };

        [Fact]
        public void test_airside_assembly_targets_netstandard21_with_spec_name()
        {
            Assert.Equal("AirportSim.Sim.Airside", Sim.GetName().Name);
            TargetFrameworkAttribute? tfm = Sim.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on sim.airside");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);
        }

        [Fact]
        public void test_airside_assembly_exports_exactly_the_spec_types_in_root_namespace()
        {
            Type[] exported = Sim.GetExportedTypes();
            var outside = exported.Where(t => t.Namespace != "AirportSim.Sim.Airside").Select(t => t.FullName).ToList();
            Assert.True(outside.Count == 0, "public types outside the RootNamespace (07 L6): " + string.Join(", ", outside));

            string[] names = exported.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(SpecTypes, names);
        }

        [Fact]
        public void test_airside_assembly_references_no_world_turnaround_delay_or_presentation()
        {
            string[] banned =
            {
                "AirportSim.Sim.World", "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay", "AirportSim.App",
                "UnityEngine", "Unity.", "UnityEditor", "Godot",
            };
            var names = Sim.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "sim.airside references (07 L2: Core, Schedule, Flow; 12 §12.1: no sim.world): " + string.Join(", ", offenders));
            Assert.Contains("AirportSim.Sim.Core", names);
        }
    }
}
