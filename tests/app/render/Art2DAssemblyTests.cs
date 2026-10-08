using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using AirportSim.App.Render.Art2D;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>
    /// Static rules on the compiled 2D art (Q-130): 15 §15.3 (headless, the
    /// scene layer's rules, netstandard2.1, no static mutable state, the scene
    /// layer never references it) and §15.17's public surface.
    /// </summary>
    public sealed class Art2DAssemblyTests
    {
        private static readonly Assembly Art = typeof(Art2DFactory).Assembly;
        private static readonly Assembly Scene = typeof(RenderFactory).Assembly;

        [Fact]
        public void test_art2d_assembly_references()
        {
            Assert.Equal("AirportSim.App.Render.Art2D", Art.GetName().Name);
            TargetFrameworkAttribute? tfm = Art.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on the 2D art");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);

            string[] banned =
            {
                "UnityEngine", "UnityEditor", "Unity.", "Godot", "Microsoft.Xna", "MonoGame", "SharpDX", "OpenTK", "Silk.NET", "SkiaSharp", "System.Drawing", "System.Windows",
                "AirportSim.App.Ui", "AirportSim.App.Host",
            };
            var names = Art.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            var offenders = names.Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal))).ToList();
            Assert.True(offenders.Count == 0, "the 2D art references (15 §15.3, §15.17: no engine reference, never app.ui): " + string.Join(", ", offenders));

            // It references the scene layer's types (15 §15.3).
            Assert.Contains("AirportSim.App.Render", names);

            // The scene layer never references the 2D art (15 §15.3, §15.17).
            var sceneRefs = Scene.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();
            Assert.DoesNotContain("AirportSim.App.Render.Art2D", sceneRefs);
        }

        [Fact]
        public void test_art2d_public_surface_matches_spec()
        {
            // 15 §15.17: "Every other art type is internal."
            string[] expected = { "ArtLayer", "Art2DConstants", "Art2DFactory", "AtlasRect", "ISpriteTessellator", "LayerColour", "Rgba", "SpriteAtlas" };
            var exported = Art.GetExportedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(), exported);
            foreach (Type t in Art.GetExportedTypes())
            {
                Assert.Equal("AirportSim.App.Render.Art2D", t.Namespace);
            }

            // Fields are get-only properties, and each struct has one constructor
            // taking them in declared order (15 §15.9 "C# shape").
            AssertStruct(typeof(Rgba), ("R", typeof(byte)), ("G", typeof(byte)), ("B", typeof(byte)), ("A", typeof(byte)));
            AssertStruct(typeof(AtlasRect), ("U0", typeof(float)), ("V0", typeof(float)), ("U1", typeof(float)), ("V1", typeof(float)));
            AssertStruct(typeof(SpriteAtlas), ("Size", typeof(int)), ("Mips", typeof(IReadOnlyList<byte[]>)));
            AssertStruct(
                typeof(ArtLayer),
                ("Colour", typeof(LayerColour)),
                ("Region", typeof(int)),
                ("Fixed", typeof(Rgb)),
                ("Rect", typeof(AtlasRect)),
                ("IsLogo", typeof(bool)),
                ("MinX", typeof(int)),
                ("MinY", typeof(int)),
                ("MaxX", typeof(int)),
                ("MaxY", typeof(int)),
                ("SliceInset", typeof(int)),
                ("SliceWorld", typeof(int)),
                ("Tile", typeof(int)),
                ("ShiftX", typeof(int)),
                ("ShiftY", typeof(int)));

            Assert.True(typeof(LayerColour).IsEnum);
            Assert.Equal(new[] { "Role", "Region", "Fixed" }, Enum.GetNames(typeof(LayerColour)));
            Assert.Equal(0, (int)LayerColour.Role);
            Assert.Equal(1, (int)LayerColour.Region);
            Assert.Equal(2, (int)LayerColour.Fixed);

            // ISpriteTessellator.
            Type tess = typeof(ISpriteTessellator);
            Assert.True(tess.IsInterface);
            MethodInfo? fill = tess.GetMethod("Fill");
            Assert.True(fill != null, "ISpriteTessellator.Fill");
            Assert.Equal(typeof(int), fill!.ReturnType);
            ParameterInfo[] ps = fill.GetParameters();
            Assert.Equal(3, ps.Length);
            Assert.Equal(typeof(RenderFrame).MakeByRefType(), ps[0].ParameterType);
            Assert.True(ps[0].IsIn, "Fill takes the frame by 'in'");
            Assert.Equal(typeof(IReadOnlyList<Rgba>), ps[1].ParameterType);
            Assert.Equal(typeof(bool), ps[2].ParameterType);
            AssertGetOnly(tess, "QuadCount", typeof(int));
            AssertGetOnly(tess, "Corners", typeof(float[]));
            AssertGetOnly(tess, "Uvs", typeof(float[]));
            AssertGetOnly(tess, "Colours", typeof(byte[]));
            Assert.Equal(new[] { "Fill", "get_Colours", "get_Corners", "get_QuadCount", "get_Uvs" }, tess.GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

            // Art2DConstants: public const int (Q-131 values; the elevation pair is
            // Q-132's, 15 §15.22, read by reflection only).
            Type consts = typeof(Art2DConstants);
            Assert.True(consts.IsAbstract && consts.IsSealed, "Art2DConstants is a static class");
            var expectedConsts = new Dictionary<string, int>
            {
                { "ATLAS_SIZE", 4096 }, { "LARGE_CELL", 512 }, { "SMALL_CELL", 128 }, { "ATLAS_MIP_COUNT", 6 }, { "ART_UNITS", 1024 },
                { "GROUND_TILE", 64 }, { "GROUND_TILES_PER_AXIS", 32 },
                { "ELEVATION_SCALE_M", 800 }, { "ELEVATION_SCALE_MAX", 2 },
            };
            FieldInfo[] fields = consts.GetFields(BindingFlags.Public | BindingFlags.Static);
            Assert.Equal(expectedConsts.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray(), fields.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            foreach (FieldInfo f in fields)
            {
                Assert.True(f.IsLiteral && f.FieldType == typeof(int), "Art2DConstants." + f.Name + " is a public const int");
                Assert.Equal(expectedConsts[f.Name], (int)f.GetRawConstantValue()!);
            }

            Assert.Equal(4096, Art2DConstants.ATLAS_SIZE);
            Assert.Equal(512, Art2DConstants.LARGE_CELL);
            Assert.Equal(128, Art2DConstants.SMALL_CELL);
            Assert.Equal(6, Art2DConstants.ATLAS_MIP_COUNT);
            Assert.Equal(1024, Art2DConstants.ART_UNITS);
            Assert.Equal(64, Art2DConstants.GROUND_TILE);
            Assert.Equal(32, Art2DConstants.GROUND_TILES_PER_AXIS);

            // Art2DFactory: 08 §8.11a's factory rule, stateless static methods only.
            Type factory = typeof(Art2DFactory);
            Assert.True(factory.IsAbstract && factory.IsSealed, "Art2DFactory is a static class");
            AssertStatic(factory, "BuildAtlas", typeof(SpriteAtlas));
            AssertStatic(factory, "LayersOf", typeof(IReadOnlyList<ArtLayer>), typeof(VisualId));
            AssertStatic(factory, "LogoRect", typeof(AtlasRect), typeof(LogoMark));
            AssertStatic(factory, "GroundLayer", typeof(ArtLayer));
            AssertStatic(factory, "CreateTessellator", typeof(ISpriteTessellator));
            var declared = factory.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "BuildAtlas", "CreateTessellator", "GroundLayer", "LayersOf", "LogoRect" }, declared);
            Assert.DoesNotContain(factory.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly), f => !f.IsLiteral);
        }

        [Fact]
        public void test_art2d_holds_no_static_mutable_state()
        {
            // 15 §15.3 "What counts" (Q-131): a static field in the 2D art is
            // allowed only if it is const, or static readonly of a primitive
            // type, string or an enum; any array, collection or other reference
            // type is static mutable state. Compiler-generated holders (lambda
            // caches, array-initialiser data) are not the art's own fields.
            var offenders = new List<string>();
            foreach (Type t in Art.GetTypes())
            {
                if (IsCompilerGenerated(t))
                {
                    continue;
                }

                foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral)
                    {
                        continue;
                    }

                    if (f.IsInitOnly && IsImmutable(f.FieldType))
                    {
                        continue;
                    }

                    offenders.Add(t.FullName + "." + f.Name + " : " + f.FieldType.Name + (f.IsInitOnly ? " (readonly, mutable type)" : " (writable)"));
                }
            }

            Assert.True(offenders.Count == 0, "static mutable state in the 2D art (15 §15.3, §15.17): " + string.Join("; ", offenders));
        }

        private static bool IsCompilerGenerated(Type t)
        {
            for (Type? x = t; x != null; x = x.DeclaringType)
            {
                if (x.Name.StartsWith("<", StringComparison.Ordinal) || x.IsDefined(typeof(CompilerGeneratedAttribute), false))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsImmutable(Type t)
        {
            return t.IsPrimitive || t.IsEnum || t == typeof(string);
        }

        private static void AssertStruct(Type t, params (string Name, Type Type)[] members)
        {
            Assert.True(t.IsValueType && !t.IsEnum, t.Name + " is a struct");
            Assert.True(
                t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                t.Name + " is a readonly struct (15 §15.17)");
            foreach ((string name, Type type) in members)
            {
                AssertGetOnly(t, name, type);
            }

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(members.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), props);

            ConstructorInfo[] ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.True(ctors.Length == 1, t.Name + " has exactly one public constructor, found " + ctors.Length);
            Assert.Equal(members.Select(m => m.Type).ToArray(), ctors[0].GetParameters().Select(p => p.ParameterType).ToArray());
        }

        private static void AssertGetOnly(Type t, string name, Type type)
        {
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(p != null, t.Name + "." + name + " is a public property");
            Assert.Equal(type, p!.PropertyType);
            Assert.True(p.CanRead, t.Name + "." + name + " is readable");
            Assert.True(p.SetMethod == null || !p.SetMethod.IsPublic, t.Name + "." + name + " is get-only");
        }

        private static void AssertStatic(Type t, string name, Type returns, params Type[] parameters)
        {
            MethodInfo? m = t.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
            Assert.True(m != null, t.Name + "." + name + "(" + string.Join(", ", parameters.Select(p => p.Name)) + ") is a public static method");
            Assert.Equal(returns, m!.ReturnType);
        }
    }
}
