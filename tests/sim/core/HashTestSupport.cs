using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Test-only helpers for the T-004 hashing tests, written from
    /// 08-interfaces-core.md §8.9 (Q-014, Q-017) and never from an implementation.
    /// All types are nested so that they cannot collide with helpers other test
    /// branches add to this shared project (07 L9).
    /// </summary>
    internal static class HashTestSupport
    {
        internal const ulong FnvOffset = 0xCBF29CE484222325UL;
        internal const ulong FnvPrime = 0x100000001B3UL;

        /// <summary>
        /// An independent FNV-1a-64 over an explicit byte stream, with the §8.9
        /// encoding written out by hand. It is the oracle every hash is checked against.
        /// </summary>
        internal sealed class FnvOracle
        {
            public ulong Result { get; private set; } = FnvOffset;

            public FnvOracle Byte(byte b)
            {
                unchecked
                {
                    Result = (Result ^ b) * FnvPrime;
                }
                return this;
            }

            /// <summary>8 bytes, little-endian.</summary>
            public FnvOracle U64(ulong v)
            {
                for (int i = 0; i < 8; i++)
                {
                    Byte((byte)(v >> (8 * i)));
                }
                return this;
            }

            /// <summary>8 bytes, little-endian two's complement.</summary>
            public FnvOracle I64(long v)
            {
                return U64(unchecked((ulong)v));
            }

            public FnvOracle Bool(bool v)
            {
                return Byte(v ? (byte)1 : (byte)0);
            }

            /// <summary>The length as a uint64, then the bytes.</summary>
            public FnvOracle Span(byte[] bytes)
            {
                U64((ulong)bytes.Length);
                foreach (byte b in bytes)
                {
                    Byte(b);
                }
                return this;
            }

            public static ulong OfU64s(params ulong[] values)
            {
                var o = new FnvOracle();
                foreach (ulong v in values)
                {
                    o.U64(v);
                }
                return o.Result;
            }
        }

        /// <summary>SplitMix64 exactly as pinned in §8.8, the input generator for property loops (07 L4).</summary>
        internal sealed class SplitMix64
        {
            private ulong _x;

            public SplitMix64(ulong seed)
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

        /// <summary>
        /// §8.9 core section with nothing submitted and no ids allocated: the next
        /// sequence (1), no pending commands (0), no non-zero id counters (0).
        /// </summary>
        internal static readonly ulong IdleCoreHash = FnvOracle.OfU64s(1UL, 0UL, 0UL);

        /// <summary>§8.9 world hash: ticks executed, the core section, then each system hash, all as uint64.</summary>
        internal static ulong WorldHashOracle(ulong ticksExecuted, ulong coreHash, ulong[] systemHashes)
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

        // ------------------------------------------------------------ fixture systems

        /// <summary>
        /// The pattern T-004 asks later modules to copy (08 §8.9, 02-determinism rule 5):
        /// state lives in an unordered Dictionary, and ComputeStateHash feeds it in a
        /// declared, stable order: the entry count, then each (key, value) sorted by key.
        /// The cached total is derived state and is never fed.
        /// </summary>
        internal sealed class SortedFeedSystem : ISimSystem
        {
            private readonly ushort _position;
            private readonly Dictionary<ulong, long> _balances = new Dictionary<ulong, long>();
            private long _cachedTotal;

            public SortedFeedSystem(ushort position)
            {
                _position = position;
            }

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.hash";

            public long CachedTotal => _cachedTotal;

            public IEnumerable<ulong> RawEnumerationOrder => _balances.Keys;

            /// <summary>The state as plain data, in the dictionary's incidental order.</summary>
            public KeyValuePair<ulong, long>[] Export()
            {
                var entries = new KeyValuePair<ulong, long>[_balances.Count];
                int i = 0;
                foreach (KeyValuePair<ulong, long> kv in _balances)
                {
                    entries[i++] = kv;
                }
                return entries;
            }

            public void Set(ulong key, long value)
            {
                _balances.TryGetValue(key, out long old);
                _balances[key] = value;
                _cachedTotal += value - old;
            }

            public void Remove(ulong key)
            {
                if (_balances.TryGetValue(key, out long old))
                {
                    _balances.Remove(key);
                    _cachedTotal -= old;
                }
            }

            /// <summary>Each tick touches a key chosen by the tick, so insertion order is scrambled.</summary>
            public void Tick(in TickContext ctx)
            {
                ulong key = TouchedKey(ctx.Tick);
                _balances.TryGetValue(key, out long old);
                Set(key, old + (long)ctx.Tick + 1);
                if (ctx.Tick % 5 == 4)
                {
                    Remove(TouchedKey(ctx.Tick / 2));
                }
            }

            public ulong ComputeStateHash()
            {
                ulong[] keys = new ulong[_balances.Count];
                _balances.Keys.CopyTo(keys, 0);
                Array.Sort(keys);
                var h = new StateHasher();
                h.Feed((ulong)keys.Length);
                foreach (ulong k in keys)
                {
                    h.Feed(k);
                    h.Feed(_balances[k]);
                }
                return h.Result;
            }

            public static ulong TouchedKey(ulong tick)
            {
                return unchecked(tick * 7919UL) % 97UL + (1UL << 40);
            }
        }

        /// <summary>
        /// An independent model of <see cref="SortedFeedSystem"/>'s state, kept in a
        /// SortedDictionary and hashed with <see cref="FnvOracle"/>.
        /// </summary>
        internal sealed class SortedFeedModel
        {
            private readonly SortedDictionary<ulong, long> _balances = new SortedDictionary<ulong, long>();

            public void RunTick(ulong tick)
            {
                ulong key = SortedFeedSystem.TouchedKey(tick);
                _balances.TryGetValue(key, out long old);
                _balances[key] = old + (long)tick + 1;
                if (tick % 5 == 4)
                {
                    _balances.Remove(SortedFeedSystem.TouchedKey(tick / 2));
                }
            }

            public ulong Hash()
            {
                var o = new FnvOracle();
                o.U64((ulong)_balances.Count);
                foreach (KeyValuePair<ulong, long> kv in _balances)
                {
                    o.U64(kv.Key);
                    o.I64(kv.Value);
                }
                return o.Result;
            }
        }

        /// <summary>A system whose hash is a fixed value, so the world-hash fold can be checked exactly.</summary>
        internal sealed class ConstantHashSystem : ISimSystem
        {
            private readonly ushort _position;
            private readonly ulong _hash;

            public ConstantHashSystem(ushort position, ulong hash)
            {
                _position = position;
                _hash = hash;
            }

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.constant";

            public void Tick(in TickContext ctx)
            {
            }

            public ulong ComputeStateHash()
            {
                return _hash;
            }
        }

        // ------------------------------------------------------------ host doubles
        // 08 §8.11a: sim.core publishes no null objects, so tests write their own.

        internal sealed class EmptyContent : IContentIndex
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

        internal sealed class SilentLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }

        internal sealed class Recorder : ICheckpointSink
        {
            public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

            public void Record(in Checkpoint cp)
            {
                Recorded.Add(cp);
            }
        }

        internal static ISimHost Host(ulong masterSeed, Recorder sink, params ISimSystem[] systems)
        {
            var config = new SimHostConfig(masterSeed, new EmptyContent(), sink, new SilentLog());
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            foreach (ISimSystem s in systems)
            {
                builder.Register(s);
            }
            return builder.Build();
        }

        internal static void AssertSameCheckpoints(IReadOnlyList<Checkpoint> a, IReadOnlyList<Checkpoint> b)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Tick, b[i].Tick);
                Assert.Equal(a[i].WorldHash, b[i].WorldHash);
                Assert.Equal(a[i].CoreHash, b[i].CoreHash);
                Assert.Equal(a[i].SystemHashes, b[i].SystemHashes);
            }
        }
    }
}
