using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Test-only helpers for the T-005 command queue tests, written from
    /// 08-interfaces-core.md §8.7 (Q-010, Q-020), §8.9 (the core section, Q-017)
    /// and §8.11a, never from an implementation. All types are nested so that
    /// they cannot collide with helpers other test branches add to this shared
    /// project (07 L9).
    /// </summary>
    internal static class CommandTestSupport
    {
        /// <summary>Registry positions of the payload table's owners (§8.7 "Handler registration").</summary>
        internal const ushort AirsidePosition = 3;
        internal const ushort FlowPosition = 4;

        internal const int FlowPayloadLength = 8;
        internal const int StandPayloadLength = 10;

        /// <summary>First payload byte that makes <see cref="ScriptedHandler"/> answer NotPermitted.</summary>
        internal const byte ForbiddenTarget = 0xFF;

        internal static PlayerId Local => SimConstants.PLAYER_LOCAL;

        internal static readonly PlayerId Foreign = new PlayerId(1);

        // ------------------------------------------------------------ commands and payloads

        /// <summary>SetServersOpen payload: NodeId.Value as uint32, count as int32, little-endian.</summary>
        internal static byte[] FlowPayload(uint node, int count)
        {
            byte[] b = new byte[FlowPayloadLength];
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0, 4), node);
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4, 4), count);
            return b;
        }

        /// <summary>ReassignStand payload: FlightId.Value as uint64, StandId.Value as uint16, little-endian.</summary>
        internal static byte[] StandPayload(ulong flight, ushort stand)
        {
            byte[] b = new byte[StandPayloadLength];
            BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0, 8), flight);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8, 2), stand);
            return b;
        }

        internal static Command NoOp(ulong tick)
        {
            return new Command(tick, Local, CommandKind.NoOp, Array.Empty<byte>());
        }

        internal static Command Flow(ulong tick, uint node, int count)
        {
            return new Command(tick, Local, CommandKind.SetServersOpen, FlowPayload(node, count));
        }

        internal static Command Stand(ulong tick, ulong flight, ushort stand)
        {
            return new Command(tick, Local, CommandKind.ReassignStand, StandPayload(flight, stand));
        }

        internal static void Admit(ISimHost host, Command cmd)
        {
            bool ok = host.TrySubmit(in cmd, out CommandRejection reason);
            Assert.Equal(CommandRejection.None, reason);
            Assert.True(ok, "expected admission of " + Describe(cmd));
        }

        internal static CommandRejection Reject(ISimHost host, Command cmd)
        {
            bool ok = host.TrySubmit(in cmd, out CommandRejection reason);
            Assert.False(ok, "expected rejection of " + Describe(cmd));
            Assert.NotEqual(CommandRejection.None, reason);
            return reason;
        }

        internal static string Describe(Command cmd)
        {
            return cmd.Kind + "@" + cmd.Tick.ToString(CultureInfo.InvariantCulture)
                + " issuer " + cmd.Issuer.Value.ToString(CultureInfo.InvariantCulture)
                + " seq " + cmd.Sequence.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ hash oracles

        /// <summary>Independent FNV-1a-64 with the §8.9 encoding written out by hand.</summary>
        internal sealed class CommandFnv
        {
            public ulong Result { get; private set; } = 0xCBF29CE484222325UL;

            public CommandFnv U64(ulong v)
            {
                unchecked
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Result = (Result ^ (byte)(v >> (8 * i))) * 0x100000001B3UL;
                    }
                }
                return this;
            }

            public CommandFnv Span(byte[] bytes)
            {
                U64((ulong)bytes.Length);
                unchecked
                {
                    foreach (byte b in bytes)
                    {
                        Result = (Result ^ b) * 0x100000001B3UL;
                    }
                }
                return this;
            }
        }

        /// <summary>
        /// §8.9 core section: the next Sequence, the pending count, each pending command
        /// in (Tick, Issuer, Sequence) order as Tick, Issuer.Value, Kind, Sequence, Payload
        /// (span), then the number of non-zero id counters (0: no test here allocates ids).
        /// <paramref name="pending"/> must already be in total order.
        /// </summary>
        internal static ulong CoreHashOracle(uint nextSequence, params (Command Cmd, uint Sequence)[] pending)
        {
            var o = new CommandFnv();
            o.U64(nextSequence);
            o.U64((ulong)pending.Length);
            foreach ((Command cmd, uint seq) in pending)
            {
                o.U64(cmd.Tick);
                o.U64(cmd.Issuer.Value);
                o.U64((ushort)cmd.Kind);
                o.U64(seq);
                o.Span(cmd.Payload);
            }
            o.U64(0UL);
            return o.Result;
        }

        internal static ulong WorldHashOracle(ulong ticksExecuted, ulong coreHash, params ulong[] systemHashes)
        {
            var o = new CommandFnv();
            o.U64(ticksExecuted);
            o.U64(coreHash);
            foreach (ulong h in systemHashes)
            {
                o.U64(h);
            }
            return o.Result;
        }

        /// <summary>SplitMix64 exactly as pinned in §8.8, the input generator for property loops (07 L4).</summary>
        internal sealed class CommandGen
        {
            private ulong _x;

            public CommandGen(ulong seed)
            {
                _x = seed;
            }

            public ulong Next()
            {
                unchecked
                {
                    _x += 0x9E3779B97F4A7C15UL;
                    ulong z = _x;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }

            public int Below(int n)
            {
                return (int)(Next() % (ulong)n);
            }
        }

        internal static string At(ulong seed, int iteration)
        {
            return "seed 0x" + seed.ToString("X16", CultureInfo.InvariantCulture)
                + ", iteration " + iteration.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------ doubles

        /// <summary>What a handler saw at Apply, copied out of the command.</summary>
        internal sealed class Applied
        {
            public Applied(in Command cmd, ulong contextTick)
            {
                Tick = cmd.Tick;
                Issuer = cmd.Issuer.Value;
                Kind = cmd.Kind;
                Sequence = cmd.Sequence;
                Payload = (byte[])cmd.Payload.Clone();
                ContextTick = contextTick;
            }

            public ulong Tick { get; }
            public ushort Issuer { get; }
            public CommandKind Kind { get; }
            public uint Sequence { get; }
            public byte[] Payload { get; }
            public ulong ContextTick { get; }
        }

        /// <summary>
        /// A handler for one kind. Validate is a pure function of the payload: wrong
        /// length is MalformedPayload, a first byte of <see cref="ForbiddenTarget"/> is
        /// NotPermitted. It counts its calls so tests can prove "exactly once".
        /// </summary>
        internal sealed class ScriptedHandler : ICommandHandler
        {
            private readonly int _payloadLength;
            private readonly List<Applied>? _sharedOrder;
            private readonly List<string>? _trace;

            public ScriptedHandler(CommandKind kind, int payloadLength, List<Applied>? sharedOrder, List<string>? trace)
            {
                Kind = kind;
                _payloadLength = payloadLength;
                _sharedOrder = sharedOrder;
                _trace = trace;
            }

            public CommandKind Kind { get; }

            public int ValidateCalls { get; private set; }

            public byte[]? LastValidated { get; private set; }

            public readonly List<Applied> AppliedCommands = new List<Applied>();

            /// <summary>Optional extra behaviour at Apply, run after recording.</summary>
            public Action<Command, TickContext>? OnApply { get; set; }

            public CommandRejection Validate(ReadOnlySpan<byte> payload)
            {
                ValidateCalls++;
                LastValidated = payload.ToArray();
                if (payload.Length != _payloadLength)
                {
                    return CommandRejection.MalformedPayload;
                }
                if (payload.Length > 0 && payload[0] == ForbiddenTarget)
                {
                    return CommandRejection.NotPermitted;
                }
                return CommandRejection.None;
            }

            public void Apply(in Command cmd, in TickContext ctx)
            {
                var a = new Applied(in cmd, ctx.Tick);
                AppliedCommands.Add(a);
                _sharedOrder?.Add(a);
                _trace?.Add("apply " + cmd.Kind + " " + ctx.Tick.ToString(CultureInfo.InvariantCulture));
                OnApply?.Invoke(cmd, ctx);
            }
        }

        /// <summary>
        /// The owning system of a handler. Its state is the ordered list of commands its
        /// handler applied, fed as count then (Tick, Kind, Payload) each, so a wrong
        /// application order or a lost command changes its hash. Sequence is not fed:
        /// it is the queue's bookkeeping, and a replay may assign different values.
        /// </summary>
        internal sealed class OwnerSystem : ISimSystem
        {
            private readonly ushort _position;
            private readonly ScriptedHandler? _handler;
            private readonly List<string>? _trace;

            public OwnerSystem(ushort position, ScriptedHandler? handler, List<string>? trace)
            {
                _position = position;
                _handler = handler;
                _trace = trace;
            }

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.command_owner";

            /// <summary>Optional extra behaviour at Tick.</summary>
            public Action<TickContext>? OnTick { get; set; }

            public void Tick(in TickContext ctx)
            {
                _trace?.Add("tick " + _position.ToString(CultureInfo.InvariantCulture) + " " + ctx.Tick.ToString(CultureInfo.InvariantCulture));
                OnTick?.Invoke(ctx);
            }

            public ulong ComputeStateHash()
            {
                var h = new StateHasher();
                if (_handler == null)
                {
                    return h.Result;
                }
                h.Feed((ulong)_handler.AppliedCommands.Count);
                foreach (Applied a in _handler.AppliedCommands)
                {
                    h.Feed(a.Tick);
                    h.Feed((ulong)a.Kind);
                    h.Feed(new ReadOnlySpan<byte>(a.Payload));
                }
                return h.Result;
            }
        }

        internal sealed class LogEntry
        {
            public LogEntry(ulong tick, LogLevel level, SystemId system, LogKey key, LogArgs args)
            {
                Tick = tick;
                Level = level;
                System = system;
                Key = key;
                Args = args;
            }

            public ulong Tick { get; }
            public LogLevel Level { get; }
            public SystemId System { get; }
            public LogKey Key { get; }
            public LogArgs Args { get; }
        }

        internal sealed class RecordingLog : ISimLog
        {
            public readonly List<LogEntry> Entries = new List<LogEntry>();

            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
                Entries.Add(new LogEntry(tick, level, system, key, args));
            }
        }

        internal sealed class NoContent : IContentIndex
        {
            public bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
            {
                definition = default!;
                return false;
            }

            public IReadOnlyList<ContentId> AllOf(ContentKind kind)
            {
                return Array.Empty<ContentId>();
            }
        }

        internal sealed class CheckpointLog : ICheckpointSink
        {
            public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

            public void Record(in Checkpoint cp)
            {
                Recorded.Add(cp);
            }
        }

        internal static ISimHostBuilder Builder(ulong seed, ISimLog log, ICheckpointSink sink)
        {
            var config = new SimHostConfig(seed, new NoContent(), sink, log);
            return SimHostFactory.CreateBuilder(in config);
        }

        /// <summary>
        /// A host with a ReassignStand handler owned by position 3 and a SetServersOpen
        /// handler owned by position 4, each with its owner system, registered the
        /// §8.11a way: handlers during construction, then systems in registry order.
        /// </summary>
        internal sealed class Rig
        {
            public readonly CheckpointLog Sink = new CheckpointLog();
            public readonly RecordingLog Log = new RecordingLog();
            public readonly List<Applied> Order = new List<Applied>();
            public readonly List<string> Trace = new List<string>();
            public readonly ScriptedHandler? Airside;
            public readonly ScriptedHandler? FlowHandler;
            public readonly OwnerSystem AirsideSystem;
            public readonly OwnerSystem FlowSystem;
            public readonly ISimHost Host;

            public Rig(ulong seed = 1UL, bool airsideHandler = true, bool flowHandler = true)
            {
                ISimHostBuilder builder = Builder(seed, Log, Sink);
                if (airsideHandler)
                {
                    Airside = new ScriptedHandler(CommandKind.ReassignStand, StandPayloadLength, Order, Trace);
                    builder.Services.Commands.Register(new SystemId(AirsidePosition), Airside);
                }
                if (flowHandler)
                {
                    FlowHandler = new ScriptedHandler(CommandKind.SetServersOpen, FlowPayloadLength, Order, Trace);
                    builder.Services.Commands.Register(new SystemId(FlowPosition), FlowHandler);
                }
                AirsideSystem = new OwnerSystem(AirsidePosition, Airside, Trace);
                FlowSystem = new OwnerSystem(FlowPosition, FlowHandler, Trace);
                builder.Register(AirsideSystem);
                builder.Register(FlowSystem);
                Host = builder.Build();
            }

            public ulong[] SystemHashes()
            {
                return new[] { AirsideSystem.ComputeStateHash(), FlowSystem.ComputeStateHash() };
            }
        }

        internal static void AssertSameCommand(Command expected, Command actual)
        {
            Assert.Equal(expected.Tick, actual.Tick);
            Assert.Equal(expected.Issuer.Value, actual.Issuer.Value);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Payload, actual.Payload);
        }
    }
}
