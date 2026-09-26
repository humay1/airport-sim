using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Structural rules on the compiled sim.core assembly: 07 L1/L6, CLAUDE.md
    /// "Code conventions", 02-determinism.md rule 1, 08 §8.11a (only constants,
    /// immutable static readonly values and factories are static) and §8.12
    /// (no threading). These scan metadata and IL; they never read source.
    /// </summary>
    public sealed class AssemblyTests
    {
        private static readonly Assembly Sim = typeof(SimConstants).Assembly;

        private const BindingFlags AllDeclared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        [Fact]
        public void test_assembly_targets_netstandard21_with_spec_name()
        {
            Assert.Equal("AirportSim.Sim.Core", Sim.GetName().Name);
            TargetFrameworkAttribute? tfm = Sim.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.True(tfm != null, "no TargetFrameworkAttribute on sim.core");
            Assert.Equal(".NETStandard,Version=v2.1", tfm!.FrameworkName);
        }

        [Fact]
        public void test_assembly_public_types_live_in_root_namespace()
        {
            var offenders = Sim.GetExportedTypes()
                .Where(t => t.Namespace != "AirportSim.Sim.Core")
                .Select(t => t.FullName)
                .ToList();
            Assert.True(offenders.Count == 0, "public types outside the RootNamespace (07 L6): " + string.Join(", ", offenders));
        }

        [Fact]
        public void test_assembly_references_no_engine_or_presentation()
        {
            string[] banned = { "UnityEngine", "Unity.", "UnityEditor", "Godot", "AirportSim.App", "AirportSim.Sim." };
            var offenders = Sim.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => banned.Any(b => n.StartsWith(b, StringComparison.Ordinal)))
                .ToList();
            Assert.True(offenders.Count == 0, "sim.core references (07 L2: none): " + string.Join(", ", offenders));
        }

        [Fact]
        public void test_assembly_static_fields_are_constants_or_immutable_readonly()
        {
            var offenders = new List<string>();
            foreach (Type t in Sim.GetTypes())
            {
                if (IsCompilerGeneratedType(t))
                {
                    continue;
                }

                foreach (FieldInfo f in t.GetFields(AllDeclared).Where(f => f.IsStatic))
                {
                    if (f.IsLiteral)
                    {
                        continue;
                    }

                    if (!f.IsInitOnly)
                    {
                        offenders.Add($"{t.FullName}.{f.Name} is mutable static state");
                    }
                    else if (f.FieldType.IsArray || IsMutableCollection(f.FieldType))
                    {
                        offenders.Add($"{t.FullName}.{f.Name} is a static readonly mutable container");
                    }
                }
            }

            Assert.True(offenders.Count == 0, "08 §8.11a / CLAUDE.md no global mutable state:\n" + string.Join("\n", offenders));
        }

        [Fact]
        public void test_assembly_never_references_wall_clock_random_or_threads()
        {
            var offenders = new SortedSet<string>(StringComparer.Ordinal);
            bool sawInvariantConstruction = false;
            foreach (Type t in Sim.GetTypes())
            {
                Type[]? typeArgs = t.IsGenericTypeDefinition ? t.GetGenericArguments() : null;
                IEnumerable<MethodBase> methods = t.GetMethods(AllDeclared).Cast<MethodBase>().Concat(t.GetConstructors(AllDeclared));
                foreach (MethodBase m in methods)
                {
                    byte[]? il;
                    try
                    {
                        il = m.GetMethodBody()?.GetILAsByteArray();
                    }
                    catch (Exception e) when (e is InvalidOperationException || e is BadImageFormatException)
                    {
                        continue;
                    }

                    if (il == null)
                    {
                        continue;
                    }

                    Type[]? methodArgs = m.IsGenericMethodDefinition ? m.GetGenericArguments() : null;
                    for (int i = 0; i < il.Length; i++)
                    {
                        int tokenAt;
                        byte op = il[i];
                        if (op == 0x28 || op == 0x6F || op == 0x73 || op == 0x7E || op == 0x80 || op == 0x8D || op == 0xD0)
                        {
                            tokenAt = i + 1;
                        }
                        else if (op == 0xFE && i + 1 < il.Length && (il[i + 1] == 0x06 || il[i + 1] == 0x07))
                        {
                            tokenAt = i + 2;
                        }
                        else
                        {
                            continue;
                        }

                        if (tokenAt + 4 > il.Length)
                        {
                            continue;
                        }

                        int token = BitConverter.ToInt32(il, tokenAt);
                        int table = (int)((uint)token >> 24);
                        if (table != 0x01 && table != 0x02 && table != 0x04 && table != 0x06 && table != 0x0A && table != 0x1B && table != 0x2B)
                        {
                            continue;
                        }

                        MemberInfo? member;
                        try
                        {
                            member = m.Module.ResolveMember(token, typeArgs, methodArgs);
                        }
                        catch (Exception e) when (e is ArgumentException || e is BadImageFormatException || e is TypeLoadException
                            || e is MissingMemberException || e is FileNotFoundException || e is FileLoadException)
                        {
                            continue;
                        }

                        if (op == 0x73 && member is ConstructorInfo ci && ci.DeclaringType == typeof(SimInvariantException)
                            && t != typeof(SimInvariantException))
                        {
                            sawInvariantConstruction = true;
                        }

                        string? why = member == null ? null : BannedReason(member);
                        if (why != null)
                        {
                            offenders.Add($"{t.FullName}.{m.Name}: {why}");
                        }
                    }
                }
            }

            Assert.True(offenders.Count == 0, "02-determinism rule 1 / CLAUDE.md / 08 §8.12:\n" + string.Join("\n", offenders));

            // Anchor: core must construct SimInvariantException for its own
            // limits and for the host wrap (08 §8.5a, §8.6), so a scanner that
            // resolves nothing cannot pass this test by finding nothing.
            Assert.True(sawInvariantConstruction, "IL scan found no construction of SimInvariantException outside its own type");
        }

        private static bool IsMutableCollection(Type t)
        {
            string ns = t.Namespace ?? string.Empty;
            return ns.StartsWith("System.Collections", StringComparison.Ordinal)
                && !ns.StartsWith("System.Collections.Immutable", StringComparison.Ordinal);
        }

        private static bool IsCompilerGeneratedType(Type t)
        {
            for (Type? c = t; c != null; c = c.DeclaringType)
            {
                if (c.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? BannedReason(MemberInfo member)
        {
            Type? type = member as Type ?? member.DeclaringType;
            string typeName = type?.FullName ?? string.Empty;
            string name = member.Name;

            if ((typeName == "System.DateTime" || typeName == "System.DateTimeOffset")
                && (name == "get_Now" || name == "get_UtcNow" || name == "get_Today"))
            {
                return $"wall-clock {typeName}.{name}";
            }

            if (typeName == "System.Environment" && (name == "get_TickCount" || name == "get_TickCount64"))
            {
                return $"wall-clock {typeName}.{name}";
            }

            if (typeName == "System.Guid" && name == "NewGuid")
            {
                return "nondeterministic System.Guid.NewGuid";
            }

            string[] bannedTypes =
            {
                "System.Diagnostics.Stopwatch", "System.Random", "System.Threading.Thread", "System.Threading.Timer",
                "System.Threading.ThreadPool", "System.Threading.Tasks.Task", "System.Threading.Tasks.Parallel",
            };
            foreach (string b in bannedTypes)
            {
                if (typeName == b || typeName.StartsWith(b + "`", StringComparison.Ordinal))
                {
                    return $"uses {typeName}";
                }
            }

            return null;
        }
    }
}
