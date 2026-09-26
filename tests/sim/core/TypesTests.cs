using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Interface conformance for the value types T-001 declares: 08 §8.4,
    /// §8.5, §8.5a, §8.9, §8.10, §8.11a, 10 §10.2, mapped to C# by 07 L10.
    /// </summary>
    public sealed class TypesTests
    {
        private static bool IsReadOnlyStruct(Type t)
        {
            return t.IsValueType
                && t.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }

        [Fact]
        public void test_types_idl_structs_are_public_readonly_structs()
        {
            Type[] types =
            {
                typeof(SystemId), typeof(EntityId), typeof(FlightId), typeof(EventId),
                typeof(EventEnvelope), typeof(EventRef), typeof(TickContext), typeof(Checkpoint),
                typeof(SimHostConfig), typeof(SystemServices), typeof(LogArgs),
            };
            var offenders = types.Where(t => !t.IsPublic || !IsReadOnlyStruct(t)).Select(t => t.Name).ToList();
            Assert.True(offenders.Count == 0, "not a public readonly struct (07 L10): " + string.Join(", ", offenders));
        }

        [Fact]
        public void test_types_state_hasher_is_mutable_public_struct_implementing_interface()
        {
            Type t = typeof(StateHasher);
            Assert.True(t.IsPublic && t.IsValueType, "StateHasher must be a public struct (08 §8.9)");
            Assert.False(IsReadOnlyStruct(t), "StateHasher is mutable, an exception to 07 L10 (08 §8.9)");
            Assert.True(typeof(IStateHasher).IsAssignableFrom(t), "StateHasher must implement IStateHasher");
        }

        [Fact]
        public void test_types_system_id_equality_is_by_value()
        {
            var a = new SystemId(3);
            var b = new SystemId(3);
            var c = new SystemId(4);
            Assert.Equal((ushort)3, a.Value);
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.True(a != c);
            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
            Assert.True(typeof(IEquatable<SystemId>).IsAssignableFrom(typeof(SystemId)));
        }

        [Fact]
        public void test_types_entity_and_flight_id_equality_is_by_value()
        {
            var e1 = new EntityId(0x0003_0000_0000_0001UL);
            var e2 = new EntityId(0x0003_0000_0000_0001UL);
            var e3 = new EntityId(2UL);
            Assert.Equal(0x0003_0000_0000_0001UL, e1.Value);
            Assert.True(e1 == e2);
            Assert.True(e1 != e3);
            Assert.True(e1.Equals(e2));
            Assert.True(typeof(IEquatable<EntityId>).IsAssignableFrom(typeof(EntityId)));

            var f1 = new FlightId(77UL);
            var f2 = new FlightId(77UL);
            var f3 = new FlightId(78UL);
            Assert.Equal(77UL, f1.Value);
            Assert.True(f1 == f2);
            Assert.True(f1 != f3);
            Assert.True(f1.Equals(f2));
            Assert.True(typeof(IEquatable<FlightId>).IsAssignableFrom(typeof(FlightId)));
        }

        [Fact]
        public void test_types_event_id_orders_by_tick_then_sequence()
        {
            var a = new EventId(1UL, 9U);
            var b = new EventId(2UL, 0U);
            var c = new EventId(2UL, 1U);
            var c2 = new EventId(2UL, 1U);
            Assert.Equal(2UL, c.Tick);
            Assert.Equal(1U, c.Sequence);
            Assert.True(a.CompareTo(b) < 0, "(1,9) < (2,0)");
            Assert.True(b.CompareTo(c) < 0, "(2,0) < (2,1)");
            Assert.True(c.CompareTo(a) > 0, "(2,1) > (1,9)");
            Assert.Equal(0, c.CompareTo(c2));
            Assert.True(c == c2);
            Assert.True(a != b);
            Assert.True(c.Equals(c2));
            Assert.True(typeof(IComparable<EventId>).IsAssignableFrom(typeof(EventId)));
            Assert.True(typeof(IEquatable<EventId>).IsAssignableFrom(typeof(EventId)));
        }

        [Fact]
        public void test_types_event_ref_none_is_static_readonly_without_value()
        {
            FieldInfo? f = typeof(EventRef).GetField("None", BindingFlags.Public | BindingFlags.Static);
            Assert.True(f != null && f.IsInitOnly, "EventRef.None must be a public static readonly field (08 §8.6)");
            Assert.False(EventRef.None.HasValue);
        }

        [Fact]
        public void test_types_event_ref_and_envelope_carry_their_members()
        {
            var id = new EventId(12UL, 3U);
            var cause = new EventRef(new EventId(12UL, 1U), true);
            Assert.True(cause.HasValue);
            Assert.Equal(new EventId(12UL, 1U), cause.Id);

            var env = new EventEnvelope(id, 12UL, new SystemId(5), cause);
            Assert.Equal(id, env.Id);
            Assert.Equal(12UL, env.Tick);
            Assert.Equal(new SystemId(5), env.Source);
            Assert.True(env.Cause.HasValue);
            Assert.Equal(new EventId(12UL, 1U), env.Cause.Id);
        }

        [Fact]
        public void test_types_checkpoint_carries_its_members()
        {
            ulong[] systems = { 5UL, 6UL };
            var cp = new Checkpoint(600UL, 0xAAUL, 0xBBUL, systems);
            Assert.Equal(600UL, cp.Tick);
            Assert.Equal(0xAAUL, cp.WorldHash);
            Assert.Equal(0xBBUL, cp.CoreHash);
            Assert.Same(systems, cp.SystemHashes);
        }

        [Theory]
        [InlineData("SYSTEM_CORE", typeof(SystemId))]
        [InlineData("PLAYER_LOCAL", typeof(PlayerId))]
        public void test_types_id_constant_is_static_readonly_field_of_sim_constants(string name, Type type)
        {
            // Q-023: public static readonly members of SimConstants, exact names.
            FieldInfo? f = typeof(SimConstants).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(f != null, $"SimConstants.{name} is missing (08 §8.7, Q-023)");
            Assert.True(f!.IsInitOnly, $"SimConstants.{name} must be static readonly (Q-023)");
            Assert.Equal(type, f.FieldType);
        }

        [Fact]
        public void test_types_system_core_is_system_id_zero()
        {
            Assert.Equal(new SystemId(0), SimConstants.SYSTEM_CORE);
            Assert.Equal((ushort)0, SimConstants.SYSTEM_CORE.Value);
        }

        [Fact]
        public void test_types_player_local_is_player_id_zero()
        {
            Assert.Equal((ushort)0, SimConstants.PLAYER_LOCAL.Value);
        }

        [Fact]
        public void test_types_log_args_count_follows_constructor_arity()
        {
            LogArgs none = default;
            Assert.Equal(0, none.Count);

            var one = new LogArgs(-1L);
            Assert.Equal(1, one.Count);
            Assert.Equal(-1L, one.A0);

            var two = new LogArgs(10L, 20L);
            Assert.Equal(2, two.Count);
            Assert.Equal(20L, two.A1);

            var three = new LogArgs(10L, 20L, 30L);
            Assert.Equal(3, three.Count);
            Assert.Equal(30L, three.A2);

            var four = new LogArgs(10L, 20L, 30L, long.MinValue);
            Assert.Equal(4, four.Count);
            Assert.Equal(10L, four.A0);
            Assert.Equal(long.MinValue, four.A3);
        }

        [Fact]
        public void test_types_enums_have_spec_underlying_types_and_values()
        {
            Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(LogLevel)));
            Assert.Equal(0, (int)LogLevel.Debug);
            Assert.Equal(1, (int)LogLevel.Info);
            Assert.Equal(2, (int)LogLevel.Warning);
            Assert.Equal(3, (int)LogLevel.Error);

            Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(LogKey)));
            Assert.Equal(0, (int)LogKey.None);

            Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(CommandKind)));
            Assert.Equal(0, (int)CommandKind.NoOp);

            // 07 L10: no IDL underlying type, so int, numbered in declared order.
            Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(CommandRejection)));
            Assert.Equal(0, (int)CommandRejection.None);
            Assert.Equal(1, (int)CommandRejection.TooLate);
            Assert.Equal(2, (int)CommandRejection.UnknownKind);
            Assert.Equal(3, (int)CommandRejection.MalformedPayload);
            Assert.Equal(4, (int)CommandRejection.NotPermitted);
        }

        [Fact]
        public void test_types_sim_invariant_exception_is_sealed_with_one_public_constructor()
        {
            Type t = typeof(SimInvariantException);
            Assert.True(t.IsSealed, "SimInvariantException is sealed (08 §8.5a)");
            Assert.True(typeof(Exception).IsAssignableFrom(t));
            ConstructorInfo ctor = Assert.Single(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Type[] ps = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.Equal(new[] { typeof(string), typeof(ulong) }, ps);
        }

        [Fact]
        public void test_types_sim_invariant_exception_constructor_sets_tick_without_world_hash()
        {
            var e = new SimInvariantException("cascade overflow", 42UL);
            Assert.Equal(42UL, e.Tick);
            Assert.False(e.HasWorldHash);
            Assert.Contains("cascade overflow", e.Message, StringComparison.Ordinal);
            Assert.Null(e.InnerException);
        }

        [Fact]
        public void test_types_sim_host_factory_is_static_class()
        {
            Type t = typeof(SimHostFactory);
            Assert.True(t.IsPublic && t.IsAbstract && t.IsSealed, "SimHostFactory must be a public static class (07 L10)");
            MethodInfo? m = t.GetMethod("CreateBuilder", BindingFlags.Public | BindingFlags.Static);
            Assert.True(m != null, "SimHostFactory.CreateBuilder is missing (08 §8.11a)");
            Assert.Equal(typeof(ISimHostBuilder), m!.ReturnType);
        }
    }
}
