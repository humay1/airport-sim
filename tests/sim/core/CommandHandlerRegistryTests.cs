using System;
using Xunit;
using static AirportSim.Sim.Core.Tests.CommandTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-005. Handler registration through SystemServices.Commands against
    /// 08-interfaces-core.md §8.7 "Queue semantics" (Q-020) and "Dispatch" (Q-010):
    /// each kind's owner is fixed by the payload table (SetServersOpen → 4,
    /// ReassignStand → 3), sim.core owns NoOp, duplicates and late registrations
    /// throw, and Build refuses a handler whose owner never registered as a system.
    /// These throws happen outside a tick and are not wrapped (§8.5a).
    /// </summary>
    public sealed class CommandHandlerRegistryTests
    {
        private static ScriptedHandler Handler(CommandKind kind)
        {
            int length = kind == CommandKind.SetServersOpen ? FlowPayloadLength
                : kind == CommandKind.ReassignStand ? StandPayloadLength : 0;
            return new ScriptedHandler(kind, length, null, null);
        }

        private static ICommandHandlerRegistry Registry(out ISimHostBuilder builder)
        {
            builder = Builder(1UL, new RecordingLog(), new CheckpointLog());
            return builder.Services.Commands;
        }

        [Fact]
        public void test_command_handler_registry_table_owners_are_accepted()
        {
            ICommandHandlerRegistry registry = Registry(out ISimHostBuilder builder);
            registry.Register(new SystemId(AirsidePosition), Handler(CommandKind.ReassignStand));
            registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen));
            builder.Register(new OwnerSystem(AirsidePosition, null, null));
            builder.Register(new OwnerSystem(FlowPosition, null, null));
            ISimHost host = builder.Build();
            Admit(host, Flow(1, 1, 1));
            Admit(host, Stand(1, 1, 1));
        }

        [Theory]
        [InlineData(CommandKind.SetServersOpen, AirsidePosition)]
        [InlineData(CommandKind.SetServersOpen, (ushort)1)]
        [InlineData(CommandKind.SetServersOpen, (ushort)0)]
        [InlineData(CommandKind.SetServersOpen, (ushort)5)]
        [InlineData(CommandKind.ReassignStand, FlowPosition)]
        [InlineData(CommandKind.ReassignStand, (ushort)2)]
        [InlineData(CommandKind.ReassignStand, (ushort)14)]
        public void test_command_handler_registry_owner_not_matching_kind_throws_argument_exception(CommandKind kind, ushort owner)
        {
            ICommandHandlerRegistry registry = Registry(out _);
            Assert.Throws<ArgumentException>(() => registry.Register(new SystemId(owner), Handler(kind)));
        }

        [Theory]
        [InlineData((ushort)0)]
        [InlineData(AirsidePosition)]
        [InlineData(FlowPosition)]
        public void test_command_handler_registry_noop_handler_throws_argument_exception(ushort owner)
        {
            ICommandHandlerRegistry registry = Registry(out _);
            Assert.Throws<ArgumentException>(() => registry.Register(new SystemId(owner), Handler(CommandKind.NoOp)));
        }

        [Fact]
        public void test_command_handler_registry_second_handler_of_a_kind_throws_argument_exception()
        {
            ICommandHandlerRegistry registry = Registry(out _);
            registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen));
            Assert.Throws<ArgumentException>(() => registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen)));
        }

        [Fact]
        public void test_command_handler_registry_same_handler_twice_throws_argument_exception()
        {
            ICommandHandlerRegistry registry = Registry(out _);
            ScriptedHandler h = Handler(CommandKind.ReassignStand);
            registry.Register(new SystemId(AirsidePosition), h);
            Assert.Throws<ArgumentException>(() => registry.Register(new SystemId(AirsidePosition), h));
        }

        [Fact]
        public void test_command_handler_registry_null_handler_throws_argument_null_exception()
        {
            ICommandHandlerRegistry registry = Registry(out _);
            Assert.Throws<ArgumentNullException>(() => registry.Register(new SystemId(FlowPosition), null!));
        }

        [Fact]
        public void test_command_handler_registry_register_after_build_throws_invalid_operation()
        {
            ICommandHandlerRegistry registry = Registry(out ISimHostBuilder builder);
            builder.Register(new OwnerSystem(FlowPosition, null, null));
            builder.Build();
            Assert.Throws<InvalidOperationException>(() => registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen)));
        }

        [Fact]
        public void test_command_handler_registry_rejected_registration_leaves_kind_unknown()
        {
            ICommandHandlerRegistry registry = Registry(out ISimHostBuilder builder);
            Assert.Throws<ArgumentException>(() => registry.Register(new SystemId(AirsidePosition), Handler(CommandKind.SetServersOpen)));
            builder.Register(new OwnerSystem(AirsidePosition, null, null));
            builder.Register(new OwnerSystem(FlowPosition, null, null));
            ISimHost host = builder.Build();
            Assert.Equal(CommandRejection.UnknownKind, Reject(host, Flow(1, 1, 1)));
        }

        [Fact]
        public void test_command_handler_registry_unregistered_owner_makes_build_throw_invalid_operation()
        {
            ICommandHandlerRegistry registry = Registry(out ISimHostBuilder builder);
            registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen));
            builder.Register(new OwnerSystem(AirsidePosition, null, null));
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Fact]
        public void test_command_handler_registry_one_of_two_owners_missing_makes_build_throw()
        {
            ICommandHandlerRegistry registry = Registry(out ISimHostBuilder builder);
            registry.Register(new SystemId(AirsidePosition), Handler(CommandKind.ReassignStand));
            registry.Register(new SystemId(FlowPosition), Handler(CommandKind.SetServersOpen));
            builder.Register(new OwnerSystem(FlowPosition, null, null));
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }

        [Fact]
        public void test_command_handler_registry_noop_needs_no_handler()
        {
            // sim.core handles NoOp itself, so a build with no handlers admits it.
            ISimHostBuilder builder = Builder(1UL, new RecordingLog(), new CheckpointLog());
            ISimHost host = builder.Build();
            Admit(host, NoOp(1));
            Assert.Equal(CommandRejection.UnknownKind, Reject(host, Flow(1, 1, 1)));
            Assert.Equal(CommandRejection.UnknownKind, Reject(host, Stand(1, 1, 1)));
        }
    }
}
