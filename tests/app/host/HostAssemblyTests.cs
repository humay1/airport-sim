using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using AirportSim.App.Render;
using AirportSim.App.Ui;
using AirportSim.Sim.Airside;
using AirportSim.Sim.Core;
using AirportSim.Sim.Delay;
using AirportSim.Sim.Flow;
using AirportSim.Sim.Schedule;
using AirportSim.Sim.Turnaround;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. Static rules on the compiled headless host: 16 §16.2 (no engine
    /// reference, netstandard2.1, no static mutable state), 16 §16.4 (no
    /// reference to tools.simharness), and 07 L1/L2/L5/L6/L10 (the public
    /// surface is exactly what 16 names).
    /// </summary>
    public sealed class HostAssemblyTests
    {
        private static readonly Assembly Host = typeof(HostFactory).Assembly;

        [Fact]
        public void test_host_assembly_has_no_engine_reference()
        {
            Assert.Equal("AirportSim.App.Host", Host.GetName().Name);
            TargetFrameworkAttribute? tfm = Host.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on the headless host");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);

            string[] banned =
            {
                "UnityEngine", "UnityEditor", "Unity.", "Godot", "Microsoft.Xna", "MonoGame", "SharpDX", "OpenTK", "Silk.NET", "SkiaSharp", "System.Drawing", "System.Windows",
                "AirportSim.Tools", "AirportSim.App.Host.Unity",
            };
            var names = Host.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "the headless host references (16 §16.2: no engine; §16.4: never tools.simharness): " + string.Join(", ", offenders));

            // 16 §16.4/§16.5: it composes every Phase 1 system and both scene layers.
            foreach (string module in new[]
            {
                "AirportSim.Sim.Core", "AirportSim.Sim.World", "AirportSim.Sim.Schedule", "AirportSim.Sim.Airside", "AirportSim.Sim.Flow",
                "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay", "AirportSim.App.Render", "AirportSim.App.Ui",
            })
            {
                Assert.Contains(module, names);
            }

            Assert.False(Host.IsDefined(typeof(InternalsVisibleToAttribute), false), "the host carries InternalsVisibleTo (07 L5)");
        }

        [Fact]
        public void test_host_assembly_has_no_static_mutable_state()
        {
            // 16 §16.2 (as 15 §15.3): no static mutable state. Constants and
            // static readonly fields are allowed (07 L10). Compiler-generated
            // types (lambda caches, fixed data) are not the author's state.
            var offenders = new List<string>();
            foreach (Type t in Host.GetTypes())
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

            Assert.True(offenders.Count == 0, "static mutable fields in the headless host (16 §16.2): " + string.Join(", ", offenders));
        }

        private static void AssertConstructor(Type type, params Type[] parameters)
        {
            ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.True(ctors.Length == 1, type.Name + " has " + ctors.Length + " public constructors (07 L10: exactly one)");
            Type[] actual = ctors[0].GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.True(
                actual.SequenceEqual(parameters),
                type.Name + "'s constructor takes (" + string.Join(", ", actual.Select(p => p.Name)) + "), expected (" + string.Join(", ", parameters.Select(p => p.Name)) + ")");
            Assert.True(type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), false), type.Name + " is not a readonly struct (07 L10)");
        }

        private static void AssertProperties(Type type, params string[] names)
        {
            var actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), actual);
            foreach (PropertyInfo p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(p.CanRead && p.SetMethod == null, type.Name + "." + p.Name + " is not get-only (07 L10)");
            }
        }

        private static List<string> Methods(Type type)
        {
            return type.GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public void test_host_assembly_public_surface_matches_spec()
        {
            // 07 L5/L6: a type is public iff the spec names it, directly in the RootNamespace.
            string[] expected =
            {
                "CheckpointRunRequest", "ComposedSim", "FrameInput", "FrameOutput", "HostFactory", "IFrameLoop", "IHeadlessRun",
                "IHostCommandLine", "IPreferenceStore", "IPresentationComposer", "IScenarioBundle", "ISimComposer", "Presentation",
            };
            var exported = Host.GetExportedTypes().Select(t => t.FullName ?? t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(expected.Select(n => "AirportSim.App.Host." + n).OrderBy(n => n, StringComparer.Ordinal).ToList(), exported);

            // 07 L10: one public constructor per struct, members in declared order (16 §16.4, §16.5, §16.6, §16.8).
            AssertConstructor(typeof(ComposedSim), typeof(ISimHost), typeof(IWorldSystem), typeof(IScheduleSystem), typeof(IAirsideSystem), typeof(IFlowSystem), typeof(ITurnaroundSystem), typeof(IDelaySystem));
            AssertProperties(typeof(ComposedSim), "Host", "World", "Schedule", "Airside", "Flow", "Turnaround", "Delay");
            AssertConstructor(typeof(Presentation), typeof(ISceneBuilder), typeof(IPromotionController), typeof(ITickPacer), typeof(IUiController), typeof(IFrameLoop));
            AssertProperties(typeof(Presentation), "Scene", "Promotion", "Pacer", "Ui", "Frame");
            AssertConstructor(typeof(FrameInput), typeof(CameraView), typeof(float), typeof(float), typeof(IReadOnlyList<UiInput>), typeof(long));
            AssertConstructor(typeof(FrameOutput), typeof(RenderFrame), typeof(UiFrame));
            AssertProperties(typeof(FrameOutput), "Render", "Ui");
            AssertConstructor(typeof(CheckpointRunRequest), typeof(uint), typeof(string));
            AssertProperties(typeof(CheckpointRunRequest), "Days", "OutputPath");

            // The interfaces carry exactly their spec members.
            Assert.Equal(new List<string> { "Has", "ReadAll" }, Methods(typeof(IScenarioBundle)));
            Assert.Equal(new List<string> { "Compose" }, Methods(typeof(ISimComposer)));
            Assert.Equal(new List<string> { "Compose" }, Methods(typeof(IPresentationComposer)));
            Assert.Equal(new List<string> { "TryRead", "Write" }, Methods(typeof(IPreferenceStore)));
            Assert.Equal(new List<string> { "RunFrame" }, Methods(typeof(IFrameLoop)));
            Assert.Equal(new List<string> { "TryParse" }, Methods(typeof(IHostCommandLine)));
            Assert.Equal(new List<string> { "Run" }, Methods(typeof(IHeadlessRun)));

            // 07 L10 / 08 §8.11a: HostFactory is a static class of the factories 16 names (§16.3, §16.4, §16.5).
            Type factory = typeof(HostFactory);
            Assert.True(factory.IsAbstract && factory.IsSealed, "HostFactory is not a static class");
            var methods = factory.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(new List<string> { "CreateCommandLine", "CreateHeadlessRun", "CreatePresentationComposer", "CreateSimComposer", "LoadContent" }, methods);
            Assert.Equal(typeof(ISimComposer), factory.GetMethod("CreateSimComposer")!.ReturnType);
            Assert.Equal(new[] { typeof(IReadOnlyList<IContentDefinition>) }, factory.GetMethod("CreateSimComposer")!.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.Equal(new[] { typeof(ISimComposer) }, factory.GetMethod("CreateHeadlessRun")!.GetParameters().Select(p => p.ParameterType).ToArray());
            Assert.Equal(new[] { typeof(IContentSource) }, factory.GetMethod("LoadContent")!.GetParameters().Select(p => p.ParameterType).ToArray());
        }
    }
}
