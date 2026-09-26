using System;
using Xunit;
using static AirportSim.Sim.Core.Tests.CommandTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-005. The Command value, its enums and PLAYER_LOCAL against
    /// 08-interfaces-core.md §8.7 "Queue semantics" (Q-020), "Issuer, kinds and
    /// payloads" (Q-010, Q-023) and 07 L10.
    /// </summary>
    public sealed class CommandTests
    {
        [Fact]
        public void test_command_constructor_sets_fields_and_zero_sequence()
        {
            byte[] payload = { 1, 2, 3, 4, 5, 6, 7, 8 };
            var cmd = new Command(42UL, new PlayerId(7), CommandKind.SetServersOpen, payload);
            Assert.Equal(42UL, cmd.Tick);
            Assert.Equal((ushort)7, cmd.Issuer.Value);
            Assert.Equal(CommandKind.SetServersOpen, cmd.Kind);
            Assert.Equal(payload, cmd.Payload);
            Assert.Equal(0u, cmd.Sequence);
        }

        [Fact]
        public void test_command_constructor_null_payload_throws_argument_null_exception()
        {
            Assert.Throws<ArgumentNullException>(() => new Command(1UL, Local, CommandKind.NoOp, null!));
        }

        [Fact]
        public void test_command_constructor_empty_payload_is_accepted()
        {
            var cmd = new Command(1UL, Local, CommandKind.NoOp, Array.Empty<byte>());
            Assert.Empty(cmd.Payload);
            Assert.Equal(0u, cmd.Sequence);
        }

        [Fact]
        public void test_command_kind_values_are_pinned_uint16()
        {
            // Values are saved in command logs and never renumbered.
            Assert.Equal(typeof(ushort), Enum.GetUnderlyingType(typeof(CommandKind)));
            Assert.Equal((ushort)0, (ushort)CommandKind.NoOp);
            Assert.Equal((ushort)1, (ushort)CommandKind.SetServersOpen);
            Assert.Equal((ushort)2, (ushort)CommandKind.ReassignStand);
            Assert.Equal(3, Enum.GetValues(typeof(CommandKind)).Length);
        }

        [Fact]
        public void test_command_rejection_values_follow_declared_order()
        {
            // 07 L10: an enum without an IDL underlying type is int, numbered from 0.
            Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(CommandRejection)));
            Assert.Equal(0, (int)CommandRejection.None);
            Assert.Equal(1, (int)CommandRejection.TooLate);
            Assert.Equal(2, (int)CommandRejection.UnknownKind);
            Assert.Equal(3, (int)CommandRejection.MalformedPayload);
            Assert.Equal(4, (int)CommandRejection.NotPermitted);
            Assert.Equal(5, Enum.GetValues(typeof(CommandRejection)).Length);
        }

        [Fact]
        public void test_command_player_local_is_player_zero_in_sim_constants()
        {
            // Q-023: SimConstants.PLAYER_LOCAL, a static readonly PlayerId(0).
            Assert.Equal((ushort)0, SimConstants.PLAYER_LOCAL.Value);
            Assert.True(SimConstants.PLAYER_LOCAL == new PlayerId(0));
            Assert.True(SimConstants.PLAYER_LOCAL != new PlayerId(1));
            Assert.True(SimConstants.PLAYER_LOCAL.Equals(new PlayerId(0)));
        }
    }
}
