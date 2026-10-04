using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Xunit;

namespace AirportSim.App.Ui.Tests
{
    /// <summary>
    /// Static rules on the compiled UI scene layer: 17 §17.2 (no engine
    /// reference, netstandard2.1, never app.render's backend or app.host, no
    /// static mutable state) and 07 L1/L2.
    /// </summary>
    public sealed class UiSceneAssemblyTests
    {
        private static readonly Assembly Scene = typeof(UiFactory).Assembly;

        [Fact]
        public void test_ui_scene_assembly_has_no_engine_reference()
        {
            Assert.Equal("AirportSim.App.Ui", Scene.GetName().Name);
            TargetFrameworkAttribute? tfm = Scene.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on the UI scene layer");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);

            string[] banned =
            {
                "UnityEngine", "UnityEditor", "Unity.", "Godot", "Microsoft.Xna", "MonoGame", "SharpDX", "OpenTK", "Silk.NET", "SkiaSharp", "System.Drawing", "System.Windows",
                "AirportSim.App.Host",
                "AirportSim.Sim.Schedule", "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay",
            };
            var names = Scene.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "the UI scene layer references (17 §17.2: no engine, never app.host; §17.6: no other sim module): " + string.Join(", ", offenders));

            // 17 §17.2/§17.7: it compiles against app.render's scene layer, sim.core and sim.flow.
            Assert.Contains("AirportSim.App.Render", names);
            Assert.Contains("AirportSim.Sim.Core", names);
            Assert.Contains("AirportSim.Sim.Flow", names);
        }

        [Fact]
        public void test_ui_scene_assembly_has_no_static_mutable_state()
        {
            // 17 §17.2 (as 15 §15.3): no static mutable state. Constants and
            // static readonly fields are allowed (07 L10). Compiler-generated
            // types (lambda caches, fixed data) are not the author's state.
            var offenders = new List<string>();
            foreach (Type t in Scene.GetTypes())
            {
                if (t.IsDefined(typeof(CompilerGeneratedAttribute), false) || t.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!f.IsLiteral && !f.IsInitOnly && !f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    {
                        offenders.Add(t.FullName + "." + f.Name);
                    }
                }
            }

            Assert.True(offenders.Count == 0, "static mutable fields in the UI scene layer (17 §17.2): " + string.Join(", ", offenders));
            Assert.True(Scene.GetTypes().Length >= 8, "the UI scene layer declares fewer types than 17 names");
        }
    }
}
