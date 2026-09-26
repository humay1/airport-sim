using System;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// The delay types T-026 relocates into sim.core: DelayCategory (06,
    /// fixed list, PascalCase per 07 L10 / Q-028), DelaySource and
    /// DelayEventId (14 §14.3).
    /// </summary>
    public sealed class DelayTests
    {
        [Fact]
        public void test_delay_category_member_list_matches_spec_and_is_stable()
        {
            Assert.Empty(PayloadShapes.EnumViolations(typeof(DelayCategory), PayloadShapes.DelayCategoryNames));
            Assert.Equal(21, Enum.GetValues(typeof(DelayCategory)).Length);
            Assert.Equal(0, (int)DelayCategory.LateInbound);
            Assert.Equal(3, (int)DelayCategory.StandUnavailable);
            Assert.Equal(12, (int)DelayCategory.SecurityQueue);
            Assert.Equal(13, (int)DelayCategory.ImmigrationQueue);
            Assert.Equal(17, (int)DelayCategory.AtcFlow);
            Assert.Equal(20, (int)DelayCategory.Propagated);
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
    }
}
