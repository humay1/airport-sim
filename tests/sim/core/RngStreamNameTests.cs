using System;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-002. RngStreamName against 08-interfaces-core.md §8.8 "Names" (Q-019, Q-023):
    /// the whole value must match <c>sim\.[a-z]+\.[a-z0-9_]+</c>, anchored as
    /// <c>\A…\z</c>; null throws ArgumentNullException, anything else malformed
    /// throws ArgumentException. Equality is ordinal (Q-014, 07 L10).
    /// </summary>
    public sealed class RngStreamNameTests
    {
        [Theory]
        [InlineData("sim.flow.showup")]
        [InlineData("sim.schedule.jitter")]
        [InlineData("sim.airside.taxi")]
        [InlineData("sim.a.b")]
        [InlineData("sim.q.0")]
        [InlineData("sim.delay.x9")]
        [InlineData("sim.turnaround.job_order")]
        [InlineData("sim.baggage.belt_0")]
        [InlineData("sim.flow._")]
        [InlineData("sim.flow.0123456789")]
        [InlineData("sim.abcdefghijklmnopqrstuvwxyz.abcdefghijklmnopqrstuvwxyz0123456789_")]
        public void test_rng_stream_name_well_formed_value_is_kept_verbatim(string value)
        {
            var name = new RngStreamName(value);
            Assert.Equal(value, name.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("sim")]
        [InlineData("sim.")]
        [InlineData("sim..")]
        [InlineData("sim.flow")]
        [InlineData("sim.flow.")]
        [InlineData("sim..showup")]
        [InlineData("flow.showup")]
        [InlineData(".flow.showup")]
        [InlineData("sim-flow-showup")]
        [InlineData("Sim.flow.showup")]
        [InlineData("SIM.FLOW.SHOWUP")]
        [InlineData("sim.Flow.showup")]
        [InlineData("sim.flow.Showup")]
        [InlineData("sim.f1ow.showup")]
        [InlineData("sim.fl_ow.showup")]
        [InlineData("sim.flow.show-up")]
        [InlineData("sim.flow.show up")]
        [InlineData("sim.flow.show.up")]
        [InlineData("core.flow.showup")]
        // Whole-string match (Q-023): nothing before the first or after the last character.
        [InlineData("xsim.flow.showup")]
        [InlineData(" sim.flow.showup")]
        [InlineData("sim.flow.showup ")]
        [InlineData("sim.flow.showup\n")]
        [InlineData("sim.flow.showup\r\n")]
        [InlineData("\nsim.flow.showup")]
        [InlineData("sim.flow.showup\0")]
        [InlineData("sim.flow.showup\nsim.flow.showup")]
        [InlineData("sim.flow.showup!")]
        // [a-z] and [0-9] are ASCII ranges, not letter or digit classes, and are case-sensitive.
        [InlineData("sim.flöw.showup")]
        [InlineData("sim.flow.shоwup")]
        [InlineData("sim.flow.٣")]
        [InlineData("sim.flow.ｓhowup")]
        [InlineData("sim.Kflow.showup")]
        [InlineData("sim.flow.ı")]
        public void test_rng_stream_name_malformed_value_throws_argument_exception(string value)
        {
            Assert.Throws<ArgumentException>(() => new RngStreamName(value));
        }

        [Fact]
        public void test_rng_stream_name_null_value_throws_argument_null_exception()
        {
            Assert.Throws<ArgumentNullException>(() => new RngStreamName(null!));
        }

        [Fact]
        public void test_rng_stream_name_equal_values_compare_equal_ordinally()
        {
            // Two distinct string instances with the same characters.
            var a = new RngStreamName("sim.flow.showup");
            var b = new RngStreamName(new string("sim.flow.showup".ToCharArray()));
            Assert.True(a.Equals(b));
            Assert.True(((IEquatable<RngStreamName>)a).Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.True(a == b);
            Assert.False(a != b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Theory]
        [InlineData("sim.flow.showup", "sim.flow.showuq")]
        [InlineData("sim.flow.showup", "sim.flow.showup_")]
        [InlineData("sim.flow.a", "sim.flo.wa")]
        [InlineData("sim.flow.showup", "sim.airside.showup")]
        public void test_rng_stream_name_different_values_compare_unequal(string left, string right)
        {
            var a = new RngStreamName(left);
            var b = new RngStreamName(right);
            Assert.False(a.Equals(b));
            Assert.False(a.Equals((object)b));
            Assert.False(a == b);
            Assert.True(a != b);
        }
    }
}
