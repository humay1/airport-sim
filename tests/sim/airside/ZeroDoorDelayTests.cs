using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8a "Chains" (Q-054): an action that makes another due at the
    /// current tick runs it at once, in the same turn. With
    /// DoorsOpenDelayMinutes = 0 and MinTurnaround = 0, the arrival's
    /// OnStand (S6) chains DoorsOpen, the fallback handoff (departure
    /// OnStand) and the departure's doors-close point (DoorsClosed then
    /// Pushback), all in one tick, inside the arrival's turn.
    /// </summary>
    public sealed class ZeroDoorDelayTests
    {
        [Fact]
        public void test_zero_door_delay_and_turnaround_chain_in_one_tick()
        {
            var rig = new HostRig(Csv.Of(Csv.Pair("A1", "D1", "06:00", "08:00", arrMinTurn: "0", depMinTurn: "0")), doorDelayMinutes: 0U);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");
            rig.RunTo(4000UL);

            ulong onStand = AirConst.At(6, 0) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(FixtureLayout.S1);
            var chain = new List<Rec>
            {
                rig.Rec.Milestone(a1, FlightMilestone.OnStand),
                rig.Rec.Milestone(a1, FlightMilestone.DoorsOpen),
                rig.Rec.Milestone(d1, FlightMilestone.OnStand),
                rig.Rec.Milestone(d1, FlightMilestone.DoorsClosed),
                rig.Rec.Milestone(d1, FlightMilestone.Pushback),
            };
            for (int i = 0; i < chain.Count; i++)
            {
                Assert.Equal(onStand, chain[i].Tick);
                Assert.Equal(onStand, chain[i].Milestone.ActualTick);
                if (i > 0)
                {
                    // Consecutive EventIds: nothing else runs between the links.
                    Assert.True(chain[i - 1].Id.Sequence + 1U == chain[i].Id.Sequence, "chain broken between " + chain[i - 1] + " and " + chain[i]);
                }
            }

            Assert.Equal(onStand, chain[1].Milestone.PlannedTick);
            Assert.Equal(AirConst.At(8, 0), chain[2].Milestone.PlannedTick);
            Assert.Equal(AirConst.At(8, 0), chain[3].Milestone.PlannedTick);

            // 12 §12.11 (Q-078): the arrival's OnStand ends its edges, so None;
            // each later link names the one just emitted: DoorsOpen the
            // OnStand (delay 0), the fallback handoff the DoorsOpen
            // (MinTurnaround 0), DoorsClosed the departure's OnStand (no
            // flow, so no hold), Pushback the DoorsClosed.
            Assert.False(chain[0].Env.Cause.HasValue);
            for (int i = 1; i < chain.Count; i++)
            {
                Assert.True(chain[i].Env.Cause.HasValue, "no Cause on " + chain[i]);
                Assert.Equal(chain[i - 1].Id, chain[i].Env.Cause.Id);
            }
        }
    }
}
