using System;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Schedule.Tests
{
    /// <summary>
    /// 11 §11.9: Tick consumes no RNG and allocates nothing on the update
    /// path; only day materialisation, at the day boundary, may allocate.
    /// </summary>
    public sealed class ScheduleTickTests
    {
        [Fact]
        public void test_schedule_tick_consumes_no_rng()
        {
            var trap = new TrapRandomService();
            var rig = new DirectRig(Fixture.Bytes(), rng: trap);
            rig.RunTo(2UL * SchedConst.TicksPerDay);
            Assert.Equal(0, trap.StreamCalls);
            Assert.Equal(0, trap.Draws);

            var trapWithFlow = new TrapRandomService();
            var flowRig = new DirectRig(Fixture.Bytes(), flow: new RecordingFlow(), rng: trapWithFlow);
            flowRig.RunTo(2UL * SchedConst.TicksPerDay);
            Assert.Equal(0, trapWithFlow.StreamCalls);
            Assert.Equal(0, trapWithFlow.Draws);
            Assert.True(flowRig.Flow!.InjectCalls > 0);
        }

        [Fact]
        public void test_schedule_tick_publishes_through_tick_context()
        {
            var rig = new DirectRig(Fixture.Bytes());
            rig.RunTo(1);
            var pub = (RecordingPublisher)rig.Publisher;
            Assert.Equal(400, pub.Events.Count);
            for (int i = 0; i < pub.Events.Count; i += 2)
            {
                Assert.IsType<FlightPlanPublished>(pub.Events[i].Event);
                Assert.False(pub.Events[i].Cause.HasValue);
                var ms = Assert.IsType<FlightMilestoneReached>(pub.Events[i + 1].Event);
                Assert.True(pub.Events[i + 1].Cause.HasValue);
                Assert.Equal(pub.Events[i].Id, pub.Events[i + 1].Cause.Id);
                Assert.Equal(((FlightPlanPublished)pub.Events[i].Event).Flight, ms.Flight);
            }
        }

        private static long MeasureAllocations(DirectRig rig, ulong from, ulong to)
        {
            rig.RunTo(from);
            long total = 0;
            while (rig.NextTick < to)
            {
                bool boundary = rig.NextTick % SchedConst.TicksPerDay == 0UL;
                long before = GC.GetAllocatedBytesForCurrentThread();
                rig.TickOnce();
                long delta = GC.GetAllocatedBytesForCurrentThread() - before;
                if (!boundary)
                {
                    total += delta;
                }
            }

            return total;
        }

        [Fact]
        public void test_schedule_tick_allocates_nothing_outside_day_boundaries_without_flow()
        {
            var rig = new DirectRig(Fixture.Bytes(), counting: true);
            // Day 0 warms every path; day 1 is measured.
            long bytes = MeasureAllocations(rig, SchedConst.TicksPerDay, 2UL * SchedConst.TicksPerDay);
            Assert.True(bytes == 0L, "Tick allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1");
            Assert.True(((CountingPublisher)rig.Publisher).Plans > 0);
        }

        [Fact]
        public void test_schedule_tick_allocates_nothing_outside_day_boundaries_with_flow()
        {
            var flow = new RecordingFlow(recording: false);
            var rig = new DirectRig(Fixture.Bytes(), flow: flow, counting: true);
            long bytes = MeasureAllocations(rig, SchedConst.TicksPerDay, 2UL * SchedConst.TicksPerDay);
            Assert.True(bytes == 0L, "Tick allocated " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over day 1");
            Assert.True(flow.InjectCalls > 0);
        }
    }
}
