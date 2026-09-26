using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using static AirportSim.Sim.Core.Tests.CommandTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-005. The command queue behind ISimHost against 08-interfaces-core.md §8.7
    /// (Q-010 dispatch, Q-020 "Queue semantics"), the core hash section of §8.9
    /// (Q-017) and tick numbering (Q-014 A1: Step(n) runs CurrentTick … CurrentTick+n−1).
    /// ICommandQueue is internal, so everything goes through ISimHost.
    /// </summary>
    public sealed class CommandQueueTests
    {
        private const uint TicksPerDay = (uint)SimConstants.TICKS_PER_SIM_DAY;

        // ------------------------------------------------------------ admission window

        [Fact]
        public void test_command_queue_tick_at_min_lead_is_admitted()
        {
            var rig = new Rig();
            Admit(rig.Host, NoOp(0UL + SimConstants.COMMAND_MIN_LEAD_TICKS));
            rig.Host.Step(10);
            Admit(rig.Host, Flow(10UL + SimConstants.COMMAND_MIN_LEAD_TICKS, 1, 1));
            Assert.Equal(2, rig.Host.CommandLogSince(0).Count);
        }

        [Fact]
        public void test_command_queue_tick_before_min_lead_is_rejected_too_late()
        {
            var rig = new Rig();
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, NoOp(0)));
            rig.Host.Step(10);
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, NoOp(10)));
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, NoOp(9)));
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, Flow(3, 1, 1)));
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, Stand(0, 1, 1)));
        }

        [Fact]
        public void test_command_queue_too_late_command_is_never_redated()
        {
            var rig = new Rig();
            rig.Host.Step(5);
            Reject(rig.Host, Flow(5, 1, 1));
            Admit(rig.Host, Flow(1_000_000, 2, 2));
            IReadOnlyList<Command> log = rig.Host.CommandLogSince(0);
            Assert.Single(log);
            Assert.Equal(1_000_000UL, log[0].Tick);
            rig.Host.Step(100);
            Assert.Empty(rig.FlowHandler!.AppliedCommands);
        }

        // ------------------------------------------------------------ admission order

        [Fact]
        public void test_command_queue_too_late_precedes_every_other_rejection()
        {
            var rig = new Rig(flowHandler: false);
            rig.Host.Step(3);
            var foreignUnknownMalformed = new Command(3, Foreign, CommandKind.SetServersOpen, new byte[] { 1 });
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, foreignUnknownMalformed));
            var foreignNoOpMalformed = new Command(2, Foreign, CommandKind.NoOp, new byte[] { 1 });
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, foreignNoOpMalformed));
            var badStand = new Command(1, Local, CommandKind.ReassignStand, new byte[] { 1 });
            Assert.Equal(CommandRejection.TooLate, Reject(rig.Host, badStand));
            Assert.Equal(0, rig.Airside!.ValidateCalls);
        }

        [Fact]
        public void test_command_queue_foreign_issuer_rejected_not_permitted_before_unknown_kind()
        {
            var rig = new Rig(flowHandler: false);
            var foreignUnknown = new Command(5, Foreign, CommandKind.SetServersOpen, FlowPayload(1, 1));
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignUnknown));
            var foreignUndefinedKind = new Command(5, new PlayerId(ushort.MaxValue), (CommandKind)77, Array.Empty<byte>());
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignUndefinedKind));
        }

        [Fact]
        public void test_command_queue_foreign_issuer_rejected_before_validate()
        {
            var rig = new Rig();
            var foreignValid = new Command(5, Foreign, CommandKind.SetServersOpen, FlowPayload(1, 1));
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignValid));
            var foreignMalformed = new Command(5, Foreign, CommandKind.ReassignStand, new byte[] { 1, 2 });
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignMalformed));
            var foreignNoOp = new Command(5, Foreign, CommandKind.NoOp, Array.Empty<byte>());
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignNoOp));
            var foreignNoOpMalformed = new Command(5, Foreign, CommandKind.NoOp, new byte[] { 0 });
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, foreignNoOpMalformed));
            Assert.Equal(0, rig.FlowHandler!.ValidateCalls);
            Assert.Equal(0, rig.Airside!.ValidateCalls);
        }

        [Fact]
        public void test_command_queue_unregistered_kind_rejected_unknown_kind_before_validate()
        {
            var rig = new Rig(flowHandler: false);
            Assert.Equal(CommandRejection.UnknownKind, Reject(rig.Host, Flow(5, 1, 1)));
            var malformedUnknown = new Command(5, Local, CommandKind.SetServersOpen, new byte[] { 1 });
            Assert.Equal(CommandRejection.UnknownKind, Reject(rig.Host, malformedUnknown));
            var undefinedKind = new Command(5, Local, (CommandKind)77, Array.Empty<byte>());
            Assert.Equal(CommandRejection.UnknownKind, Reject(rig.Host, undefinedKind));
            // The other kind's handler is never consulted.
            Assert.Equal(0, rig.Airside!.ValidateCalls);
        }

        [Fact]
        public void test_command_queue_validate_result_is_the_rejection_reason()
        {
            var rig = new Rig();
            var shortFlow = new Command(5, Local, CommandKind.SetServersOpen, new byte[FlowPayloadLength - 1]);
            Assert.Equal(CommandRejection.MalformedPayload, Reject(rig.Host, shortFlow));
            var longStand = new Command(5, Local, CommandKind.ReassignStand, new byte[StandPayloadLength + 1]);
            Assert.Equal(CommandRejection.MalformedPayload, Reject(rig.Host, longStand));
            byte[] forbidden = FlowPayload(1, 1);
            forbidden[0] = ForbiddenTarget;
            var forbiddenFlow = new Command(5, Local, CommandKind.SetServersOpen, forbidden);
            Assert.Equal(CommandRejection.NotPermitted, Reject(rig.Host, forbiddenFlow));
            Assert.Empty(rig.Host.CommandLogSince(0));
        }

        [Fact]
        public void test_command_queue_success_returns_true_with_reason_none()
        {
            var rig = new Rig();
            Command[] cmds = { NoOp(1), Flow(2, 7, 3), Stand(3, 99, 4) };
            foreach (Command c in cmds)
            {
                bool ok = rig.Host.TrySubmit(in c, out CommandRejection reason);
                Assert.True(ok);
                Assert.Equal(CommandRejection.None, reason);
            }
        }

        [Fact]
        public void test_command_queue_validate_runs_exactly_once_per_reaching_submit()
        {
            var rig = new Rig();
            Admit(rig.Host, Flow(5, 1, 1));
            Assert.Equal(1, rig.FlowHandler!.ValidateCalls);
            Reject(rig.Host, new Command(5, Local, CommandKind.SetServersOpen, new byte[3]));
            Assert.Equal(2, rig.FlowHandler.ValidateCalls);
            Admit(rig.Host, Stand(6, 1, 1));
            Assert.Equal(1, rig.Airside!.ValidateCalls);
            Assert.Equal(2, rig.FlowHandler.ValidateCalls);

            // Never at application.
            rig.Host.Step(20);
            Assert.Single(rig.FlowHandler.AppliedCommands);
            Assert.Single(rig.Airside.AppliedCommands);
            Assert.Equal(2, rig.FlowHandler.ValidateCalls);
            Assert.Equal(1, rig.Airside.ValidateCalls);
        }

        [Fact]
        public void test_command_queue_validate_receives_submitted_payload()
        {
            var rig = new Rig();
            byte[] payload = StandPayload(0x0102030405060708UL, 0xBEEF);
            Admit(rig.Host, new Command(5, Local, CommandKind.ReassignStand, payload));
            Assert.Equal(payload, rig.Airside!.LastValidated);
        }

        [Fact]
        public void test_command_queue_noop_with_empty_payload_is_admitted()
        {
            var rig = new Rig(airsideHandler: false, flowHandler: false);
            Admit(rig.Host, NoOp(1));
            Admit(rig.Host, NoOp(1));
            Assert.Equal(2, rig.Host.CommandLogSince(0).Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(8)]
        [InlineData(10)]
        public void test_command_queue_noop_with_payload_rejected_malformed_payload(int length)
        {
            var rig = new Rig();
            var cmd = new Command(5, Local, CommandKind.NoOp, new byte[length]);
            Assert.Equal(CommandRejection.MalformedPayload, Reject(rig.Host, cmd));
            Assert.Equal(0, rig.FlowHandler!.ValidateCalls);
            Assert.Equal(0, rig.Airside!.ValidateCalls);
        }

        // ------------------------------------------------------------ sequence

        [Fact]
        public void test_command_queue_sequence_starts_at_one_and_increments_by_one()
        {
            var rig = new Rig();
            Admit(rig.Host, NoOp(9));
            Admit(rig.Host, Flow(3, 1, 1));
            Admit(rig.Host, Stand(6, 1, 1));
            IReadOnlyList<Command> log = rig.Host.CommandLogSince(0);
            Assert.Equal(new uint[] { 2, 3, 1 }, log.Select(c => c.Sequence).ToArray());
            Assert.Equal(new ulong[] { 3, 6, 9 }, log.Select(c => c.Tick).ToArray());
        }

        [Fact]
        public void test_command_queue_rejected_submit_consumes_no_sequence()
        {
            var rig = new Rig(flowHandler: false);
            Admit(rig.Host, NoOp(5));
            Reject(rig.Host, NoOp(0));                                                        // TooLate
            Reject(rig.Host, new Command(5, Foreign, CommandKind.NoOp, Array.Empty<byte>()));  // NotPermitted
            Reject(rig.Host, Flow(5, 1, 1));                                                  // UnknownKind
            Reject(rig.Host, new Command(5, Local, CommandKind.NoOp, new byte[] { 1 }));       // MalformedPayload
            Reject(rig.Host, new Command(5, Local, CommandKind.ReassignStand, new byte[2]));   // Validate
            Admit(rig.Host, Stand(5, 1, 1));
            Assert.Equal(new uint[] { 1, 2 }, rig.Host.CommandLogSince(0).Select(c => c.Sequence).ToArray());
        }

        [Fact]
        public void test_command_queue_sequence_is_one_counter_for_the_session()
        {
            var rig = new Rig();
            Admit(rig.Host, NoOp(1));
            Admit(rig.Host, Flow(2, 1, 1));
            Admit(rig.Host, Stand(2, 1, 1));
            rig.Host.Step(50);
            Admit(rig.Host, Flow(60, 2, 2));
            rig.Host.Step(50);
            Admit(rig.Host, NoOp(200));
            Assert.Equal(new uint[] { 1, 2, 3, 4, 5 }, rig.Host.CommandLogSince(0).Select(c => c.Sequence).ToArray());
            Assert.Equal(new uint[] { 2, 4 }, rig.FlowHandler!.AppliedCommands.Select(a => a.Sequence).ToArray());
            Assert.Equal(new uint[] { 3 }, rig.Airside!.AppliedCommands.Select(a => a.Sequence).ToArray());
        }

        [Fact]
        public void test_command_queue_caller_command_keeps_sequence_zero()
        {
            // 0 means "not admitted": the caller's value is never assigned one.
            var rig = new Rig();
            Command cmd = Flow(4, 1, 1);
            Admit(rig.Host, cmd);
            Assert.Equal(0u, cmd.Sequence);
            Assert.Equal(1u, rig.Host.CommandLogSince(0)[0].Sequence);
        }

        // ------------------------------------------------------------ application

        [Fact]
        public void test_command_queue_command_applies_exactly_at_its_tick()
        {
            var rig = new Rig();
            Admit(rig.Host, Flow(5, 1, 1));
            rig.Host.Step(5);                        // ticks 0..4
            Assert.Empty(rig.FlowHandler!.AppliedCommands);
            rig.Host.Step(1);                        // tick 5
            Applied a = Assert.Single(rig.FlowHandler.AppliedCommands);
            Assert.Equal(5UL, a.Tick);
            Assert.Equal(5UL, a.ContextTick);
            rig.Host.Step(1000);
            Assert.Single(rig.FlowHandler.AppliedCommands);
        }

        [Fact]
        public void test_command_queue_apply_runs_in_phase_one_before_systems()
        {
            var rig = new Rig();
            Admit(rig.Host, Flow(3, 1, 1));
            Admit(rig.Host, Stand(3, 1, 1));
            rig.Host.Step(4);
            int applyFlow = rig.Trace.IndexOf("apply SetServersOpen 3");
            int applyStand = rig.Trace.IndexOf("apply ReassignStand 3");
            int tick3 = rig.Trace.IndexOf("tick 3 3");
            int tick4 = rig.Trace.IndexOf("tick 4 3");
            Assert.True(applyFlow >= 0 && applyStand >= 0);
            Assert.True(rig.Trace.IndexOf("tick 4 2") < Math.Min(applyFlow, applyStand), "applied before tick 3 began");
            Assert.True(Math.Max(applyFlow, applyStand) < tick3, "phase 1 must precede phase 2");
            Assert.True(tick3 < tick4);
        }

        [Fact]
        public void test_command_queue_due_commands_apply_in_tick_issuer_sequence_order()
        {
            // With one player the order is (Tick, Sequence), across kinds and owners:
            // the owner's registry position plays no part.
            var rig = new Rig();
            Admit(rig.Host, Flow(5, 51, 0));    // seq 1
            Admit(rig.Host, Flow(3, 31, 0));    // seq 2
            Admit(rig.Host, NoOp(5));           // seq 3
            Admit(rig.Host, Stand(3, 32, 0));   // seq 4
            Admit(rig.Host, Stand(4, 41, 0));   // seq 5
            Admit(rig.Host, Flow(3, 33, 0));    // seq 6
            rig.Host.Step(10);

            Assert.Equal(new uint[] { 2, 4, 6, 5, 1 }, rig.Order.Select(a => a.Sequence).ToArray());
            Assert.Equal(new ulong[] { 3, 3, 3, 4, 5 }, rig.Order.Select(a => a.ContextTick).ToArray());
            Assert.Equal(
                new[] { CommandKind.SetServersOpen, CommandKind.ReassignStand, CommandKind.SetServersOpen, CommandKind.ReassignStand, CommandKind.SetServersOpen },
                rig.Order.Select(a => a.Kind).ToArray());
        }

        [Fact]
        public void test_command_queue_apply_receives_admitted_command()
        {
            var rig = new Rig();
            Admit(rig.Host, NoOp(2));
            Admit(rig.Host, Stand(2, 0xAABBCCDDEEFF0011UL, 513));
            rig.Host.Step(3);
            Applied a = Assert.Single(rig.Airside!.AppliedCommands);
            Assert.Equal(2UL, a.Tick);
            Assert.Equal((ushort)0, a.Issuer);
            Assert.Equal(CommandKind.ReassignStand, a.Kind);
            Assert.Equal(2u, a.Sequence);
            Assert.Equal(StandPayload(0xAABBCCDDEEFF0011UL, 513), a.Payload);
        }

        [Fact]
        public void test_command_queue_admission_copies_payload()
        {
            var rig = new Rig();
            byte[] payload = FlowPayload(9, 3);
            byte[] original = (byte[])payload.Clone();
            Admit(rig.Host, new Command(5, Local, CommandKind.SetServersOpen, payload));
            for (int i = 0; i < payload.Length; i++) payload[i] = ForbiddenTarget;

            Assert.Equal(original, rig.Host.CommandLogSince(0)[0].Payload);
            rig.Host.Step(6);
            Assert.Equal(original, Assert.Single(rig.FlowHandler!.AppliedCommands).Payload);
        }

        [Fact]
        public void test_command_queue_log_payload_is_a_fresh_copy()
        {
            var rig = new Rig();
            Admit(rig.Host, Flow(5, 9, 3));
            byte[] first = rig.Host.CommandLogSince(0)[0].Payload;
            for (int i = 0; i < first.Length; i++) first[i] = 0;

            Assert.Equal(FlowPayload(9, 3), rig.Host.CommandLogSince(0)[0].Payload);
            rig.Host.Step(6);
            Assert.Equal(FlowPayload(9, 3), Assert.Single(rig.FlowHandler!.AppliedCommands).Payload);
        }

        [Fact]
        public void test_command_queue_apply_impossibility_logged_by_handler_at_info()
        {
            // §8.7: an Apply that finds the effect impossible logs through ISimLog at
            // Info with the tick and returns. The host must carry on as normal.
            var rig = new Rig();
            rig.FlowHandler!.OnApply = (c, x) =>
            {
                if (c.Payload[0] == 13)
                {
                    x.Log.Write(x.Tick, LogLevel.Info, new SystemId(FlowPosition), LogKey.None, new LogArgs(13L));
                }
            };
            Admit(rig.Host, Flow(4, 13, 1));
            Admit(rig.Host, Flow(6, 14, 1));
            rig.Host.Step(10);

            Assert.Equal(10UL, rig.Host.CurrentTick);
            Assert.Equal(2, rig.FlowHandler.AppliedCommands.Count);
            LogEntry entry = Assert.Single(rig.Log.Entries, e => e.Level == LogLevel.Info);
            Assert.Equal(4UL, entry.Tick);
            Assert.Equal(FlowPosition, entry.System.Value);
            Assert.Equal(13L, entry.Args.A0);
            Assert.DoesNotContain(rig.Log.Entries, e => e.Level >= LogLevel.Warning);
        }

        [Fact]
        public void test_command_queue_apply_exception_escapes_step_wrapped()
        {
            // Core does not catch: the exception leaves Step wrapped once (§8.5a).
            var rig = new Rig();
            rig.Airside!.OnApply = (c, x) => throw new ProbeFailure();
            Admit(rig.Host, Stand(7, 1, 1));
            var ex = Assert.Throws<SimInvariantException>(() => rig.Host.Step(20));
            Assert.Equal(7UL, ex.Tick);
            Assert.IsType<ProbeFailure>(ex.InnerException);
        }

        // ------------------------------------------------------------ TrySubmit during Step

        [Fact]
        public void test_command_queue_try_submit_from_handler_throws_invalid_operation()
        {
            var rig = new Rig();
            Type? caught = null;
            rig.FlowHandler!.OnApply = (c, x) =>
            {
                try
                {
                    rig.Host.TrySubmit(NoOp(x.Tick + 5), out CommandRejection _);
                }
                catch (Exception e)
                {
                    caught = e.GetType();
                }
            };
            Admit(rig.Host, Flow(3, 1, 1));
            rig.Host.Step(10);
            Assert.Equal(typeof(InvalidOperationException), caught);

            // The refused submit consumed nothing.
            Assert.Single(rig.Host.CommandLogSince(0));
            Admit(rig.Host, NoOp(20));
            Assert.Equal(new uint[] { 1, 2 }, rig.Host.CommandLogSince(0).Select(c => c.Sequence).ToArray());
        }

        [Fact]
        public void test_command_queue_try_submit_from_system_tick_throws_invalid_operation()
        {
            var rig = new Rig();
            var caught = new List<Type>();
            rig.AirsideSystem.OnTick = x =>
            {
                if (x.Tick == 2)
                {
                    try
                    {
                        rig.Host.TrySubmit(NoOp(x.Tick + 5), out CommandRejection _);
                    }
                    catch (Exception e)
                    {
                        caught.Add(e.GetType());
                    }
                }
            };
            rig.Host.Step(5);
            Assert.Equal(new[] { typeof(InvalidOperationException) }, caught);
            Assert.Empty(rig.Host.CommandLogSince(0));
            Admit(rig.Host, NoOp(6));
            Assert.Equal(1u, rig.Host.CommandLogSince(0)[0].Sequence);
        }

        // ------------------------------------------------------------ LogSince

        [Fact]
        public void test_command_queue_log_since_empty_session_returns_empty_list()
        {
            var rig = new Rig();
            Assert.Empty(rig.Host.CommandLogSince(0));
            rig.Host.Step(100);
            Assert.Empty(rig.Host.CommandLogSince(0));
        }

        [Fact]
        public void test_command_queue_log_since_holds_pending_and_applied_in_total_order()
        {
            var rig = new Rig();
            Admit(rig.Host, Flow(5, 1, 1));     // seq 1
            Admit(rig.Host, Stand(3, 2, 2));    // seq 2
            Admit(rig.Host, NoOp(5));           // seq 3
            Admit(rig.Host, Flow(3, 4, 4));     // seq 4
            Admit(rig.Host, Stand(4, 5, 5));    // seq 5

            var expectedTicks = new ulong[] { 3, 3, 4, 5, 5 };
            var expectedSequences = new uint[] { 2, 4, 5, 1, 3 };
            IReadOnlyList<Command> pending = rig.Host.CommandLogSince(0);
            Assert.Equal(expectedTicks, pending.Select(c => c.Tick).ToArray());
            Assert.Equal(expectedSequences, pending.Select(c => c.Sequence).ToArray());

            rig.Host.Step(4);                   // ticks 0..3: two applied, three pending
            IReadOnlyList<Command> mixed = rig.Host.CommandLogSince(0);
            Assert.Equal(expectedSequences, mixed.Select(c => c.Sequence).ToArray());

            rig.Host.Step(10);                  // all applied, all retained
            IReadOnlyList<Command> applied = rig.Host.CommandLogSince(0);
            Assert.Equal(expectedTicks, applied.Select(c => c.Tick).ToArray());
            Assert.Equal(expectedSequences, applied.Select(c => c.Sequence).ToArray());
            Assert.Equal(new[] { CommandKind.ReassignStand, CommandKind.SetServersOpen, CommandKind.ReassignStand, CommandKind.SetServersOpen, CommandKind.NoOp },
                applied.Select(c => c.Kind).ToArray());
            Assert.Equal(StandPayload(5, 5), applied[2].Payload);
        }

        [Fact]
        public void test_command_queue_log_since_is_inclusive_by_command_tick()
        {
            var rig = new Rig();
            Admit(rig.Host, NoOp(2));
            Admit(rig.Host, NoOp(3));
            Admit(rig.Host, Flow(4, 1, 1));
            Admit(rig.Host, NoOp(4));
            Assert.Equal(new ulong[] { 3, 4, 4 }, rig.Host.CommandLogSince(3).Select(c => c.Tick).ToArray());
            Assert.Equal(new ulong[] { 4, 4 }, rig.Host.CommandLogSince(4).Select(c => c.Tick).ToArray());
            Assert.Empty(rig.Host.CommandLogSince(5));
            Assert.Equal(4, rig.Host.CommandLogSince(0).Count);
            rig.Host.Step(10);
            Assert.Equal(new ulong[] { 3, 4, 4 }, rig.Host.CommandLogSince(3).Select(c => c.Tick).ToArray());
            Assert.Empty(rig.Host.CommandLogSince(ulong.MaxValue));
        }

        // ------------------------------------------------------------ hash participation

        [Fact]
        public void test_command_queue_noop_submission_changes_world_hash_via_sequence_counter()
        {
            // §8.7/§8.9: NoOp touches no system; only the sequence counter records it.
            var with = new Rig();
            var without = new Rig();
            Admit(with.Host, NoOp(5));
            with.Host.Step(10);
            without.Host.Step(10);

            Assert.Equal(without.SystemHashes(), with.SystemHashes());
            Assert.NotEqual(without.Host.WorldStateHash(), with.Host.WorldStateHash());
            Assert.Equal(WorldHashOracle(10, CoreHashOracle(2), with.SystemHashes()), with.Host.WorldStateHash());
            Assert.Equal(WorldHashOracle(10, CoreHashOracle(1), without.SystemHashes()), without.Host.WorldStateHash());
        }

        [Fact]
        public void test_command_queue_admission_changes_world_hash_before_application()
        {
            var rig = new Rig();
            ulong before = rig.Host.WorldStateHash();
            Assert.Equal(WorldHashOracle(0, CoreHashOracle(1), rig.SystemHashes()), before);

            Command noop = NoOp(700);
            Admit(rig.Host, noop);
            Assert.NotEqual(before, rig.Host.WorldStateHash());
            Assert.Equal(WorldHashOracle(0, CoreHashOracle(2, (noop, 1u)), rig.SystemHashes()), rig.Host.WorldStateHash());

            // A rejected submit changes nothing.
            ulong admitted = rig.Host.WorldStateHash();
            Reject(rig.Host, NoOp(0));
            Reject(rig.Host, new Command(9, Local, CommandKind.SetServersOpen, new byte[1]));
            Assert.Equal(admitted, rig.Host.WorldStateHash());
        }

        [Fact]
        public void test_command_queue_core_hash_feeds_pending_commands_in_total_order()
        {
            var rig = new Rig();
            Command noop = NoOp(700);
            Command flow = Flow(650, 0xA1B2C3D4, -5);
            Command stand = Stand(650, ulong.MaxValue - 1, 65535);
            Admit(rig.Host, noop);   // seq 1
            Admit(rig.Host, flow);   // seq 2
            Admit(rig.Host, stand);  // seq 3
            ulong pendingCore = CoreHashOracle(4, (flow, 2u), (stand, 3u), (noop, 1u));

            rig.Host.Step(601);                                   // checkpoints at 0 and 600, all pending
            Assert.Equal(2, rig.Sink.Recorded.Count);
            foreach (Checkpoint cp in rig.Sink.Recorded)
            {
                Assert.Equal(pendingCore, cp.CoreHash);
                Assert.Equal(WorldHashOracle(cp.Tick + 1, cp.CoreHash, cp.SystemHashes), cp.WorldHash);
            }

            rig.Host.Step(600);                                   // through tick 1200, all applied
            Checkpoint last = rig.Sink.Recorded[rig.Sink.Recorded.Count - 1];
            Assert.Equal(1200UL, last.Tick);
            Assert.Equal(CoreHashOracle(4), last.CoreHash);
            Assert.Equal(rig.SystemHashes(), last.SystemHashes);
            Assert.Equal(WorldHashOracle(1201, CoreHashOracle(4), rig.SystemHashes()), last.WorldHash);
        }

        // ------------------------------------------------------------ headless day, determinism, replay

        [Fact]
        public void test_command_queue_headless_day_fixture_schedule_applies_in_total_order()
        {
            const ulong seed = 0x5EED0005A0000001UL;
            List<Command> schedule = FixtureSchedule(seed, 400);
            var rig = new Rig(2026UL);
            foreach (Command c in schedule) Admit(rig.Host, c);
            rig.Host.Step(TicksPerDay);

            // Expected: every non-NoOp command, sorted by (Tick, Sequence = submission index + 1).
            var expected = schedule
                .Select((c, i) => (Cmd: c, Seq: (uint)(i + 1)))
                .Where(p => p.Cmd.Kind != CommandKind.NoOp)
                .OrderBy(p => p.Cmd.Tick).ThenBy(p => p.Seq)
                .ToList();
            Assert.Equal(expected.Count, rig.Order.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Applied a = rig.Order[i];
                if (a.Sequence != expected[i].Seq || a.ContextTick != expected[i].Cmd.Tick || !a.Payload.AsSpan().SequenceEqual(expected[i].Cmd.Payload))
                {
                    Assert.Fail(At(seed, i) + ": application " + i + " out of order");
                }
            }
            Assert.Equal(24, rig.Sink.Recorded.Count);
            Assert.Equal(schedule.Count, rig.Host.CommandLogSince(0).Count);
            Assert.Equal(WorldHashOracle(TicksPerDay, CoreHashOracle((uint)schedule.Count + 1u), rig.SystemHashes()), rig.Host.WorldStateHash());
        }

        [Fact]
        public void test_command_queue_same_submissions_same_seed_identical_checkpoints()
        {
            const ulong seed = 0x5EED0005A0000002UL;
            List<Command> schedule = FixtureSchedule(seed, 300);
            var a = new Rig(7UL);
            var b = new Rig(7UL);
            foreach (Command c in schedule)
            {
                Admit(a.Host, c);
                Admit(b.Host, c);
            }
            a.Host.Step(TicksPerDay);

            var gen = new SplitMix64(seed ^ 1UL);
            ulong remaining = TicksPerDay;
            while (remaining > 0)
            {
                uint chunk = (uint)(1 + gen.Below(900));
                if (chunk > remaining) chunk = (uint)remaining;
                b.Host.Step(chunk);
                remaining -= chunk;
            }

            Assert.Equal(a.Sink.Recorded.Count, b.Sink.Recorded.Count);
            for (int i = 0; i < a.Sink.Recorded.Count; i++)
            {
                Assert.Equal(a.Sink.Recorded[i].Tick, b.Sink.Recorded[i].Tick);
                Assert.Equal(a.Sink.Recorded[i].WorldHash, b.Sink.Recorded[i].WorldHash);
                Assert.Equal(a.Sink.Recorded[i].CoreHash, b.Sink.Recorded[i].CoreHash);
                Assert.Equal(a.Sink.Recorded[i].SystemHashes, b.Sink.Recorded[i].SystemHashes);
            }
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());
        }

        [Fact]
        public void test_command_queue_replay_of_command_log_reproduces_session()
        {
            // "The log is the save": a session with submissions spread over the day is
            // replayed on a fresh host from CommandLogSince(0) alone. Systems see the
            // same commands in the same order, so every checkpoint's system hashes and
            // the final world hash agree. (Intermediate CoreHash may differ: the replay
            // holds future commands as pending earlier than the original did.)
            const ulong seed = 0x5EED0005A0000003UL;
            var gen = new SplitMix64(seed);
            var original = new Rig(11UL);
            ulong executed = 0;
            while (executed < TicksPerDay)
            {
                int burst = gen.Below(4);
                for (int k = 0; k < burst; k++)
                {
                    ulong tick = executed + 1 + (ulong)gen.Below(2000);
                    if (tick >= TicksPerDay) continue;
                    Admit(original.Host, RandomCommand(gen, tick));
                }
                uint step = (uint)Math.Min(1 + gen.Below(700), (int)(TicksPerDay - executed));
                original.Host.Step(step);
                executed += step;
            }
            IReadOnlyList<Command> log = original.Host.CommandLogSince(0);
            Assert.True(log.Count > 20, "the fixture must submit a meaningful number of commands");

            var replay = new Rig(11UL);
            foreach (Command c in log)
            {
                Admit(replay.Host, new Command(c.Tick, c.Issuer, c.Kind, c.Payload));
            }
            replay.Host.Step(TicksPerDay);

            Assert.Equal(original.Sink.Recorded.Count, replay.Sink.Recorded.Count);
            for (int i = 0; i < original.Sink.Recorded.Count; i++)
            {
                Assert.Equal(original.Sink.Recorded[i].SystemHashes, replay.Sink.Recorded[i].SystemHashes);
            }
            Assert.Equal(original.Host.WorldStateHash(), replay.Host.WorldStateHash());
            IReadOnlyList<Command> replayLog = replay.Host.CommandLogSince(0);
            Assert.Equal(log.Count, replayLog.Count);
            for (int i = 0; i < log.Count; i++)
            {
                AssertSameCommand(log[i], replayLog[i]);
            }
        }

        // ------------------------------------------------------------ budget

        [Fact]
        [Trait("Category", "Budget")]
        public void test_command_queue_apply_due_allocates_zero_bytes()
        {
            // T-005 budget: ApplyDue must not allocate on the hot path. Both hosts step
            // ticks 1..599 (no checkpoint); one applies a command every tick. The
            // difference in allocated bytes is what applying commands costs. A warm-up
            // host runs the same path first so one-time JIT and type setup are excluded.
            var warm = new CountingRig();
            for (ulong t = 1; t < 600; t++) Admit(warm.Host, Flow(t, (uint)t, 1));
            warm.Host.Step(600);

            var counting = new CountingRig();
            var idle = new CountingRig();
            for (ulong t = 1; t < 600; t++)
            {
                Admit(counting.Host, Flow(t, (uint)t, 1));
                if (t % 3 == 0) Admit(counting.Host, NoOp(t));
            }
            counting.Host.Step(1);
            idle.Host.Step(1);

            long before = GC.GetAllocatedBytesForCurrentThread();
            counting.Host.Step(599);
            long withCommands = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            idle.Host.Step(599);
            long withoutCommands = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(599, counting.Handler.Applied);
            Assert.Equal(withoutCommands, withCommands);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_command_queue_day_with_command_every_tick_within_core_budget()
        {
            // sim.core's 0.25 ms/tick (03 "Performance"), with one command due per tick
            // and a handler that does nothing, measured over a whole day.
            var warm = new CountingRig();
            for (ulong t = 1; t < 2000; t++) Admit(warm.Host, Flow(t, 1, 1));
            warm.Host.Step(2000);

            var rig = new CountingRig();
            for (ulong t = 1; t < TicksPerDay; t++) Admit(rig.Host, Flow(t, 1, 1));
            long start = Stopwatch.GetTimestamp();
            rig.Host.Step(TicksPerDay);
            long elapsed = Stopwatch.GetTimestamp() - start;

            // 0.25 ms = Frequency / 4000 ticks of the stopwatch, per sim tick.
            long budget = Stopwatch.Frequency * TicksPerDay / 4000;
            Assert.Equal((int)TicksPerDay - 1, rig.Handler.Applied);
            Assert.True(elapsed <= budget, "day took " + elapsed + " stopwatch ticks; budget " + budget);
        }

        // ------------------------------------------------------------ helpers

        private static List<Command> FixtureSchedule(ulong seed, int count)
        {
            var gen = new SplitMix64(seed);
            var list = new List<Command>();
            for (int i = 0; i < count; i++)
            {
                ulong tick = 1 + (ulong)gen.Below((int)TicksPerDay - 1);
                // Cluster some commands on shared ticks to exercise same-tick order.
                if (i > 0 && gen.Below(4) == 0) tick = list[gen.Below(list.Count)].Tick;
                list.Add(RandomCommand(gen, tick));
            }
            return list;
        }

        private static Command RandomCommand(SplitMix64 gen, ulong tick)
        {
            switch (gen.Below(3))
            {
                case 0:
                    return NoOp(tick);
                case 1:
                    // An even low byte keeps the first payload byte off ForbiddenTarget.
                    return Flow(tick, (uint)gen.Next() & 0xFFFFFFFEu, unchecked((int)gen.Next()));
                default:
                    return Stand(tick, gen.Next() & ~1UL, (ushort)gen.Next());
            }
        }

        private sealed class ProbeFailure : Exception
        {
        }

        /// <summary>A handler whose Apply allocates nothing, for the budget tests.</summary>
        private sealed class CountingHandler : ICommandHandler
        {
            public CommandKind Kind => CommandKind.SetServersOpen;

            public int Applied;
            public ulong Sink;

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                return payload.Length == FlowPayloadLength ? CommandRejection.None : CommandRejection.MalformedPayload;
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                Applied++;
                Sink ^= cmd.Payload[0] ^ ctx.Tick;
            }
        }

        private sealed class CountingRig
        {
            public readonly CountingHandler Handler = new CountingHandler();
            public readonly ISimHost Host;

            public CountingRig()
            {
                ISimHostBuilder builder = Builder(1UL, new RecordingLog(), new CheckpointLog());
                builder.Services.Commands.Register(new SystemId(FlowPosition), Handler);
                builder.Register(new OwnerSystem(FlowPosition, null, null));
                Host = builder.Build();
            }
        }
    }
}
