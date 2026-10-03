using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// Structural rules on the compiled sim.turnaround assembly: 07 L1 (name
    /// and target), L5 (public iff a spec interface section names it), L6
    /// (root namespace), L2's reference list (Core, Schedule), 13 §13.1 (no
    /// sim.flow, sim.staff or sim.airside calls) and §13.7/§13.8 (no mutating
    /// entry point).
    /// </summary>
    public sealed class TurnaroundAssemblyTests
    {
        private static readonly Assembly Sim = typeof(TurnaroundFactory).Assembly;

        // 13 §13.3, §13.4, §13.7 and §13.10a name these, and nothing else, for
        // sim.turnaround. VehicleId, JobId and the four enums are sim.core's (T-026).
        private static readonly string[] SpecTypes =
        {
            "ITurnaroundSetupLoader",
            "ITurnaroundSystem",
            "JobDef",
            "TurnaroundCatalogue",
            "TurnaroundFactory",
            "TurnaroundFleet",
            "TurnaroundJob",
            "TurnaroundSetup",
            "VehicleDef",
            "VehicleState",
        };

        [Fact]
        public void test_turnaround_assembly_targets_netstandard21_with_spec_name()
        {
            Assert.Equal("AirportSim.Sim.Turnaround", Sim.GetName().Name);
            TargetFrameworkAttribute? tfm = Sim.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on sim.turnaround");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);
        }

        [Fact]
        public void test_turnaround_assembly_exports_exactly_the_spec_types_in_root_namespace()
        {
            Type[] exported = Sim.GetExportedTypes();
            var outside = exported.Where(t => t.Namespace != "AirportSim.Sim.Turnaround").Select(t => t.FullName).ToList();
            Assert.True(outside.Count == 0, "public types outside the RootNamespace (07 L6): " + string.Join(", ", outside));

            string[] names = exported.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(SpecTypes, names);
        }

        [Fact]
        public void test_turnaround_assembly_references_no_airside_flow_staff_or_presentation()
        {
            string[] banned =
            {
                "AirportSim.Sim.Airside", "AirportSim.Sim.Flow", "AirportSim.Sim.World", "AirportSim.Sim.Staff",
                "AirportSim.Sim.Delay", "AirportSim.App", "UnityEngine", "Unity.", "UnityEditor", "Godot",
            };
            var names = Sim.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "sim.turnaround references (07 L2: Core, Schedule; 13 §13.1): " + string.Join(", ", offenders));
            Assert.Contains("AirportSim.Sim.Core", names);
            Assert.Contains("AirportSim.Sim.Schedule", names);
        }

        [Fact]
        public void test_turnaround_assembly_system_interface_has_only_the_four_queries()
        {
            Type t = typeof(ITurnaroundSystem);
            Assert.Contains(typeof(AirportSim.Sim.Core.ISimSystem), t.GetInterfaces());
            string[] declared = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "FreeVehicles", "JobsForFlight", "TryGetJob", "TryGetVehicle" }, declared);
            Assert.Empty(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
            Assert.Empty(t.GetEvents());

            string[] factory = typeof(TurnaroundFactory).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "CreateSetupLoader", "CreateSystem" }, factory);
            Assert.True(typeof(TurnaroundFactory).IsAbstract && typeof(TurnaroundFactory).IsSealed, "TurnaroundFactory is not a static class (07 L10)");
        }
    }
}
