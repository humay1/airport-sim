using System;
using System.Collections.Generic;
using System.Globalization;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Core.Tests
{
    // Test doubles for the T-001 suite. 08 §8.11a (Q-014): sim.core publishes no
    // null or empty IContentIndex, ICheckpointSink or ISimLog, so tests write
    // their own. Event payloads are test-local structs: every real payload is
    // T-026's (10 §10.9), and the bus is generic over ISimEvent.

    internal readonly struct Ping : ISimEvent
    {
        public Ping(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    internal readonly struct Pong : ISimEvent
    {
        public Pong(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    internal delegate void TickAction(ProbeSystem self, in TickContext ctx);

    /// <summary>
    /// A system at a legal registry position whose module is absent from the
    /// build (08 §8.5, Q-014: probes are allowed). Its state is one ulong,
    /// updated by an order-sensitive mix so a reordering shows in its hash.
    /// </summary>
    internal sealed class ProbeSystem : ISimSystem
    {
        public ProbeSystem(ushort id, string name = "probe")
        {
            Id = new SystemId(id);
            Name = name;
        }

        public SystemId Id { get; }

        public string Name { get; }

        public ulong State;

        public int TickCalls;

        public TickAction? OnTick;

        public Func<ulong>? HashOverride;

        public void Tick(in TickContext ctx)
        {
            TickCalls++;
            OnTick?.Invoke(this, ctx);
        }

        public ulong ComputeStateHash()
        {
            return HashOverride != null ? HashOverride() : HashOf(Id.Value, State);
        }

        public void Mix(ulong x)
        {
            State = MixStep(State, x);
        }

        public static ulong MixStep(ulong state, ulong x)
        {
            unchecked
            {
                return (state ^ x) * 0x100000001B3UL + 1UL;
            }
        }

        public static ulong HashOf(ushort id, ulong state)
        {
            return FnvOracle.Words(id, state);
        }
    }

    internal sealed class RecordingCheckpointSink : ICheckpointSink
    {
        public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

        public Action<Checkpoint>? OnRecord;

        public void Record(in Checkpoint cp)
        {
            Recorded.Add(cp);
            OnRecord?.Invoke(cp);
        }

        public List<string> Describe()
        {
            var result = new List<string>(Recorded.Count);
            foreach (Checkpoint cp in Recorded)
            {
                result.Add(DescribeCheckpoint(cp));
            }

            return result;
        }

        public static string DescribeCheckpoint(in Checkpoint cp)
        {
            var parts = new List<string>();
            foreach (ulong h in cp.SystemHashes)
            {
                parts.Add(h.ToString("X16", CultureInfo.InvariantCulture));
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "t={0} world={1:X16} core={2:X16} systems=[{3}]",
                cp.Tick,
                cp.WorldHash,
                cp.CoreHash,
                string.Join(",", parts));
        }
    }

    /// <summary>Counts checkpoints without allocating on record.</summary>
    internal sealed class CountingCheckpointSink : ICheckpointSink
    {
        public int Count;

        public void Record(in Checkpoint cp)
        {
            Count++;
        }
    }

    internal sealed class NullLog : ISimLog
    {
        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
        }
    }

    internal sealed class CapturingLog : ISimLog
    {
        public readonly List<string> Lines = new List<string>();

        public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
        {
            Lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}",
                tick,
                (byte)level,
                system.Value,
                (ushort)key,
                args.Count,
                args.A0,
                args.A1,
                args.A2,
                args.A3));
        }
    }

    internal sealed class EmptyContentIndex : IContentIndex
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

    /// <summary>
    /// FNV-1a-64 over bytes, written directly from 08 §8.9 "Encoding" (Q-017):
    /// offset basis 0xCBF29CE484222325, prime 0x100000001B3, integers as 8
    /// little-endian bytes, bool as one byte, spans prefixed with a uint64
    /// length. Independent of StateHasher, so it can serve as its oracle.
    /// </summary>
    internal sealed class FnvOracle
    {
        public const ulong OffsetBasis = 0xCBF29CE484222325UL;
        public const ulong Prime = 0x100000001B3UL;

        private ulong _h = OffsetBasis;

        public ulong Result => _h;

        public FnvOracle Byte(byte b)
        {
            unchecked
            {
                _h = (_h ^ b) * Prime;
            }

            return this;
        }

        public FnvOracle U64(ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                Byte((byte)(v >> (8 * i)));
            }

            return this;
        }

        public FnvOracle I64(long v)
        {
            return U64(unchecked((ulong)v));
        }

        public FnvOracle Bool(bool v)
        {
            return Byte(v ? (byte)1 : (byte)0);
        }

        public FnvOracle Span(byte[] bytes)
        {
            U64((ulong)bytes.Length);
            foreach (byte b in bytes)
            {
                Byte(b);
            }

            return this;
        }

        public static ulong Words(params ulong[] words)
        {
            var o = new FnvOracle();
            foreach (ulong w in words)
            {
                o.U64(w);
            }

            return o.Result;
        }

        /// <summary>
        /// The world hash of 08 §8.9: ticks executed, then CoreHash, then each
        /// registered system's hash in registry order, all as uint64.
        /// </summary>
        public static ulong WorldHash(ulong ticksExecuted, ulong coreHash, params ulong[] systemHashes)
        {
            var o = new FnvOracle();
            o.U64(ticksExecuted);
            o.U64(coreHash);
            foreach (ulong h in systemHashes)
            {
                o.U64(h);
            }

            return o.Result;
        }

        /// <summary>
        /// The core section at T-001 scope (08 §8.9, task T-001): next command
        /// sequence 1, no pending commands, then the non-zero id counters as
        /// (owner, counter) pairs in ascending owner order.
        /// </summary>
        public static ulong CoreHashT001(params (ushort Owner, ulong Counter)[] counters)
        {
            var o = new FnvOracle();
            o.U64(1UL);
            o.U64(0UL);
            o.U64((ulong)counters.Length);
            foreach ((ushort owner, ulong counter) in counters)
            {
                o.U64(owner);
                o.U64(counter);
            }

            return o.Result;
        }
    }

    internal static class Harness
    {
        public const ulong Seed = 0x5EED_0001UL;

        public static SimHostConfig Config(ICheckpointSink sink, ISimLog? log = null, ulong seed = Seed)
        {
            return new SimHostConfig(seed, new EmptyContentIndex(), sink, log ?? new NullLog());
        }

        public static ISimHostBuilder Builder(ICheckpointSink sink, ISimLog? log = null)
        {
            return SimHostFactory.CreateBuilder(Config(sink, log));
        }

        public static ISimHost Build(ICheckpointSink sink, params ISimSystem[] systems)
        {
            ISimHostBuilder b = Builder(sink);
            foreach (ISimSystem s in systems)
            {
                b.Register(s);
            }

            return b.Build();
        }

        public static string DescribeEnvelope(string tag, in EventEnvelope env, int value)
        {
            string cause = env.Cause.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0}.{1}", env.Cause.Id.Tick, env.Cause.Id.Sequence)
                : "-";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} t={1} id={2}.{3} src={4} cause={5} v={6}",
                tag,
                env.Tick,
                env.Id.Tick,
                env.Id.Sequence,
                env.Source.Value,
                cause,
                value);
        }

        /// <summary>Returns the next chunk size so that the total never exceeds <paramref name="remaining"/>.</summary>
        public static uint NextChunk(SplitMix64 rng, ulong remaining, uint maxChunk)
        {
            ulong c = rng.Next() % (maxChunk + 1UL);
            return (uint)Math.Min(c, remaining);
        }
    }

    /// <summary>
    /// A small deterministic fixture exercising every T-001 moving part at
    /// once: phase-2 publishes, a same-tick cascade with causes, per-owner id
    /// allocation during ticks, and log lines. Used for the headless day,
    /// determinism, chunking and replay (Q-014 A8) tests.
    /// </summary>
    internal sealed class RichFixture
    {
        public readonly RecordingCheckpointSink Sink = new RecordingCheckpointSink();
        public readonly List<string> Trace = new List<string>();
        public readonly ProbeSystem Source = new ProbeSystem(1, "probe.source");
        public readonly ProbeSystem Relay = new ProbeSystem(4, "probe.relay");
        public readonly ProbeSystem Observer = new ProbeSystem(7, "probe.observer");
        public readonly ISimHost Host;

        public RichFixture(ISimLog log, ulong seed = Harness.Seed)
        {
            ISimHostBuilder b = SimHostFactory.CreateBuilder(Harness.Config(Sink, log, seed));
            IIdAllocator ids = b.Services.Ids;
            IEventBus bus = b.Services.Events;
            List<string> trace = Trace;
            ProbeSystem relay = Relay;
            ProbeSystem observer = Observer;

            Source.OnTick = (ProbeSystem self, in TickContext ctx) =>
            {
                self.Mix(ctx.Tick);
                ctx.Events.Publish(new Ping((int)(ctx.Tick % 7UL)), EventRef.None);
                if (ctx.Tick % 50UL == 0UL)
                {
                    self.Mix(ids.Next(self.Id).Value);
                }

                if (ctx.Tick % 100UL == 0UL)
                {
                    ctx.Log.Write(ctx.Tick, LogLevel.Info, self.Id, LogKey.None, new LogArgs(unchecked((long)ctx.Tick), unchecked((long)self.State)));
                }
            };

            bus.Subscribe<Ping>(Relay.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                relay.Mix(env.Id.Tick);
                relay.Mix(env.Id.Sequence);
                relay.Mix((ulong)evt.Value);
                trace.Add(Harness.DescribeEnvelope("relay.ping", env, evt.Value));
                if (evt.Value == 3)
                {
                    ctx.Events.Publish(new Pong(evt.Value * 10), new EventRef(env.Id, true));
                }
            });

            bus.Subscribe<Ping>(Observer.Id, (in EventEnvelope env, in Ping evt, in TickContext ctx) =>
            {
                observer.Mix(env.Source.Value);
                trace.Add(Harness.DescribeEnvelope("observer.ping", env, evt.Value));
            });

            bus.Subscribe<Pong>(Observer.Id, (in EventEnvelope env, in Pong evt, in TickContext ctx) =>
            {
                observer.Mix(env.Source.Value);
                observer.Mix(env.Cause.HasValue ? env.Cause.Id.Sequence + 1UL : 0UL);
                observer.Mix(ids.Next(observer.Id).Value);
                trace.Add(Harness.DescribeEnvelope("observer.pong", env, evt.Value));
                ctx.Log.Write(ctx.Tick, LogLevel.Debug, observer.Id, LogKey.None, new LogArgs(evt.Value));
            });

            b.Register(Source);
            b.Register(Relay);
            b.Register(Observer);
            Host = b.Build();
        }
    }
}
