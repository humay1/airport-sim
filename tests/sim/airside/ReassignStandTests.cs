using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Airside.Tests
{
    /// <summary>
    /// 12 §12.10 and 08 §8.7 (Q-010): ReassignStand's Validate checks the
    /// payload only (length 10, known StandId, else MalformedPayload).
    /// Whether the flight is OnStand and whether the target is free and
    /// compatible are checked at Apply; failing them is a deterministic,
    /// logged no-op (Info, sim.airside, with the tick). On success, at the
    /// next tick boundary, the old stand clears, the new one is set, and no
    /// milestone re-fires.
    /// </summary>
    public sealed class ReassignStandTests
    {
        // X1 (a320) is on S1 from 3660; X2 (a359) on S2 from 3765.
        private static HostRig Rig(ISimLog? log = null)
        {
            return new HostRig(Csv.Of(Csv.Row("X1", "A", "06:00"), Csv.Row("X2", "A", "06:10", aircraft: "a359")), log: log);
        }

        private static void AssertRejected(HostRig rig, byte[] payload)
        {
            var cmd = new Command(100UL, SimConstants.PLAYER_LOCAL, CommandKind.ReassignStand, payload);
            Assert.False(rig.Submit(cmd, out CommandRejection reason), "admitted a payload of " + payload.Length.ToString(CultureInfo.InvariantCulture) + " bytes");
            Assert.Equal(CommandRejection.MalformedPayload, reason);
        }

        [Fact]
        public void test_reassign_stand_validate_rejects_malformed_payload_and_unknown_stand()
        {
            HostRig rig = Rig();
            byte[] valid = Payload.Reassign(rig.Id("X1"), FixtureLayout.S4);
            AssertRejected(rig, Array.Empty<byte>());
            AssertRejected(rig, valid.AsSpan(0, 9).ToArray());
            var eleven = new byte[11];
            valid.CopyTo(eleven, 0);
            AssertRejected(rig, eleven);
            AssertRejected(rig, Payload.Reassign(rig.Id("X1"), 99));
            AssertRejected(rig, Payload.Reassign(rig.Id("X1"), 0));

            // Runtime facts are not admission's business: an unknown flight is admitted.
            Assert.True(rig.Submit(Payload.ReassignCommand(100UL, rig.Id("X1"), FixtureLayout.S4), out CommandRejection ok));
            Assert.Equal(CommandRejection.None, ok);
            Assert.True(rig.Submit(Payload.ReassignCommand(100UL, 999UL, FixtureLayout.S4), out CommandRejection unknownFlight));
            Assert.Equal(CommandRejection.None, unknownFlight);
        }

        [Fact]
        public void test_reassign_stand_moves_on_stand_flight_to_free_compatible_stand()
        {
            HostRig rig = Rig();
            ulong x1 = rig.Id("X1");
            rig.RunTo(4000UL);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            int eventsBefore = rig.Rec.All.FindAll(r => r.Flight == x1).Count;

            Assert.True(rig.Submit(Payload.ReassignCommand(4001UL, x1, FixtureLayout.S4), out CommandRejection reason));
            Assert.Equal(CommandRejection.None, reason);
            rig.RunTo(4001UL);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            Assert.False(rig.Occupant(FixtureLayout.S4).HasValue);

            rig.RunTo(4003UL);
            Assert.False(rig.Occupant(FixtureLayout.S1).HasValue);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S4)!.Value.Value);
            Assert.Contains(FixtureLayout.S1, rig.Free());
            Assert.DoesNotContain(FixtureLayout.S4, rig.Free());

            rig.RunTo(AirConst.TicksPerDay);
            Assert.Equal(eventsBefore, rig.Rec.All.FindAll(r => r.Flight == x1).Count);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S4)!.Value.Value);
        }

        [Fact]
        public void test_reassign_stand_to_occupied_stand_is_logged_no_op()
        {
            var log = new CapturingLog();
            HostRig rig = Rig(log);
            ulong x1 = rig.Id("X1");
            ulong x2 = rig.Id("X2");
            rig.RunTo(4000UL);
            Assert.Equal(x2, rig.Occupant(FixtureLayout.S2)!.Value.Value);
            int linesBefore = log.Lines.Count;

            Assert.True(rig.Submit(Payload.ReassignCommand(4001UL, x1, FixtureLayout.S2), out CommandRejection reason));
            Assert.Equal(CommandRejection.None, reason);
            rig.RunTo(4010UL);

            Assert.Equal(x1, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            Assert.Equal(x2, rig.Occupant(FixtureLayout.S2)!.Value.Value);
            var added = log.Lines.GetRange(linesBefore, log.Lines.Count - linesBefore);
            Assert.Contains(added, l => l.StartsWith("t=4001 Info s=3 ", StringComparison.Ordinal));
        }

        [Fact]
        public void test_reassign_stand_to_incompatible_stand_is_no_op()
        {
            // X2 is heavy; S1 takes medium at most, and is free here.
            var rig = new HostRig(Csv.Of(Csv.Row("X2", "A", "06:10", aircraft: "a359")));
            ulong x2 = rig.Id("X2");
            rig.RunTo(4000UL);
            Assert.Equal(x2, rig.Occupant(FixtureLayout.S2)!.Value.Value);

            Assert.True(rig.Submit(Payload.ReassignCommand(4001UL, x2, FixtureLayout.S1), out _));
            rig.RunTo(4010UL);
            Assert.Equal(x2, rig.Occupant(FixtureLayout.S2)!.Value.Value);
            Assert.False(rig.Occupant(FixtureLayout.S1).HasValue);
            Assert.Equal(new List<ushort> { 1, 3, 4 }, rig.Free());
        }

        [Fact]
        public void test_reassign_stand_for_flight_not_on_stand_is_no_op()
        {
            HostRig rig = Rig();
            ulong x1 = rig.Id("X1");
            rig.RunTo(3000UL);
            Assert.Equal(AircraftLegPhase.AwaitingApproach, rig.Track(x1).Phase);

            Assert.True(rig.Submit(Payload.ReassignCommand(3001UL, x1, FixtureLayout.S4), out _));
            Assert.True(rig.Submit(Payload.ReassignCommand(3001UL, 999UL, FixtureLayout.S3), out _));
            rig.RunTo(3010UL);
            Assert.Equal(new List<ushort> { 1, 2, 3, 4 }, rig.Free());

            // X1 later takes the stand the normal rule gives it.
            rig.RunTo(4000UL);
            Assert.Equal(x1, rig.Occupant(FixtureLayout.S1)!.Value.Value);
            Assert.False(rig.Occupant(FixtureLayout.S4).HasValue);
            Assert.False(rig.Occupant(FixtureLayout.S3).HasValue);
        }
    }
}
