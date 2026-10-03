using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.8 step 3 and §12.8a S4 (Q-062), with turnaroundRegistered true
    /// and a probe at position 5. A DeboardComplete that comes before the
    /// arrival's DoorsOpen is recorded in the arrival's RecordedCause, which
    /// is hashed (§12.12 item 4). The handoff never runs before DoorsOpen: it
    /// chains right after it, in the same tick and turn, with Cause = the
    /// recorded DeboardComplete.
    /// </summary>
    public sealed class HandoffTests
    {
        [Fact]
        public void test_handoff_waits_for_arrival_doors_open_when_deboard_completes_first()
        {
            // R_A: lands 3900, OnStand 3960 (route 50), DoorsOpen 3980 (2 min).
            ulong onStand = AirConst.At(6, 30) + FixtureLayout.OccupancyTicks + FixtureLayout.RouteTicks(FixtureLayout.S1);
            ulong doorsOpen = onStand + AirConst.FixtureDoorDelayTicks;
            byte[] csv = Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00"));

            var probe = new TurnaroundProbe();
            var rig = new HostRig(csv, turnaroundRegistered: true, turnaround: probe);
            var control = new HostRig(csv, turnaroundRegistered: true, turnaround: new TurnaroundProbe());
            ulong ra = rig.Id("R_A");
            ulong rd = rig.Id("R_D");
            probe.Script.Add((onStand, ra, FlightMilestone.DeboardComplete));

            var recorded = new Dictionary<ulong, EventRef>();
            var controlRecorded = new Dictionary<ulong, EventRef>();
            var hashDiffers = new Dictionary<ulong, bool>();
            while (rig.Host.CurrentTick < doorsOpen + 10UL)
            {
                ulong t = rig.Host.CurrentTick;
                rig.Host.Step(1);
                control.Host.Step(1);
                if (rig.Airside.TryGetTrack(new FlightId(ra), out AircraftTrack tr))
                {
                    recorded[t] = tr.RecordedCause;
                }

                if (control.Airside.TryGetTrack(new FlightId(ra), out AircraftTrack ct))
                {
                    controlRecorded[t] = ct.RecordedCause;
                }

                hashDiffers[t] = rig.Airside.ComputeStateHash() != control.Airside.ComputeStateHash();
            }

            Assert.Equal(onStand, rig.Rec.Milestone(ra, FlightMilestone.OnStand).Tick);
            Rec open = rig.Rec.Milestone(ra, FlightMilestone.DoorsOpen);
            Assert.Equal(doorsOpen, open.Tick);
            EventId deboard = probe.Published.Find(p => p.Flight == ra).Id;
            Assert.Equal(onStand, deboard.Tick);

            // Before the event, the two runs agree.
            Assert.False(hashDiffers[onStand - 1UL]);
            Assert.False(recorded[onStand - 1UL].HasValue);

            // From the recording (phase 3 of onStand) to the handoff, the record names it.
            for (ulong t = onStand; t < doorsOpen; t++)
            {
                string at = "t=" + t.ToString(CultureInfo.InvariantCulture);
                Assert.True(recorded.ContainsKey(t), at + ": arrival untracked while waiting for DoorsOpen");
                Assert.True(recorded[t].HasValue, at + ": RecordedCause unset while waiting");
                Assert.Equal(deboard, recorded[t].Id);
                Assert.True(hashDiffers[t], at + ": the recorded event is not in the hash");
                Assert.False(controlRecorded[t].HasValue);
            }

            // The handoff chains right after DoorsOpen, same tick, Cause = the DeboardComplete.
            Rec depOnStand = rig.Rec.Milestone(rd, FlightMilestone.OnStand);
            Assert.Equal(doorsOpen, depOnStand.Tick);
            Assert.True(open.Id.Sequence + 1U == depOnStand.Id.Sequence, "departure OnStand not right after DoorsOpen: " + open + " / " + depOnStand);
            Assert.True(depOnStand.Env.Cause.HasValue);
            Assert.Equal(deboard, depOnStand.Env.Cause.Id);
            Assert.False(recorded.ContainsKey(doorsOpen), "arrival still tracked after the handoff");
            Assert.Equal(rd, rig.Occupant(FixtureLayout.S1)!.Value.Value);

            // Without the event, nothing is recorded and no handoff happens.
            Assert.Empty(control.Rec.Milestones(rd));
        }
    }
}
