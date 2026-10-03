using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 "Without sim.turnaround": at DoorsOpen + MinTurnaround (the
    /// arrival's, converted with TICKS_PER_SIM_MINUTE), in one tick, the
    /// departure's OnStand, Absorb and DoorsClosed; Pushback follows
    /// immediately (§12.3). §12.7: Absorb(stand.DepartureSinkNode, flight)
    /// once, unconditionally. §12.3: DoorsOpen is a fixed delay after OnStand.
    /// </summary>
    public sealed class DoorsTests
    {
        [Fact]
        public void test_doors_close_after_min_turnaround_when_turnaround_absent()
        {
            // Different MinTurnaround per row: the fallback timer is the arrival's.
            var rig = new HostRig(Csv.Of(Csv.Pair("A1", "D1", "06:00", "08:00", arrMinTurn: "35", depMinTurn: "45")), turnaroundRegistered: false);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(AirConst.TicksPerDay);

            ulong open = rig.Rec.Milestone(a1, FlightMilestone.DoorsOpen).Milestone.ActualTick;
            ulong close = open + (35UL * AirConst.TicksPerMinute);
            Rec onStand = rig.Rec.Milestone(d1, FlightMilestone.OnStand);
            Rec closed = rig.Rec.Milestone(d1, FlightMilestone.DoorsClosed);
            Rec pushback = rig.Rec.Milestone(d1, FlightMilestone.Pushback);
            Assert.Equal(close, onStand.Milestone.ActualTick);
            Assert.Equal(close, closed.Milestone.ActualTick);
            Assert.Equal(close, pushback.Milestone.ActualTick);
            Assert.True(onStand.Id.CompareTo(closed.Id) < 0, "departure OnStand must precede DoorsClosed");
            Assert.True(closed.Id.CompareTo(pushback.Id) < 0, "DoorsClosed must precede Pushback");

            ulong std = AirConst.At(8, 0);
            Assert.Equal(std, closed.Milestone.PlannedTick);
            Assert.Equal(std, pushback.Milestone.PlannedTick);

            Assert.False(rig.Rec.Has(a1, FlightMilestone.DoorsClosed));
            Assert.False(rig.Rec.Has(a1, FlightMilestone.Pushback));

            // 12 §12.11: the fallback handoff after a nonzero MinTurnaround has
            // Cause None; DoorsClosed names the departure OnStand just emitted;
            // Pushback names the DoorsClosed.
            Assert.False(onStand.Env.Cause.HasValue);
            Assert.Equal(onStand.Id, closed.Env.Cause.Id);
            Assert.Equal(closed.Id, pushback.Env.Cause.Id);
        }

        [Fact]
        public void test_doors_closed_calls_absorb_once_with_stand_departure_sink()
        {
            ScriptedFlow flow = ScriptedFlow.None();
            var rig = new HostRig(Csv.Of(Csv.Pair("A1", "D1", "06:00", "08:00")), flow: flow);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(AirConst.TicksPerDay);

            ulong closed = rig.Rec.Milestone(d1, FlightMilestone.DoorsClosed).Milestone.ActualTick;
            var absorbs = flow.AbsorbsOf(d1);
            Assert.Single(absorbs);
            Assert.Equal(closed, absorbs[0].Tick);
            Assert.Equal(FixtureLayout.Sink(FixtureLayout.S1), absorbs[0].Sink);
            Assert.Empty(flow.AbsorbsOf(a1));
            Assert.Equal(1L, flow.AbsorbCalls);
            Assert.Equal(0L, flow.InjectCalls);
        }

        [Fact]
        public void test_doors_open_follows_on_stand_by_rules_door_delay()
        {
            // 12 §12.3 (Q-047): DoorsOpen at OnStand + DoorsOpenDelayMinutes ×
            // TICKS_PER_SIM_MINUTE, planned and actual, read from AirsideRules.
            var rows = new[]
            {
                Csv.Row("X1", "A", "06:00"), Csv.Row("X2", "A", "06:20"), Csv.Row("X3", "A", "09:40"), Csv.Row("X4", "A", "13:00"),
            };
            foreach (uint minutes in new[] { AirConst.FixtureDoorDelayMinutes, 7U })
            {
                var rig = new HostRig(Csv.Of(rows), doorDelayMinutes: minutes);
                rig.RunTo(AirConst.TicksPerDay);
                ulong delay = minutes * AirConst.TicksPerMinute;
                foreach (string name in new[] { "X1", "X2", "X3", "X4" })
                {
                    Rec onStand = rig.Rec.Milestone(rig.Id(name), FlightMilestone.OnStand);
                    Rec open = rig.Rec.Milestone(rig.Id(name), FlightMilestone.DoorsOpen);
                    Assert.Equal(onStand.Milestone.ActualTick + delay, open.Milestone.ActualTick);
                    Assert.Equal(onStand.Milestone.PlannedTick + delay, open.Milestone.PlannedTick);
                }
            }
        }
    }
}
