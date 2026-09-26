using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Test-only helpers for the T-002 RNG tests. Everything here is written from
    /// 08-interfaces-core.md §8.8 "Exact reference" (Q-019) and never from an
    /// implementation. All types are nested so that they cannot collide with
    /// helpers other test branches add to this shared project (07 L9).
    /// </summary>
    internal static class RngTestSupport
    {
        /// <summary>
        /// SplitMix64 exactly as pinned in §8.8. Used both as the input generator
        /// for property loops (07 L4) and inside the reference stream seeding.
        /// </summary>
        internal sealed class RngGen
        {
            private ulong _x;

            public RngGen(ulong seed)
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

            /// <summary>Uniform-enough index in [0, n) for picking test inputs. Not the sim's NextInt.</summary>
            public int Below(int n)
            {
                return (int)(Next() % (ulong)n);
            }
        }

        internal const ulong FnvOffset = 0xCBF29CE484222325UL;
        internal const ulong FnvPrime = 0x100000001B3UL;

        /// <summary>Plain FNV-1a-64 over bytes, no length prefix (§8.8 stream seed, §8.9 constants).</summary>
        internal static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
        {
            ulong h = FnvOffset;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    h ^= bytes[i];
                    h *= FnvPrime;
                }
            }
            return h;
        }

        /// <summary>FNV-1a-64 over each value as 8 little-endian bytes (§8.9 Feed(uint64)).</summary>
        internal static ulong FnvOfUInt64s(params ulong[] values)
        {
            ulong h = FnvOffset;
            unchecked
            {
                foreach (ulong v in values)
                {
                    for (int b = 0; b < 8; b++)
                    {
                        h ^= (byte)(v >> (8 * b));
                        h *= FnvPrime;
                    }
                }
            }
            return h;
        }

        /// <summary>
        /// Reference model of one stream, transcribed from §8.8 "Exact reference".
        /// It is the oracle for property loops; its correctness is pinned by the
        /// spec's golden vectors, which the golden tests re-check against it.
        /// </summary>
        internal sealed class ReferenceStream
        {
            private ulong _s0, _s1, _s2, _s3;

            /// <summary>Number of Lemire rejections so far, so a test can prove the rejection path ran.</summary>
            public int Rejections { get; private set; }

            public ReferenceStream(ulong masterSeed, string name)
            {
                ulong seed = masterSeed ^ Fnv1a64(Encoding.UTF8.GetBytes(name));
                var sm = new RngGen(seed);
                _s0 = sm.Next();
                _s1 = sm.Next();
                _s2 = sm.Next();
                _s3 = sm.Next();
                while (_s0 == 0 && _s1 == 0 && _s2 == 0 && _s3 == 0)
                {
                    _s0 = sm.Next();
                }
            }

            private static ulong Rotl(ulong x, int k)
            {
                return (x << k) | (x >> (64 - k));
            }

            public ulong NextUInt64()
            {
                unchecked
                {
                    ulong r = Rotl(_s1 * 5, 7) * 9;
                    ulong t = _s1 << 17;
                    _s2 ^= _s0;
                    _s3 ^= _s1;
                    _s1 ^= _s2;
                    _s0 ^= _s3;
                    _s2 ^= t;
                    _s3 = Rotl(_s3, 45);
                    return r;
                }
            }

            public int NextInt(int min, int max)
            {
                if (min >= max)
                {
                    throw new ArgumentOutOfRangeException(nameof(min));
                }
                unchecked
                {
                    uint range = (uint)(max - min);
                    uint x = (uint)(NextUInt64() >> 32);
                    ulong m = (ulong)x * range;
                    uint l = (uint)m;
                    if (l < range)
                    {
                        uint t = (0u - range) % range;
                        while (l < t)
                        {
                            Rejections++;
                            x = (uint)(NextUInt64() >> 32);
                            m = (ulong)x * range;
                            l = (uint)m;
                        }
                    }
                    return min + (int)(m >> 32);
                }
            }

            public long NextFx01Raw()
            {
                return (long)(NextUInt64() >> 32);
            }

            public bool Chance(long probabilityRaw)
            {
                return NextFx01Raw() < probabilityRaw;
            }

            public void Shuffle<T>(Span<T> items)
            {
                for (int i = items.Length - 1; i >= 1; i--)
                {
                    int j = NextInt(0, i + 1);
                    T tmp = items[i];
                    items[i] = items[j];
                    items[j] = tmp;
                }
            }

            public ulong ComputeStateHash()
            {
                return FnvOfUInt64s(_s0, _s1, _s2, _s3);
            }
        }

        /// <summary>Stream names used by the property loops. All match §8.8's name grammar.</summary>
        internal static readonly string[] Names =
        {
            "sim.flow.showup",
            "sim.schedule.jitter",
            "sim.airside.taxi",
            "sim.core.a",
            "sim.turnaround.job_order",
            "sim.delay.x9",
            "sim.baggage.belt_0",
            "sim.q.0",
        };

        internal static string At(ulong seed, int iteration)
        {
            return "seed 0x" + seed.ToString("X16", CultureInfo.InvariantCulture)
                + ", iteration " + iteration.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Hex(ulong v)
        {
            return "0x" + v.ToString("X16", CultureInfo.InvariantCulture);
        }

        internal static IRandomStream FreshStream(ulong masterSeed, string name)
        {
            IRandomService service = RandomServiceFactory.Create(masterSeed);
            return service.Stream(new RngStreamName(name));
        }

        // ------------------------------------------------------------ host doubles
        // 08 §8.11a: sim.core publishes no null objects, so tests write their own.

        internal sealed class RngEmptyContent : IContentIndex
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

        internal sealed class RngNullLog : ISimLog
        {
            public void Write(ulong tick, LogLevel level, SystemId system, LogKey key, in LogArgs args)
            {
            }
        }

        internal sealed class CheckpointRecorder : ICheckpointSink
        {
            public readonly List<Checkpoint> Recorded = new List<Checkpoint>();

            public void Record(in Checkpoint cp)
            {
                Recorded.Add(cp);
            }
        }

        /// <summary>
        /// A probe system at a registry position whose module is absent from
        /// core tests (08 §8.5 "Registration rules"). Each tick it fetches its
        /// stream from ctx.Rng by name and draws <c>drawsPerTick</c> NextInt
        /// values. Its state hash is the stream's hash, as §8.8 prescribes for
        /// an owning system.
        /// </summary>
        internal sealed class RngProbe : ISimSystem
        {
            private readonly ushort _position;
            private readonly RngStreamName _name;
            private readonly int _drawsPerTick;
            private IRandomStream? _stream;

            public RngProbe(ushort position, string streamName, int drawsPerTick)
            {
                _position = position;
                _name = new RngStreamName(streamName);
                _drawsPerTick = drawsPerTick;
            }

            public bool Ticked { get; private set; }
            public ulong ObservedMasterSeed { get; private set; }
            public ulong FirstTickFirstDraw { get; private set; }
            public long LastTick { get; private set; } = -1;

            public SystemId Id => new SystemId(_position);

            public string Name => "probe.rng";

            public void Tick(in TickContext ctx)
            {
                IRandomStream stream = ctx.Rng.Stream(_name);
                if (!Ticked)
                {
                    Ticked = true;
                    ObservedMasterSeed = ctx.Rng.MasterSeed;
                    FirstTickFirstDraw = stream.NextUInt64();
                    for (int i = 1; i < _drawsPerTick; i++)
                    {
                        stream.NextInt(0, 1000);
                    }
                }
                else
                {
                    for (int i = 0; i < _drawsPerTick; i++)
                    {
                        stream.NextInt(0, 1000);
                    }
                }
                _stream = stream;
                LastTick = (long)ctx.Tick;
            }

            public ulong ComputeStateHash()
            {
                return _stream == null ? 0UL : _stream.ComputeStateHash();
            }
        }

        internal static ISimHost BuildHost(ulong masterSeed, CheckpointRecorder sink, params ISimSystem[] systems)
        {
            var config = new SimHostConfig(masterSeed, new RngEmptyContent(), sink, new RngNullLog());
            ISimHostBuilder builder = SimHostFactory.CreateBuilder(in config);
            foreach (ISimSystem s in systems)
            {
                builder.Register(s);
            }
            return builder.Build();
        }

        internal static void AssertCheckpointsEqual(IReadOnlyList<Checkpoint> a, IReadOnlyList<Checkpoint> b)
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
