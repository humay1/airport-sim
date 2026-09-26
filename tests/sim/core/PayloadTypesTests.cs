using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-026's relocated and payload types as a set: the enums (06, 10
    /// §10.4, 11 §11.3, 13 §13.3, 14 §14.3, PascalCase per 07 L10 / Q-028),
    /// the single-Value id structs (07 L10: IEquatable, == and !=), and no
    /// floating point on the sim.core surface (08 §8.3, CLAUDE.md).
    /// </summary>
    public sealed class PayloadTypesTests
    {
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static readonly Assembly Core = typeof(SimConstants).Assembly;

        [Fact]
        public void test_payload_types_enums_match_spec_names_and_ordinals()
        {
            var violations = PayloadShapes.Enums.SelectMany(e => PayloadShapes.EnumViolations(e.Enum, e.Names)).ToList();
            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Fact]
        public void test_payload_types_enum_members_are_pascal_case()
        {
            // 07 L10 (Q-028): no underscores, first letter upper-case.
            var bad = PayloadShapes.Enums
                .SelectMany(e => Enum.GetNames(e.Enum).Select(n => (e.Enum.Name, n)))
                .Where(x => x.n.Contains('_') || !char.IsUpper(x.n[0]))
                .Select(x => $"{x.Name}.{x.n}")
                .ToList();
            Assert.True(bad.Count == 0, "not PascalCase (07 L10, Q-028): " + string.Join(", ", bad));
            Assert.Equal(21, Enum.GetNames(typeof(DelayCategory)).Length);
        }

        [Fact]
        public void test_payload_types_id_structs_have_value_constructor_and_value_equality()
        {
            var v = new List<string>();
            foreach ((Type id, Type value) in PayloadShapes.Ids)
            {
                string n = id.Name;
                if (!id.IsPublic || !PayloadShapes.IsReadOnlyStruct(id))
                {
                    v.Add($"{n}: not a public readonly struct (07 L10)");
                }

                ConstructorInfo[] ctors = id.GetConstructors(PublicInstance);
                if (ctors.Length != 1 || !ctors[0].GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { value }))
                {
                    v.Add($"{n}: expected exactly one public constructor ({value.Name})");
                    continue;
                }

                PropertyInfo? prop = id.GetProperty("Value", PublicInstance);
                if (prop == null || prop.PropertyType != value || prop.SetMethod != null)
                {
                    v.Add($"{n}.Value: expected get-only {value.Name}");
                    continue;
                }

                if (!typeof(IEquatable<>).MakeGenericType(id).IsAssignableFrom(id))
                {
                    v.Add($"{n}: does not implement IEquatable<{n}> (07 L10)");
                }

                MethodInfo? eq = id.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static, null, new[] { id, id }, null);
                MethodInfo? ne = id.GetMethod("op_Inequality", BindingFlags.Public | BindingFlags.Static, null, new[] { id, id }, null);
                if (eq == null || ne == null)
                {
                    v.Add($"{n}: missing == or != (07 L10)");
                    continue;
                }

                object five = Convert.ChangeType(5, value, System.Globalization.CultureInfo.InvariantCulture);
                object six = Convert.ChangeType(6, value, System.Globalization.CultureInfo.InvariantCulture);
                object a = ctors[0].Invoke(new[] { five });
                object a2 = ctors[0].Invoke(new[] { five });
                object c = ctors[0].Invoke(new[] { six });

                if (!Equals(five, prop.GetValue(a)))
                {
                    v.Add($"{n}: Value does not return the constructor argument");
                }

                if (!(bool)eq.Invoke(null, new[] { a, a2 })! || (bool)eq.Invoke(null, new[] { a, c })!)
                {
                    v.Add($"{n}: == is not value equality");
                }

                if ((bool)ne.Invoke(null, new[] { a, a2 })! || !(bool)ne.Invoke(null, new[] { a, c })!)
                {
                    v.Add($"{n}: != is not value inequality");
                }

                if (!a.Equals(a2) || a.Equals(c) || a.GetHashCode() != a2.GetHashCode())
                {
                    v.Add($"{n}: Equals/GetHashCode are not value-based");
                }
            }

            Assert.True(v.Count == 0, string.Join("\n", v));
        }

        [Fact]
        public void test_payload_types_id_structs_carry_full_value_range()
        {
            Assert.Equal(uint.MaxValue, new AirlineId(uint.MaxValue).Value);
            Assert.Equal(ushort.MaxValue, new RunwayId(ushort.MaxValue).Value);
            Assert.Equal(ushort.MaxValue, new StandId(ushort.MaxValue).Value);
            Assert.Equal(ushort.MaxValue, new TaxiNodeId(ushort.MaxValue).Value);
            Assert.Equal(ushort.MaxValue, new TaxiEdgeId(ushort.MaxValue).Value);
            Assert.Equal(ushort.MaxValue, new VehicleId(ushort.MaxValue).Value);
            Assert.Equal(ulong.MaxValue, new JobId(ulong.MaxValue).Value);
            Assert.Equal(uint.MaxValue, new NodeId(uint.MaxValue).Value);
            Assert.Equal(uint.MaxValue, new EdgeId(uint.MaxValue).Value);
            Assert.Equal(ulong.MaxValue, new CohortId(ulong.MaxValue).Value);
            Assert.True(new NodeId(1U) != new NodeId(2U));
            Assert.True(new CohortId(3UL) == new CohortId(3UL));
        }

        [Fact]
        public void test_payload_types_sim_core_surface_has_no_floating_point()
        {
            var floats = new HashSet<Type> { typeof(float), typeof(double), typeof(decimal) };
            bool IsFloat(Type t)
            {
                while (t.HasElementType)
                {
                    t = t.GetElementType()!;
                }

                if (floats.Contains(t))
                {
                    return true;
                }

                return t.IsGenericType && t.GetGenericArguments().Any(IsFloat);
            }

            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var v = new List<string>();
            foreach (Type t in Core.GetTypes().Where(t => !t.Name.StartsWith("<", StringComparison.Ordinal)))
            {
                v.AddRange(t.GetFields(all).Where(f => IsFloat(f.FieldType)).Select(f => $"{t.Name}.{f.Name} (field)"));
                v.AddRange(t.GetProperties(all).Where(p => IsFloat(p.PropertyType)).Select(p => $"{t.Name}.{p.Name} (property)"));
                foreach (MethodBase m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
                {
                    if ((m is MethodInfo mi && IsFloat(mi.ReturnType)) || m.GetParameters().Any(p => IsFloat(p.ParameterType)))
                    {
                        v.Add($"{t.Name}.{m.Name} (signature)");
                    }
                }
            }

            Assert.True(v.Count == 0, "floating point in sim.core (08 §8.3, CLAUDE.md):\n" + string.Join("\n", v));
            Assert.Contains(typeof(DelayNode), Core.GetTypes());
        }
    }
}
