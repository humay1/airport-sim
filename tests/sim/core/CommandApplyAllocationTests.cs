using System;
using System.Buffers.Binary;
using Xunit;
using static AirportSim.Sim.Core.Tests.CommandTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-047. 08 §8.7 "Allocation" (Q-065): on a tick that completes normally,
    /// applying commands allocates nothing, for every kind, any number of due
    /// commands per tick and any log size, from the first tick after Build.
    /// Metered over ISimHost.Step on ticks 1..599, which hold no checkpoint
    /// (03 "How a budget is measured", the Step meter). Every command is
    /// admitted before the window, outside Step, so the log never grows inside
    /// it. A throwaway host runs the identical schedule first, so JIT and type
    /// loading are not charged to the queue; queue state is per host, so this
    /// warms nothing the spec rules on.
    /// </summary>
    public sealed class CommandApplyAllocationTests
    {
        private const ulong WindowFirst = 1;
        private const ulong WindowEnd = 600;
        private const ulong BurstTick = 300;
        private const int BurstPerKind = 40;

        private sealed class Expected
        {
            public int Flow;
            public int Stand;
            public int NoOps;
        }

        /// <summary>
        /// A probe handler whose Apply allocates nothing: it decodes the admitted
        /// payload in place, counts the call and notes a context tick other than
        /// the command's.
        /// </summary>
        private sealed class ProbeHandler : ICommandHandler
        {
            private readonly int _payloadLength;

            public ProbeHandler(CommandKind kind, int payloadLength)
            {
                Kind = kind;
                _payloadLength = payloadLength;
            }

            public CommandKind Kind { get; }

            public int Applied;
            public int WrongTick;
            public ulong Sink;

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                return payload.Length == _payloadLength ? CommandRejection.None : CommandRejection.MalformedPayload;
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                Applied++;
                if (ctx.Tick != cmd.Tick)
                {
                    WrongTick++;
                }

                ReadOnlySpan<byte> p = cmd.Payload;
                Sink ^= BinaryPrimitives.ReadUInt32LittleEndian(p) + (ulong)p.Length;            }
        }

        private sealed class AllocRig
        {
            public readonly ProbeHandler Airside = new ProbeHandler(CommandKind.ReassignStand, StandPayloadLength);
            public readonly ProbeHandler FlowHandler = new ProbeHandler(CommandKind.SetServersOpen, FlowPayloadLength);
            public readonly CountingCheckpointSink Sink = new CountingCheckpointSink();
            public readonly ISimHost Host;

            public AllocRig()
            {
                ISimHostBuilder b = Harness.Builder(Sink);
                b.Services.Commands.Register(new SystemId(AirsidePosition), Airside);
                b.Services.Commands.Register(new SystemId(FlowPosition), FlowHandler);
                b.Register(new ProbeSystem(AirsidePosition, "probe.airside"));
                b.Register(new ProbeSystem(FlowPosition, "probe.flow"));
                Host = b.Build();
            }
        }

        /// <summary>
        /// Window ticks by t % 5: 1 has three of each handled kind and two NoOps,
        /// interleaved; 2 has only NoOps; 4 has one ReassignStand; 0 and 3 have
        /// nothing due. BurstTick has BurstPerKind of every kind. Ticks 601..1199
        /// get one of each kind too, so the log holds commands beyond the window.
        /// </summary>
        private static Expected Schedule(ISimHost host)
        {
            var e = new Expected();
            for (ulong t = WindowFirst; t < WindowEnd; t++)
            {
                if (t == BurstTick)
                {
                    for (int i = 0; i < BurstPerKind; i++)
                    {
                        Admit(host, Stand(t, (ulong)i, (ushort)i));
                        Admit(host, NoOp(t));
                        Admit(host, Flow(t, (uint)i, i));
                        e.Stand++;
                        e.NoOps++;
                        e.Flow++;
                    }

                    continue;
                }

                switch (t % 5)
                {
                    case 1:
                        for (int i = 0; i < 3; i++)
                        {
                            Admit(host, Flow(t, (uint)(t + (ulong)i), i + 1));
                            if (i < 2)
                            {
                                Admit(host, NoOp(t));
                                e.NoOps++;
                            }

                            Admit(host, Stand(t, t * 10 + (ulong)i, (ushort)(i + 1)));
                            e.Flow++;
                            e.Stand++;
                        }

                        break;
                    case 2:
                        Admit(host, NoOp(t));
                        Admit(host, NoOp(t));
                        e.NoOps += 2;
                        break;
                    case 4:
                        Admit(host, Stand(t, t, 7));
                        e.Stand++;
                        break;
                }
            }

            for (ulong t = WindowEnd + 1; t < 2 * WindowEnd; t++)
            {
                Admit(host, Flow(t, 1, 1));
                Admit(host, Stand(t, 1, 1));
                Admit(host, NoOp(t));
            }

            return e;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_command_queue_apply_due_of_every_kind_allocates_nothing()
        {
            var warm = new AllocRig();
            Schedule(warm.Host);
            warm.Host.Step(1);
            warm.Host.Step((uint)(WindowEnd - WindowFirst));

            var rig = new AllocRig();
            Expected e = Schedule(rig.Host);
            rig.Host.Step(1);
            Assert.Equal(WindowFirst, rig.Host.CurrentTick);
            Assert.Equal(1, rig.Sink.Count);
            Assert.Equal(0, rig.FlowHandler.Applied);
            Assert.Equal(0, rig.Airside.Applied);

            long start = Allocation.Start();
            rig.Host.Step((uint)(WindowEnd - WindowFirst));
            long allocated = Allocation.Since(start);

            Assert.Equal(WindowEnd, rig.Host.CurrentTick);
            Assert.Equal(1, rig.Sink.Count);
            Assert.True(e.NoOps > 0 && e.Flow > 0 && e.Stand > 0);
            Assert.Equal(e.Flow, rig.FlowHandler.Applied);
            Assert.Equal(e.Stand, rig.Airside.Applied);
            Assert.Equal(0, rig.FlowHandler.WrongTick);
            Assert.Equal(0, rig.Airside.WrongTick);
            Assert.Equal(0L, allocated);
        }
    }
}
