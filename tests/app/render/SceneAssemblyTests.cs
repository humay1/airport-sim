using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// Static rules on the compiled scene layer: 15 §15.1/§15.3 (no engine
    /// reference, netstandard2.1, never app.ui, nothing from sim.turnaround
    /// or sim.delay at Phase 1, never the 2D art) and 07 L1/L2. Since Q-130
    /// the scene layer reads sim.schedule's TryGetFlight (15 §15.1, §15.6,
    /// and §15.18 task 1's project reference), so sim.schedule is no longer
    /// banned.
    /// </summary>
    public sealed class SceneAssemblyTests
    {
        private static readonly Assembly Scene = typeof(RenderFactory).Assembly;

        [Fact]
        public void test_scene_assembly_has_no_engine_reference()
        {
            Assert.Equal("AirportSim.App.Render", Scene.GetName().Name);
            TargetFrameworkAttribute? tfm = Scene.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on the scene layer");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);

            string[] banned =
            {
                "UnityEngine", "UnityEditor", "Unity.", "Godot", "Microsoft.Xna", "MonoGame", "SharpDX", "OpenTK", "Silk.NET", "SkiaSharp", "System.Drawing", "System.Windows",
                "AirportSim.App.Ui", "AirportSim.App.Host",
                "AirportSim.App.Render.Art2D",
                "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay",
            };
            var names = Scene.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "the scene layer references (15 §15.3: no engine, never app.ui, never the 2D art; §15.1: no turnaround or delay): " + string.Join(", ", offenders));
            Assert.Contains("AirportSim.Sim.Core", names);
        }
    }
}
