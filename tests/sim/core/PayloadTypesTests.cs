using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-026's relocated and payload types: the enums (06, 10 §10.4, 11
    /// §11.3, 13 §13.3, 14 §14.3), the single-Value id structs (07 L10:
    /// IEquatable, == and !=), their compiled home in sim.core (Q-018), and
    /// no floating point on the sim.core surface (08 §8.3, CLAUDE.md).
    /// </summary>
    public sealed class PayloadTypesTests
    {
        private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

        private static readonly Assembly Core = typeof(SimConstants).Assembly;

        private static List<string> EnumViolations(Type t, string[] names)
        {
            var v = new List<string>();
            if (!t.IsPublic || !t.IsEnum)
            {
                v.Add($"{t.Name}: not a public enum");
                return v;
            }

            // 07 L10: no IDL underlying type, so int, numbered in declared order from 0.
            if (Enum.GetUnderlyingType(t) != typeof(int))
            {
                v.Add($"{t.Name}: underlying {Enum.GetUnderlyingType(t).Name}, expected Int32");
            }

            string[] actual = Enum.GetValues(t).Cast<object>()
                .OrderBy(x => Convert.ToInt64(x, System.Globalization.CultureInfo.InvariantCulture))
                .Select(x => Enum.GetName(t, x) ?? "?")
                .ToArray();
            if (!actual.SequenceEqual(names))
            {
                v.Add($"{t.Name}: [{string.Join(", ", actual)}], expected [{string.Join(", ", names)}]");
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (Enum.IsDefined(t, names[i]))
                {
                    long ordinal = Convert.ToInt64(Enum.Parse(t, names[i]), System.Globalization.CultureInfo.InvariantCulture);
                    if (ordinal != i)
                    {
                        v.Add($"{t.Name}.{names[i]} = {ordinal}, expected {i}");
                    }
                }
            }

            return v;
        }

        [Fact]
        public void test_payload_enums_match_spec_names_and_ordinals()
        {
            var violations = PayloadShapes.Enums.SelectMany(e => EnumViolations(e.Enum, e.Names)).ToList();
            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Fact]
        public void test_delay_category_member_list_matches_spec_and_is_stable()
        {
            Assert.Empty(EnumViolations(typeof(DelayCategory), PayloadShapes.DelayCategoryNames));
            Assert.Equal(21, Enum.GetValues(typeof(DelayCategory)).Length);
            Assert.Equal(0, (int)DelayCategory.late_inbound);
            Assert.Equal(12, (int)DelayCategory.security_queue);
            Assert.Equal(13, (int)DelayCategory.immigration_queue);
            Assert.Equal(20, (int)DelayCategory.propagated);
        }

        [Fact]
        public void test_job_kind_enum_matches_spec_eight_values()
        {
            Assert.Empty(EnumViolations(typeof(JobKind), new[] { "Deboard", "BaggageUnload", "CabinClean", "Catering", "Fuel", "BaggageLoad", "PushbackPrep", "Boarding" }));
            Assert.Equal(8, Enum.GetValues(typeof(JobKind)).Length);
            Assert.Equal(7, (int)JobKind.Boarding);
        }

        [Fact]
        public void test_delay_source_passenger_hold_appended_last_ordinal_unchanged()
        {
            Assert.Equal(0, (int)DelaySource.FlightTotal);
            Assert.Equal(1, (int)DelaySource.InboundAircraft);
            Assert.Equal(2, (int)DelaySource.RunwayHold);
            Assert.Equal(3, (int)DelaySource.TaxiwayHold);
            Assert.Equal(4, (int)DelaySource.StandUnavailable);
            Assert.Equal(5, (int)DelaySource.TurnaroundJobWait);
            Assert.Equal(6, (int)DelaySource.Unexplained);
            Assert.Equal(7, (int)DelaySource.PassengerHold);
            Assert.Equal(8, Enum.GetValues(typeof(DelaySource)).Length);
        }

        [Fact]
        public void test_flight_milestone_enum_matches_spec_thirteen_values_ordinal_order()
        {
            Assert.Empty(EnumViolations(typeof(FlightMilestone), PayloadShapes.FlightMilestoneNames));
            Assert.Equal(13, Enum.GetValues(typeof(FlightMilestone)).Length);
            Assert.Equal(0, (int)FlightMilestone.PlanPublished);
            Assert.Equal(12, (int)FlightMilestone.Airborne);
        }

        [Fact]
        public void test_payload_id_structs_have_value_constructor_and_value_equality()
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

                object a = ctors[0].Invoke(new[] { Convert.ChangeType(5, value, System.Globalization.CultureInfo.InvariantCulture) });
                object a2 = ctors[0].Invoke(new[] { Convert.ChangeType(5, value, System.Globalization.CultureInfo.InvariantCulture) });
                object c = ctors[0].Invoke(new[] { Convert.ChangeType(6, value, System.Globalization.CultureInfo.InvariantCulture) });

                if (!Equals(Convert.ChangeType(5, value, System.Globalization.CultureInfo.InvariantCulture), prop.GetValue(a)))
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
        public void test_delay_event_id_struct_shape()
        {
            // 14 §14.3: a plain uint64 wrapper; 0 = none, a valid unallocated value.
            DelayEventId none = default;
            Assert.Equal(0UL, none.Value);
            Assert.True(new DelayEventId(0UL) == none);
            Assert.True(new DelayEventId(ulong.MaxValue) != none);
            Assert.Equal(ulong.MaxValue, new DelayEventId(ulong.MaxValue).Value);
            Assert.Equal(typeof(ulong), typeof(DelayEventId).GetProperty("Value")!.PropertyType);
        }

        [Fact]
        public void test_payload_id_structs_carry_full_value_range()
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
        public void test_airside_turnaround_delay_payload_types_compile_in_sim_core_only()
        {
            var types = new List<Type>();
            types.AddRange(PayloadShapes.AllStructShapes().Select(s => s.Type));
            types.AddRange(PayloadShapes.Ids.Select(i => i.Id));
            types.AddRange(PayloadShapes.Enums.Select(e => e.Enum));
            types.Add(typeof(IContentDefinition));
            types.Add(typeof(ContentIndexFactory));

            var misplaced = types
                .Where(t => t.Assembly != Core || t.Namespace != "AirportSim.Sim.Core")
                .Select(t => $"{t.FullName} in {t.Assembly.GetName().Name}")
                .ToList();
            Assert.True(misplaced.Count == 0, "not compiled in sim.core's RootNamespace (Q-018, 07 L6):\n" + string.Join("\n", misplaced));

            string[] modules = { "AirportSim.Sim.Airside", "AirportSim.Sim.Turnaround", "AirportSim.Sim.Delay", "AirportSim.Sim.Flow", "AirportSim.Sim.World", "AirportSim.Sim.Schedule" };
            var edges = Core.GetReferencedAssemblies().Select(a => a.Name).Where(n => modules.Contains(n)).ToList();
            Assert.True(edges.Count == 0, "sim.core references a module: " + string.Join(", ", edges));
        }

        [Fact]
        public void test_payload_sim_core_surface_has_no_floating_point()
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
