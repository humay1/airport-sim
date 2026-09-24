using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>Expected outcome of an Fx operation, per 08 §8.3 "C# shape and edge cases".</summary>
    internal enum FxOutcome : byte
    {
        Ok = 0,
        Overflow = 1,
        DivideByZero = 2,
        ArgumentOutOfRange = 3,
    }

    /// <summary>
    /// Independent reference model of Fx, written from 08-interfaces-core.md §8.3
    /// only. It uses BigInteger, which is permitted in test projects as an oracle
    /// (08 §8.3) and never reaches src/. Every value is the exact rational
    /// Raw / 2^32, and every narrowing floors toward negative infinity.
    /// </summary>
    internal static class FxOracle
    {
        public static readonly BigInteger Two32 = BigInteger.One << 32;

        public static BigInteger FloorDiv(BigInteger n, BigInteger d)
        {
            BigInteger q = BigInteger.DivRem(n, d, out BigInteger r);
            if (!r.IsZero && ((r.Sign < 0) != (d.Sign < 0)))
            {
                q -= 1;
            }
            return q;
        }

        public static bool InRange(BigInteger v) => v >= long.MinValue && v <= long.MaxValue;

        private static (FxOutcome, long) Narrow(BigInteger v) =>
            InRange(v) ? (FxOutcome.Ok, (long)v) : (FxOutcome.Overflow, 0L);

        public static (FxOutcome Outcome, long Raw) FromInt(long v) =>
            v >= int.MinValue && v <= int.MaxValue ? (FxOutcome.Ok, v << 32) : (FxOutcome.Overflow, 0L);

        public static (FxOutcome Outcome, long Raw) FromRatio(long n, long d) =>
            d == 0 ? (FxOutcome.DivideByZero, 0L) : Narrow(FloorDiv((BigInteger)n * Two32, d));

        public static (FxOutcome Outcome, long Raw) Add(long a, long b) => Narrow((BigInteger)a + b);

        public static (FxOutcome Outcome, long Raw) Sub(long a, long b) => Narrow((BigInteger)a - b);

        public static (FxOutcome Outcome, long Raw) Mul(long a, long b) =>
            Narrow(FloorDiv((BigInteger)a * b, Two32));

        public static (FxOutcome Outcome, long Raw) Div(long a, long b) =>
            b == 0 ? (FxOutcome.DivideByZero, 0L) : Narrow(FloorDiv((BigInteger)a * Two32, b));

        public static (FxOutcome Outcome, long Raw) Neg(long a) => Narrow(-(BigInteger)a);

        public static (FxOutcome Outcome, long Raw) Abs(long a) => Narrow(BigInteger.Abs(a));

        public static long Floor(long a) => (long)FloorDiv(a, Two32);

        public static long Ceil(long a) => (long)(-FloorDiv(-(BigInteger)a, Two32));

        public static long RoundHalfUp(long a) => (long)FloorDiv((BigInteger)a + (Two32 / 2), Two32);

        public static long Frac(long a) => (long)((BigInteger)a - FloorDiv(a, Two32) * Two32);

        public static (FxOutcome Outcome, long Raw) Sqrt(long a) =>
            a < 0 ? (FxOutcome.ArgumentOutOfRange, 0L) : Narrow(ISqrt((BigInteger)a * Two32));

        public static BigInteger ISqrt(BigInteger n)
        {
            if (n.IsZero)
            {
                return BigInteger.Zero;
            }
            BigInteger x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
            while (true)
            {
                BigInteger y = (x + n / x) >> 1;
                if (y >= x)
                {
                    return x;
                }
                x = y;
            }
        }

        /// <summary>Parse per the 08 §8.3 grammar. Returns null outcome text on FormatException.</summary>
        public static (FxOutcome Outcome, long Raw, bool FormatError) Parse(string s)
        {
            int i = 0;
            bool negative = false;
            if (i < s.Length && s[i] == '-')
            {
                negative = true;
                i++;
            }
            int intStart = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                i++;
            }
            int intLen = i - intStart;
            if (intLen == 0 || (intLen > 1 && s[intStart] == '0'))
            {
                return (FxOutcome.Ok, 0, true);
            }
            BigInteger num = BigInteger.Parse(s.Substring(intStart, intLen), NumberStyles.None, CultureInfo.InvariantCulture);
            BigInteger den = BigInteger.One;
            if (i < s.Length)
            {
                if (s[i] != '.')
                {
                    return (FxOutcome.Ok, 0, true);
                }
                i++;
                int fracStart = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                {
                    i++;
                }
                int fracLen = i - fracStart;
                if (fracLen < 1 || fracLen > 10 || i != s.Length)
                {
                    return (FxOutcome.Ok, 0, true);
                }
                den = BigInteger.Pow(10, fracLen);
                num = num * den + BigInteger.Parse(s.Substring(fracStart, fracLen), NumberStyles.None, CultureInfo.InvariantCulture);
            }
            if (negative)
            {
                num = -num;
            }
            var (o, r) = Narrow(FloorDiv(num * Two32, den));
            return (o, r, false);
        }

        /// <summary>ToDisplayString per 08 §8.3: floor to a multiple of 10^-d, invariant ASCII.</summary>
        public static string Display(long raw, int d)
        {
            BigInteger scale = BigInteger.Pow(10, d);
            BigInteger m = FloorDiv((BigInteger)raw * scale, Two32);
            var sb = new StringBuilder();
            if (m.Sign < 0)
            {
                sb.Append('-');
                m = -m;
            }
            BigInteger ip = BigInteger.DivRem(m, scale, out BigInteger fp);
            sb.Append(ip.ToString(CultureInfo.InvariantCulture));
            if (d > 0)
            {
                sb.Append('.');
                sb.Append(fp.ToString(CultureInfo.InvariantCulture).PadLeft(d, '0'));
            }
            return sb.ToString();
        }

        private static readonly long[] Specials =
        {
            0L, 1L, -1L, long.MinValue, long.MaxValue, 1L << 32, -(1L << 32), 1L << 31,
            -(1L << 31), (1L << 32) + 1, long.MinValue + 1, long.MaxValue - 1,
        };

        /// <summary>
        /// A raw value spread over every magnitude: one draw in eight is a
        /// boundary special, the rest are a full 64-bit draw arithmetically
        /// shifted right by 0..63 bits.
        /// </summary>
        public static long NextRaw(SplitMix64 g)
        {
            ulong u = g.Next();
            ulong k = g.Next();
            if (k % 8 == 0)
            {
                return Specials[(int)((k >> 8) % (ulong)Specials.Length)];
            }
            return unchecked((long)u) >> (int)((k >> 8) % 64);
        }

        public const ulong FnvOffset = 0xcbf29ce484222325UL;
        public const ulong FnvPrime = 0x100000001b3UL;

        public static ulong FnvByte(ulong h, byte b)
        {
            unchecked
            {
                return (h ^ b) * FnvPrime;
            }
        }

        public static ulong FnvInt64(ulong h, long v)
        {
            ulong u = unchecked((ulong)v);
            for (int i = 0; i < 8; i++)
            {
                h = FnvByte(h, (byte)(u >> (8 * i)));
            }
            return h;
        }

        /// <summary>
        /// The seeded operation sequence behind the golden digest. Operation
        /// <paramref name="op"/> is applied to raw operands a and b.
        /// </summary>
        public static (FxOutcome Outcome, long Value) Apply(int op, long a, long b)
        {
            switch (op)
            {
                case 0: return Add(a, b);
                case 1: return Sub(a, b);
                case 2: return Mul(a, b);
                case 3: return Div(a, b);
                case 4: return Sqrt(a);
                case 5: return FromRatio(a, b);
                case 6: return (FxOutcome.Ok, Floor(a));
                case 7: return (FxOutcome.Ok, Ceil(a));
                case 8: return (FxOutcome.Ok, RoundHalfUp(a));
                case 9: return (FxOutcome.Ok, Frac(a));
                case 10: return Neg(a);
                case 11: return Abs(a);
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        public const int OpCount = 12;
    }
}
