using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Turnaround.Tests
{
    /// <summary>
    /// 12 §12.8 "With sim.turnaround registered", end to end with both modules
    /// registered (13 §13.11, Q-087): the arrival's OnStand starts its jobs,
    /// DeboardComplete hands the stand to the departure, the departure's
    /// OnStand starts its jobs, and BoardingComplete closes the doors.
    /// </summary>
    public sealed class AirsideTests
    {
        [Fact]
        public void test_airside_doors_close_after_boarding_complete_with_turnaround_registered()
        {
            // One rotation on the 13 §13.11 setup, nothing else contending:
            // Deboard 60, ReadyToBoard 120 and BoardingComplete 220 ticks after
            // the departure's OnStand. MinTurnaround 35 min is far longer than
            // any of that, so the fallback (doors close at DoorsOpen +
            // MinTurnaround) would be visibly later.
            var rig = new AirsideRig(
                Csv.Of(Csv.Row("A1", "A", "06:00", "D1"), Csv.Row("D1", "D", "08:00", "A1")),
                Phase1Fixture.Setup());
            rig.RunTo(7000UL);
            ulong a1 = rig.Id("A1");
            ulong d1 = rig.Id("D1");

            Rec arrivalOnStand = rig.AirsideMilestone(a1, FlightMilestone.OnStand);
            Rec doorsOpen = rig.AirsideMilestone(a1, FlightMilestone.DoorsOpen);
            Assert.Equal(arrivalOnStand.Tick + AirsideRig.DoorDelayTicks, doorsOpen.Tick);

            // 13 §13.6: Deboard starts at the arrival's OnStand; DeboardComplete
            // is for the arrival's FlightId.
            Rec deboard = Assert.Single(rig.Rec.Milestones(FlightMilestone.DeboardComplete));
            Assert.Equal(a1, deboard.Flight);
            Assert.Equal(arrivalOnStand.Tick + 60UL, deboard.Tick);

            // 12 §12.8 step 3: the handoff is due at the first Tick after the
            // recording, DoorsOpen having fired; its Cause is the DeboardComplete.
            Rec departureOnStand = rig.AirsideMilestone(d1, FlightMilestone.OnStand);
            Assert.Equal(deboard.Tick + 1UL, departureOnStand.Tick);
            Assert.True(departureOnStand.Env.Cause.HasValue);
            Assert.Equal(deboard.Id, departureOnStand.Env.Cause.Id);
            Assert.False(rig.Airside.TryGetTrack(new FlightId(a1), out _), "the arrival is still tracked after the handoff");

            // 13 §13.6: the departure's jobs start at its OnStand.
            Assert.Equal(departureOnStand.Tick, rig.Job("D1", JobKind.Fuel).CreatedAt);
            Rec ready = Assert.Single(rig.Rec.Milestones(FlightMilestone.ReadyToBoard));
            Assert.Equal(d1, ready.Flight);
            Assert.Equal(departureOnStand.Tick + Phase1Fixture.UnimpededReadyToBoardTicks, ready.Tick);
            Rec boarded = Assert.Single(rig.Rec.Milestones(FlightMilestone.BoardingComplete));
            Assert.Equal(d1, boarded.Flight);
            Assert.Equal(departureOnStand.Tick + Phase1Fixture.UnimpededBoardingCompleteTicks, boarded.Tick);

            // 12 §12.8 step 5: doors close, and push back, on the next Tick,
            // caused by the recorded BoardingComplete (12 §12.11).
            Rec doorsClosed = rig.AirsideMilestone(d1, FlightMilestone.DoorsClosed);
            Assert.Equal(boarded.Tick + 1UL, doorsClosed.Tick);
            Assert.True(doorsClosed.Env.Cause.HasValue);
            Assert.Equal(boarded.Id, doorsClosed.Env.Cause.Id);
            Rec pushback = rig.AirsideMilestone(d1, FlightMilestone.Pushback);
            Assert.Equal(doorsClosed.Tick, pushback.Tick);
            Assert.True(pushback.Id.CompareTo(doorsClosed.Id) > 0, "Pushback before DoorsClosed");

            foreach (JobKind k in TConst.DepartureJobs)
            {
                Assert.Equal(JobStatus.Completed, rig.Job("D1", k).Status);
            }

            Assert.Equal(JobStatus.Completed, rig.Job("A1", JobKind.BaggageUnload).Status);
        }
    }
}
