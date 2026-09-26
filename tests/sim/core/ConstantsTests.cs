using System;
using System.Globalization;
using System.Reflection;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// 08 §8.1 and 07 L10: the constant table lives in public static class
    /// SimConstants as public const members. The five tick-arithmetic constants
    /// are ulong and the rest are int (Q-014). Types are checked by reflection
    /// so a wrong type fails one test instead of the whole build.
    /// </summary>
    public sealed class ConstantsTests
    {
        [Theory]
        [InlineData("TICK_MS", "System.Int32", 100L)]
        [InlineData("SIM_SECONDS_PER_TICK", "System.Int32", 6L)]
        [InlineData("TICKS_PER_SIM_MINUTE", "System.UInt64", 10L)]
        [InlineData("TICKS_PER_SIM_HOUR", "System.UInt64", 600L)]
        [InlineData("TICKS_PER_SIM_DAY", "System.UInt64", 14400L)]
        [InlineData("HASH_CHECKPOINT_TICKS", "System.UInt64", 600L)]
        [InlineData("COMMAND_MIN_LEAD_TICKS", "System.UInt64", 1L)]
        [InlineData("MAX_EVENT_CASCADE_PASSES", "System.Int32", 8L)]
        [InlineData("MAX_EVENTS_PER_TICK", "System.Int32", 4096L)]
        [InlineData("MAX_ATTRIBUTION_DEPTH", "System.Int32", 6L)]
        [InlineData("FX_FRACTIONAL_BITS", "System.Int32", 32L)]
        public void test_constants_field_matches_spec_table(string name, string typeName, long value)
        {
            FieldInfo? f = typeof(SimConstants).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.True(f != null, $"SimConstants.{name} is missing (08 §8.1)");
            Assert.True(f!.IsLiteral, $"SimConstants.{name} must be a public const (07 L10)");
            Assert.Equal(typeName, f.FieldType.FullName);
            Assert.Equal(value, Convert.ToInt64(f.GetRawConstantValue(), CultureInfo.InvariantCulture));
        }

        [Fact]
        public void test_constants_class_is_public_static()
        {
            Type t = typeof(SimConstants);
            Assert.True(t.IsPublic, "SimConstants must be public");
            Assert.True(t.IsAbstract && t.IsSealed, "SimConstants must be a static class (07 L10)");
        }

        [Fact]
        public void test_constants_derived_tick_values_agree_with_seconds_per_tick()
        {
            ulong secondsPerTick = (ulong)SimConstants.SIM_SECONDS_PER_TICK;
            ulong minute = SimConstants.TICKS_PER_SIM_MINUTE;
            ulong hour = SimConstants.TICKS_PER_SIM_HOUR;
            ulong day = SimConstants.TICKS_PER_SIM_DAY;
            ulong checkpoint = SimConstants.HASH_CHECKPOINT_TICKS;
            Assert.Equal(60UL / secondsPerTick, minute);
            Assert.Equal(minute * 60UL, hour);
            Assert.Equal(hour * 24UL, day);
            Assert.Equal(hour, checkpoint);
        }
    }
}
