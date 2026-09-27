using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AirportSim.Sim.Core;
using AirportSim.Sim.World;
using Xunit;

namespace AirportSim.Sim.World.Tests
{
    /// <summary>
    /// CLAUDE.md "No singletons, no global mutable state, no hidden statics"
    /// and 08 §8.11a: constants and static readonly values of immutable types
    /// are not state, and they are the only static data a module may hold.
    /// Every static field of AirportSim.Sim.World is checked, public or private.
    /// (Copied from tests/sim/core/StaticStateTests.cs (T-027); 07 L3 lets each test project see only its own module, so each module carries the check.) A field must be a
    /// const, or readonly with an immutable type: a primitive, an enum,
    /// string, Nullable of one of these, or a readonly struct whose instance
    /// fields are all of such types (Fx, the id structs, EventRef.None). An
    /// array, an Encoding, a collection, any other class, or a non-readonly
    /// struct is mutable, even behind readonly.
    /// </summary>
    public sealed class WorldStaticStateTests
    {
        private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Probes for the classifier's own sanity check.
        private struct MutableProbe
        {
            public MutableProbe(int x)
            {
                X = x;
            }

            public int X;
        }

        private readonly struct ArrayHoldingProbe
        {
            public ArrayHoldingProbe(int[] a)
            {
                A = a;
            }

            public int[] A { get; }
        }

        private readonly struct NestedProbe
        {
            public NestedProbe(SystemId id, string s, LogLevel level, ulong? n)
            {
                Id = id;
                S = s;
                Level = level;
                N = n;
            }

            public SystemId Id { get; }

            public string S { get; }

            public LogLevel Level { get; }

            public ulong? N { get; }
        }

        /// <summary>Returns null if <paramref name="t"/> is immutable, else why it is not.</summary>
        private static string? WhyMutable(Type t, HashSet<Type> visiting)
        {
            if (t.IsGenericParameter)
            {
                return $"generic parameter {t.Name} (its type is not known to be immutable)";
            }

            if (t.IsPrimitive || t.IsEnum || t == typeof(string))
            {
                return null;
            }

            Type? inner = Nullable.GetUnderlyingType(t);
            if (inner != null)
            {
                return WhyMutable(inner, visiting);
            }

            if (t.IsArray)
            {
                return $"array {t.Name} (elements are writable)";
            }

            if (!t.IsValueType)
            {
                return $"reference type {t.FullName} (not known to be immutable)";
            }

            bool isReadOnly = t.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
            if (!isReadOnly)
            {
                return $"non-readonly struct {t.FullName}";
            }

            if (!visiting.Add(t))
            {
                return null;
            }

            foreach (FieldInfo f in t.GetFields(AllInstance))
            {
                string? why = WhyMutable(f.FieldType, visiting);
                if (why != null)
                {
                    return $"readonly struct {t.FullName} holds {f.Name}: {why}";
                }
            }

            return null;
        }

        private static string? WhyMutable(Type t) => WhyMutable(t, new HashSet<Type>());

        private static bool IsCompilerGenerated(Type t)
        {
            // Lambda caches (<>c), fixed-buffer and <PrivateImplementationDetails>
            // types are emitted by the compiler, not written as state.
            for (Type? c = t; c != null; c = c.DeclaringType)
            {
                if (c.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Assembly> SimAssemblies()
        {
            // Only this module's own assembly: sim.core has its own copy of this
            // test, so an offender is reported by the module that owns it.
            return new List<Assembly> { typeof(WorldFactory).Assembly };
        }

        private static IEnumerable<Type> LoadableTypes(Assembly a)
        {
            try
            {
                return a.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.OfType<Type>();
            }
        }

        [Fact]
        public void test_world_static_state_holds_no_mutable_static_fields()
        {
            // The classifier must reject what the rule rejects and accept what it allows.
            Assert.NotNull(WhyMutable(typeof(string[])));
            Assert.NotNull(WhyMutable(typeof(UTF8Encoding)));
            Assert.NotNull(WhyMutable(typeof(Encoding)));
            Assert.NotNull(WhyMutable(typeof(List<int>)));
            Assert.NotNull(WhyMutable(typeof(Dictionary<string, int>)));
            Assert.NotNull(WhyMutable(typeof(MutableProbe)));
            Assert.NotNull(WhyMutable(typeof(ArrayHoldingProbe)));
            Assert.NotNull(WhyMutable(typeof(object)));
            Assert.Null(WhyMutable(typeof(int)));
            Assert.Null(WhyMutable(typeof(string)));
            Assert.Null(WhyMutable(typeof(LogLevel)));
            Assert.Null(WhyMutable(typeof(ulong?)));
            Assert.Null(WhyMutable(typeof(Fx)));
            Assert.Null(WhyMutable(typeof(SystemId)));
            Assert.Null(WhyMutable(typeof(EventRef)));
            Assert.Null(WhyMutable(typeof(NestedProbe)));

            List<Assembly> assemblies = SimAssemblies();
            var offenders = new List<string>();
            int scanned = 0;
            foreach (Assembly a in assemblies)
            {
                foreach (Type t in LoadableTypes(a).Where(t => !IsCompilerGenerated(t)))
                {
                    foreach (FieldInfo f in t.GetFields(AllStatic))
                    {
                        scanned++;
                        if (f.IsLiteral)
                        {
                            continue;
                        }

                        string where = $"{a.GetName().Name}: {t.FullName}.{f.Name}";
                        if (!f.IsInitOnly)
                        {
                            offenders.Add($"{where} is a non-readonly static field ({f.FieldType.Name})");
                            continue;
                        }

                        string? why = WhyMutable(f.FieldType);
                        if (why != null)
                        {
                            offenders.Add($"{where} is static readonly but mutable: {why}");
                        }
                    }
                }
            }

            Assert.True(
                offenders.Count == 0,
                $"mutable static state (CLAUDE.md, 08 §8.11a) in {string.Join(", ", assemblies.Select(x => x.GetName().Name))} ({scanned} static fields scanned):\n" + string.Join("\n", offenders));

            // Anchor: the module's own assembly was the one scanned.
            Assert.Contains(assemblies, x => x == typeof(WorldFactory).Assembly);
        }
    }
}
