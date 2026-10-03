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
        // 12 §12.3 planned OnStand = STA + OccupancyTicks + RouteTicks, unimpeded here:
        // X1 (a320) on S1 from 3600 + 10 + 50 (E1 30, E2 20) = 3660;
        // X2 (a359, so not S1) on S2 from 3700 + 10 + 55 (E1 30, E3 15, E4 10) = 3765.
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
            // sim.airside's events only (12 §12.10 "no milestone re-fires").
            int eventsBefore = rig.Rec.All.FindAll(r => r.FromAirside && r.Flight == x1).Count;
            Assert.Equal(5, eventsBefore);

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
            Assert.Equal(eventsBefore, rig.Rec.All.FindAll(r => r.FromAirside && r.Flight == x1).Count);
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
            AssertNoOpLogged(log, linesBefore, 4001UL, x1, FixtureLayout.S2, 2L);
        }

        /// <summary>
        /// 12 §12.10 (Q-056): exactly one line since <paramref name="from"/>,
        /// Write(tick, Info, SystemId(3), LogKey.AirsideReassignStandNoOp,
        /// new LogArgs(flight, newStand, reason)), at the applying tick.
        /// </summary>
        private static void AssertNoOpLogged(CapturingLog log, int from, ulong tick, ulong flight, ushort stand, long reason)
        {
            var lines = log.Entries.GetRange(from, log.Entries.Count - from).FindAll(e => e.Key == LogKey.AirsideReassignStandNoOp);
            Assert.True(lines.Count == 1, "expected one AirsideReassignStandNoOp line, got " + lines.Count.ToString(CultureInfo.InvariantCulture) + ":\n" + string.Join("\n", log.Lines));
            var e = lines[0];
            Assert.Equal(1, (int)LogKey.AirsideReassignStandNoOp);
            Assert.Equal(tick, e.Tick);
            Assert.Equal(LogLevel.Info, e.Level);
            Assert.Equal(AirConst.AirsideSystemId, e.System);
            Assert.Equal(3, e.Args.Count);
            Assert.Equal(unchecked((long)flight), e.Args.A0);
            Assert.Equal((long)stand, e.Args.A1);
            Assert.Equal(reason, e.Args.A2);
        }

        [Fact]
        public void test_reassign_stand_no_op_logs_key_and_reason()
        {
            // Reason 1: unknown flight; flight not OnStand; and an arrival
            // whose stand was handed to its departure, which is no longer tracked.
            var log = new CapturingLog();
            HostRig rig = Rig(log);
            ulong x1 = rig.Id("X1");
            ulong x2 = rig.Id("X2");
            rig.RunTo(3000UL);
            int mark = log.Entries.Count;
            rig.Submit(Payload.ReassignCommand(3001UL, 999UL, FixtureLayout.S4), out _);
            rig.RunTo(3002UL);
            AssertNoOpLogged(log, mark, 3001UL, 999UL, FixtureLayout.S4, 1L);

            mark = log.Entries.Count;
            rig.Submit(Payload.ReassignCommand(3003UL, x1, FixtureLayout.S1), out _);
            rig.RunTo(3004UL);
            AssertNoOpLogged(log, mark, 3003UL, x1, FixtureLayout.S1, 1L);

            // Reason 2: newStand occupied by another flight, or by the flight itself.
            rig.RunTo(4000UL);
            mark = log.Entries.Count;
            rig.Submit(Payload.ReassignCommand(4001UL, x1, FixtureLayout.S2), out _);
            rig.RunTo(4002UL);
            AssertNoOpLogged(log, mark, 4001UL, x1, FixtureLayout.S2, 2L);
            mark = log.Entries.Count;
            rig.Submit(Payload.ReassignCommand(4003UL, x2, FixtureLayout.S2), out _);
            rig.RunTo(4004UL);
            AssertNoOpLogged(log, mark, 4003UL, x2, FixtureLayout.S2, 2L);

            // Reason 3: free but incompatible (heavy X2 to medium-only S1, here free).
            var log3 = new CapturingLog();
            var only = new HostRig(Csv.Of(Csv.Row("X2", "A", "06:10", aircraft: "a359")), log: log3);
            only.RunTo(4000UL);
            only.Submit(Payload.ReassignCommand(4001UL, only.Id("X2"), FixtureLayout.S1), out _);
            only.RunTo(4002UL);
            AssertNoOpLogged(log3, 0, 4001UL, only.Id("X2"), FixtureLayout.S1, 3L);

            // Reason 1, handed-off arrival: a boarding hold keeps R_D on S1 after
            // the handoff at DoorsOpen + 35 min. R_A's track was removed at the
            // handoff (12 §12.8 "The handed-off arrival", Q-062), so check 1
            // fails as "not tracked".
            var logH = new CapturingLog();
            var handed = new HostRig(Csv.Of(Csv.Pair("R_A", "R_D", "06:30", "08:00")), flow: ScriptedFlow.For(2UL, 200UL, 3, 77U), log: logH);
            ulong ra = handed.Id("R_A");
            ulong rd = handed.Id("R_D");
            Assert.Equal(2UL, rd);
            handed.RunTo(4400UL);
            ulong handoff = handed.Rec.Milestone(rd, FlightMilestone.OnStand).Tick;
            Assert.True(handoff < 4399UL);
            Assert.Equal(rd, handed.Occupant(FixtureLayout.S1)!.Value.Value);
            Assert.False(handed.Airside.TryGetTrack(new FlightId(ra), out _));
            handed.Submit(Payload.ReassignCommand(4401UL, ra, FixtureLayout.S4), out _);
            handed.RunTo(4402UL);
            AssertNoOpLogged(logH, 0, 4401UL, ra, FixtureLayout.S4, 1L);
            Assert.Equal(rd, handed.Occupant(FixtureLayout.S1)!.Value.Value);
            Assert.False(handed.Occupant(FixtureLayout.S4).HasValue);
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
