using System;
using System.Text;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// The one <see cref="IRandomStream"/> implementation: xoshiro256** 1.0, seeded
    /// with SplitMix64 as pinned in 08-interfaces-core.md §8.8 "Exact reference"
    /// (Q-019, Q-023). Binding bit for bit; part of the save format. After
    /// construction, no member allocates.
    /// </summary>
    internal sealed class RandomStream : IRandomStream
    {
        private const ulong FnvOffsetBasis = 0xCBF29CE484222325UL;
        private const ulong FnvPrime = 0x100000001B3UL;

        private ulong _s0, _s1, _s2, _s3;

        internal RandomStream(ulong masterSeed, string name)
        {
            ulong seed = masterSeed ^ Fnv1a64Utf8(name);
            ulong x = seed;
            ulong s0 = SplitMix64Next(ref x);
            ulong s1 = SplitMix64Next(ref x);
            ulong s2 = SplitMix64Next(ref x);
            ulong s3 = SplitMix64Next(ref x);
            while (s0 == 0 && s1 == 0 && s2 == 0 && s3 == 0)
            {
                s0 = SplitMix64Next(ref x);
            }

            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
        }

        private static ulong Fnv1a64Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value); // Construction time only, not the per-draw hot path.
            ulong h = FnvOffsetBasis;
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    h = (h ^ bytes[i]) * FnvPrime;
                }
            }

            return h;
        }

        private static ulong SplitMix64Next(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static ulong Rotl(ulong x, int k) => unchecked((x << k) | (x >> (64 - k)));

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(minInclusive), "RandomStream.NextInt: minInclusive must be < maxExclusive.");
            }

            unchecked
            {
                uint range = (uint)(maxExclusive - minInclusive);
                uint x = (uint)(NextUInt64() >> 32);
                ulong m = (ulong)x * range;
                uint l = (uint)m;
                if (l < range)
                {
                    uint t = (0u - range) % range;
                    while (l < t)
                    {
                        x = (uint)(NextUInt64() >> 32);
                        m = (ulong)x * range;
                        l = (uint)m;
                    }
                }

                return minInclusive + (int)(m >> 32);
            }
        }

        /// <inheritdoc/>
        public Fx NextFx01()
        {
            return Fx.FromRaw(unchecked((long)(NextUInt64() >> 32)));
        }

        /// <inheritdoc/>
        public bool Chance(Fx probability)
        {
            return NextFx01() < probability;
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public ulong ComputeStateHash()
        {
            var hasher = new StateHasher();
            hasher.Feed(_s0);
            hasher.Feed(_s1);
            hasher.Feed(_s2);
            hasher.Feed(_s3);
            return hasher.Result;
        }
    }
}
