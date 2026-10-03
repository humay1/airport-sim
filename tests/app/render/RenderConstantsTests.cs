using System;
using System.Reflection;
using Xunit;

namespace AirportSim.App.Render.Tests
{
    /// <summary>15 §15.2 (Q-099): the presentation constants, their values and C# types.</summary>
    public sealed class RenderConstantsTests
    {
        [Fact]
        public void test_render_constants_match_spec_values_and_types()
        {
            Type t = typeof(RenderConstants);
            Assert.True(t.IsAbstract && t.IsSealed && t.IsPublic, "RenderConstants is a public static class");
            Assert.Equal("AirportSim.App.Render", t.Namespace);

            (string Name, Type Type, object Value)[] expected =
            {
                ("AGENT_ZOOM_THRESHOLD", typeof(int), 120),
                ("MAX_DRAWN_AGENTS_PER_NODE", typeof(int), 256),
                ("MAX_DRAWN_LANES_PER_NODE", typeof(int), 32),
                ("MAX_CATCHUP_TICKS_PER_FRAME", typeof(uint), 3U),
                ("REAL_MICROSECONDS_PER_TICK_1X", typeof(long), 100000L),
            };
            foreach ((string name, Type type, object value) in expected)
            {
                FieldInfo? f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
                Assert.True(f != null, "RenderConstants." + name + " missing");
                Assert.True(f!.IsLiteral, name + " is not a const");
                Assert.Equal(type, f.FieldType);
                Assert.Equal(value, f.GetRawConstantValue());
            }
        }
    }
}
