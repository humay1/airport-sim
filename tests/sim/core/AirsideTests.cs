using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Holds the T-026 task's mandated name
    /// test_airside_turnaround_delay_payload_types_compile_in_sim_core_only
    /// (07 L6, Q-028: the class of the longest subject prefix). It asserts
    /// every relocated type's compiled home is sim.core (Q-018).
    /// </summary>
    public sealed class AirsideTests
    {
        [Fact]
        public void test_airside_turnaround_delay_payload_types_compile_in_sim_core_only()
        {
            Assembly core = typeof(SimConstants).Assembly;
            var types = new List<Type>();
            types.AddRange(PayloadShapes.AllStructShapes().Select(s => s.Type));
            types.AddRange(PayloadShapes.Ids.Select(i => i.Id));
            types.AddRange(PayloadShapes.Enums.Select(e => e.Enum));
            types.Add(typeof(IContentDefinition));
            types.Add(typeof(ContentIndexFactory));

            var misplaced = types
                .Where(t => t.Assembly != core || t.Namespace != "AirportSim.Sim.Core")
                .Select(t => $"{t.FullName} in {t.Assembly.GetName().Name}")
                .ToList();
            Assert.True(misplaced.Count == 0, "not compiled in sim.core's RootNamespace (Q-018, 07 L6):\n" + string.Join("\n", misplaced));

            string[] modules = { "AirportSim.Sim.Airside", "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay", "AirportSim.Sim.Flow", "AirportSim.Sim.World", "AirportSim.Sim.Schedule" };
            var edges = core.GetReferencedAssemblies().Select(a => a.Name).Where(n => modules.Contains(n)).ToList();
            Assert.True(edges.Count == 0, "sim.core references a module: " + string.Join(", ", edges));
        }
    }
}
