// T-003 — sim.core — Fixed-point math type Fx.
//
// Implements spec/08-interfaces-core.md §8.3 (Q31.32 fixed point, including
// "C# shape and edge cases (Q-015)") subject to spec/02-determinism.md rule 4
// (no IEEE-754 types in sim state, ever) and the hand-rolled-128-bit mandate of
// §8.3: the 64x64->128 multiply, the 128-by-64 division and the bit-length
// count used by Sqrt's initial estimate are written here in plain integer
// code over uint64 halves. No Int128/UInt128, no Math.BigMul, no
// System.Numerics.BitOperations, no BigInteger (sim.core targets
// netstandard2.1, per spec/01-architecture.md D1). Every checked exception
// path below is an explicit condition evaluated before the corresponding
// unchecked arithmetic runs — never a `checked` context and never a BCL
// operator's own exception (spec/07-conventions.md "Error handling").
namespace AirportSim.Sim.Core
{
    using System;

    /// <summary>
    /// Q31.32 fixed-point number. spec/08-interfaces-core.md §8.3: value =
    /// Raw / 2^32. Never wraps, never saturates; every narrowing operation
    /// throws instead. Has no public constructor (Q-015); <see cref="FromRaw"/>
    /// is the only way in from a raw value, and <c>default(Fx)</c> is
    /// <see cref="Zero"/>.
    /// </summary>
    public readonly struct Fx : IEquatable<Fx>, IComparable<Fx>
    {
        private const int FractionalBits = 32; // Not a spec-named member (L5): an internal implementation detail, distinct from the public FX_FRACTIONAL_BITS constant §8.1 assigns to SimConstants.
        private const ulong SignBitMagnitude = 1UL << 63; // |long.MinValue|, as a literal, never computed by negating long.MinValue.
        private const ulong HalfRaw = 1UL << (FractionalBits - 1); // 0.5 in Q31.32.

        /// <summary>The raw Q31.32 value: the represented number times 2^32. spec/08-interfaces-core.md §8.3.</summary>
        public long Raw { get; }

        private Fx(long raw)
        {
            Raw = raw;
        }

        /// <summary>Exact construction from a raw Q31.32 value. Never throws. spec/08-interfaces-core.md §8.3.</summary>
        public static Fx FromRaw(long raw) => new Fx(raw);

        /// <summary>The value 0. spec/08-interfaces-core.md §8.3.</summary>
        public static readonly Fx Zero = new Fx(0);

        /// <summary>The value 1. spec/08-interfaces-core.md §8.3.</summary>
        public static readonly Fx One = FromInt(1);

        /// <summary>The smallest representable value (Raw = int64.MinValue). spec/08-interfaces-core.md §8.3.</summary>
        public static readonly Fx MinValue = new Fx(long.MinValue);

        /// <summary>The largest representable value (Raw = int64.MaxValue). spec/08-interfaces-core.md §8.3.</summary>
        public static readonly Fx MaxValue = new Fx(long.MaxValue);

        // --------------------------------------------------------------
        // Construction
        // --------------------------------------------------------------

        /// <summary>
        /// Constructs the integer <paramref name="v"/> exactly.
        /// spec/08-interfaces-core.md §8.3. Throws <see cref="OverflowException"/>
        /// unless -2^31 &lt;= v &lt;= 2^31 - 1.
        /// </summary>
        public static Fx FromInt(long v)
        {
            if (v > int.MaxValue || v < int.MinValue)
            {
                throw new OverflowException($"Fx.FromInt: value {v} does not fit Q31.32's integer range.");
            }

            return new Fx(v << FractionalBits);
        }

        /// <summary>
        /// Constructs numerator / denominator exactly, then floors toward
        /// negative infinity. spec/08-interfaces-core.md §8.3. Throws
        /// <see cref="DivideByZeroException"/> if denominator is 0, or
        /// <see cref="OverflowException"/> if the floored result is out of range.
        /// </summary>
        public static Fx FromRatio(long numerator, long denominator)
        {
            return DivCore(numerator, denominator, "FromRatio");
        }

        /// <summary>
        /// Parses a decimal literal matching exactly
        /// <c>-?(0|[1-9][0-9]*)(\.[0-9]{1,10})?</c>: no leading <c>+</c>, no
        /// whitespace, no exponent, no leading zero, 1 to 10 fraction digits
        /// when a fraction is present. The decimal value is taken exactly and
        /// then floored toward negative infinity, so <c>"-0.1"</c> is not the
        /// negation of <c>Parse("0.1")</c>; <c>"-0"</c> and <c>"-0.0"</c>
        /// parse to <see cref="Zero"/>. Content loading only
        /// (spec/08-interfaces-core.md §8.3, §8.11). Throws
        /// <see cref="ArgumentNullException"/> if <paramref name="value"/> is
        /// null, <see cref="FormatException"/> if it does not match the
        /// grammar, or <see cref="OverflowException"/> if the floored result
        /// is out of range.
        /// </summary>
        public static Fx Parse(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            int length = value.Length;
            int i = 0;
            bool negative = false;
            if (i < length && value[i] == '-')
            {
                negative = true;
                i++;
            }

            int intStart = i;
            if (i < length && value[i] == '0')
            {
                i++; // Lone "0"; a further digit here would be a leading zero, rejected below by the final position check.
            }
            else if (i < length && value[i] >= '1' && value[i] <= '9')
            {
                i++;
                while (i < length && value[i] >= '0' && value[i] <= '9')
                {
                    i++;
                }
            }
            else
            {
                throw new FormatException($"Fx.Parse: '{value}' does not match the required grammar.");
            }

            int intLen = i - intStart;
            long intPart = 0;
            for (int k = 0; k < intLen; k++)
            {
                int d = value[intStart + k] - '0';
                intPart = (intPart * 10) + d; // Safe unchecked: intPart is bounded below (int.MaxValue + 1) by the check on the next line at every step, far short of long's own range.
                if (intPart > (long)int.MaxValue + 1) // 2^31: the magnitude of int.MinValue, the most negative valid integer part; NarrowMagnitude below enforces the sign-aware bound exactly.
                {
                    throw new OverflowException($"Fx.Parse: integer part out of range in '{value}'.");
                }
            }

            bool hasFraction = false;
            ulong fracNumerator = 0;
            ulong fracDenominator = 1;

            if (i < length && value[i] == '.')
            {
                i++;
                int fracStart = i;
                while (i < length && value[i] >= '0' && value[i] <= '9')
                {
                    i++;
                }

                int fracLen = i - fracStart;
                if (fracLen < 1 || fracLen > 10)
                {
                    throw new FormatException($"Fx.Parse: '{value}' does not match the required grammar.");
                }

                hasFraction = true;
                for (int k = 0; k < fracLen; k++)
                {
                    int d = value[fracStart + k] - '0';
                    fracNumerator = (fracNumerator * 10) + (uint)d;
                    fracDenominator *= 10;
                }
            }

            if (i != length)
            {
                throw new FormatException($"Fx.Parse: '{value}' does not match the required grammar.");
            }

            ulong fracRawFloor = 0;
            ulong fracRemainder = 0;
            if (hasFraction)
            {
                ulong hi = fracNumerator >> (64 - FractionalBits);
                ulong lo = fracNumerator << FractionalBits;
                if (!DivMod128(hi, lo, fracDenominator, out fracRawFloor, out fracRemainder))
                {
                    throw new OverflowException($"Fx.Parse: internal overflow computing the fraction of '{value}'.");
                }
            }

            // floor(a + b) == a + floor(b) for integer a, applied to the signed
            // exact value: the integer part contributes exactly, and only a
            // negative, inexact fraction pushes the floor one unit further
            // from zero (mirrors DivCore's remainder-aware adjustment).
            ulong combinedMagnitude = ((ulong)intPart << FractionalBits) + fracRawFloor;
            ulong adjustment = negative && fracRemainder != 0 ? 1UL : 0UL;
            ulong finalMagnitude = combinedMagnitude + adjustment;

            return new Fx(NarrowMagnitude(finalMagnitude, negative, "Parse"));
        }

        // --------------------------------------------------------------
        // Arithmetic
        // --------------------------------------------------------------

        /// <summary>Exact sum. spec/08-interfaces-core.md §8.3. Throws <see cref="OverflowException"/> if out of range.</summary>
        public static Fx Add(Fx a, Fx b)
        {
            long ar = a.Raw;
            long br = b.Raw;
            long result = unchecked(ar + br);
            bool overflow = ((ar ^ result) & (br ^ result)) < 0;
            if (overflow)
            {
                throw new OverflowException("Fx.Add: result overflow.");
            }

            return new Fx(result);
        }

        /// <summary>Exact difference. spec/08-interfaces-core.md §8.3. Throws <see cref="OverflowException"/> if out of range.</summary>
        public static Fx Sub(Fx a, Fx b)
        {
            long ar = a.Raw;
            long br = b.Raw;
            long result = unchecked(ar - br);
            bool overflow = ((ar ^ br) & (ar ^ result)) < 0;
            if (overflow)
            {
                throw new OverflowException("Fx.Sub: result overflow.");
            }

            return new Fx(result);
        }

        /// <summary>
        /// a * b, via a hand-rolled 64x64-&gt;128-bit multiply and a floor
        /// toward negative infinity on narrowing. spec/08-interfaces-core.md
        /// §8.3. Throws <see cref="OverflowException"/> if out of range.
        /// </summary>
        public static Fx Mul(Fx a, Fx b)
        {
            ulong aMag = MagnitudeOf(a.Raw, out bool aNeg);
            ulong bMag = MagnitudeOf(b.Raw, out bool bNeg);
            Mul128(aMag, bMag, out ulong hi, out ulong lo);
            bool negative = aNeg ^ bNeg;

            if (!TryReduceBy2PowFractionalBits(hi, lo, negative, out ulong mag))
            {
                throw new OverflowException("Fx.Mul: result overflow.");
            }

            return new Fx(NarrowMagnitude(mag, negative, "Mul"));
        }

        /// <summary>
        /// a / b, via a hand-rolled 128-by-64-bit divide and a floor toward
        /// negative infinity on narrowing. spec/08-interfaces-core.md §8.3.
        /// Throws <see cref="DivideByZeroException"/> if b is 0, or
        /// <see cref="OverflowException"/> if out of range. Division by zero
        /// and overflow are checked explicitly before any BCL operator runs.
        /// </summary>
        public static Fx Div(Fx a, Fx b)
        {
            return DivCore(a.Raw, b.Raw, "Div");
        }

        /// <summary>Exact negation. spec/08-interfaces-core.md §8.3. Throws <see cref="OverflowException"/> for <see cref="MinValue"/>.</summary>
        public static Fx Neg(Fx a)
        {
            if (a.Raw == long.MinValue)
            {
                throw new OverflowException("Fx.Neg: MinValue has no representable negation.");
            }

            return new Fx(-a.Raw);
        }

        /// <summary>Exact absolute value. spec/08-interfaces-core.md §8.3. Throws <see cref="OverflowException"/> for <see cref="MinValue"/>.</summary>
        public static Fx Abs(Fx a)
        {
            if (a.Raw == long.MinValue)
            {
                throw new OverflowException("Fx.Abs: MinValue has no representable absolute value.");
            }

            return new Fx(a.Raw < 0 ? -a.Raw : a.Raw);
        }

        /// <summary>The lesser of a and b. spec/08-interfaces-core.md §8.3. Never throws.</summary>
        public static Fx Min(Fx a, Fx b) => a.Raw <= b.Raw ? a : b;

        /// <summary>The greater of a and b. spec/08-interfaces-core.md §8.3. Never throws.</summary>
        public static Fx Max(Fx a, Fx b) => a.Raw >= b.Raw ? a : b;

        /// <summary>
        /// Max(lo, Min(x, hi)). spec/08-interfaces-core.md §8.3. Throws
        /// <see cref="ArgumentException"/> if lo &gt; hi.
        /// </summary>
        public static Fx Clamp(Fx x, Fx lo, Fx hi)
        {
            if (lo.Raw > hi.Raw)
            {
                throw new ArgumentException("Fx.Clamp: lo must be <= hi.");
            }

            return Max(lo, Min(x, hi));
        }

        // --------------------------------------------------------------
        // Narrowing conversions — all floor toward negative infinity except
        // RoundHalfUp, the one explicit half-up path.
        // --------------------------------------------------------------

        /// <summary>Floor(x) as an int64. spec/08-interfaces-core.md §8.3. Never throws.</summary>
        public static long Floor(Fx x) => x.Raw >> FractionalBits; // Arithmetic shift = floor toward -infinity.

        /// <summary>Ceil(x) as an int64. spec/08-interfaces-core.md §8.3. Never throws.</summary>
        public static long Ceil(Fx x)
        {
            long floor = Floor(x);
            bool hasFraction = (x.Raw & 0xFFFFFFFFL) != 0;
            return hasFraction ? floor + 1 : floor; // floor is bounded to the type's ~2^31 integer range; +1 never overflows int64.
        }

        /// <summary>
        /// Floor(x + 1/2) as an int64, ties toward positive infinity
        /// (-0.5 -&gt; 0, -1.5 -&gt; -1, 2.5 -&gt; 3). The one explicit half-up
        /// path; every other narrowing operation floors toward negative
        /// infinity. spec/08-interfaces-core.md §8.3. Never throws, MaxValue included.
        /// </summary>
        public static long RoundHalfUp(Fx x)
        {
            long floor = Floor(x);
            ulong fracRaw = (ulong)x.Raw & 0xFFFFFFFFUL;
            return fracRaw >= HalfRaw ? floor + 1 : floor; // Compares the fraction directly; never adds Half to Raw, which could overflow near MaxValue.
        }

        /// <summary>x - Floor(x), always in [0, 1). spec/08-interfaces-core.md §8.3. Never throws.</summary>
        public static Fx Frac(Fx x) => new Fx((long)((ulong)x.Raw & 0xFFFFFFFFUL));

        /// <summary>
        /// The integer square root of the 96-bit product: Raw =
        /// floor(sqrt(x.Raw * 2^32)) exactly, via integer Newton's method over
        /// the hand-rolled 128-bit primitives (no numerically-approximate
        /// IEEE-754 Newton iteration). spec/08-interfaces-core.md §8.3. Throws
        /// <see cref="ArgumentOutOfRangeException"/> if x &lt; 0.
        /// </summary>
        public static Fx Sqrt(Fx x)
        {
            if (x.Raw < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "Fx.Sqrt: argument must be non-negative.");
            }

            if (x.Raw == 0)
            {
                return Zero;
            }

            ulong value = (ulong)x.Raw;

            // Radicand R = value * 2^32, as an explicit 128-bit pair: Sqrt(x)
            // in Q31.32 is exactly floor(sqrt(Raw * 2^32)), since
            // sqrt(Raw / 2^32) * 2^32 = sqrt(Raw * 2^32).
            ulong radHi = value >> (64 - FractionalBits);
            ulong radLo = value << FractionalBits;

            int bitLength = radHi != 0 ? 64 + BitLength(radHi) : BitLength(radLo);
            int initialBits = (bitLength + 1) / 2; // ceil(bitLength / 2); guarantees y0 >= sqrt(R).
            ulong y = initialBits >= 64 ? ulong.MaxValue : (1UL << initialBits);

            while (true)
            {
                if (!DivMod128(radHi, radLo, y, out ulong q, out _))
                {
                    // y starts >= sqrt(R) by construction, so R / y <= y always
                    // fits in 64 bits; reaching here means a broken invariant.
                    throw new InvalidOperationException("Fx.Sqrt: internal invariant broken computing R / y.");
                }

                ulong yNext = (y & q) + ((y ^ q) >> 1); // Overflow-free unsigned average.
                if (yNext >= y)
                {
                    break;
                }

                y = yNext;
            }

            return new Fx((long)y);
        }

        /// <summary>
        /// Renders x floored to a multiple of 10^-decimals, in invariant
        /// ASCII: an optional '-' (omitted for a zero result), the integer
        /// digits with no leading zero ("0" when the integer part is zero),
        /// then, when decimals &gt; 0, a '.' and exactly decimals digits.
        /// Presentation and logs only — never read back by sim code, and
        /// never uses IEEE-754 types internally. spec/08-interfaces-core.md
        /// §8.3. Throws <see cref="ArgumentOutOfRangeException"/> unless
        /// 0 &lt;= decimals &lt;= 10.
        /// </summary>
        public string ToDisplayString(int decimals)
        {
            if (decimals < 0 || decimals > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(decimals), "Fx.ToDisplayString: decimals must be in [0, 10].");
            }

            ulong magRaw = MagnitudeOf(Raw, out bool negative);

            ulong pow = 1;
            for (int k = 0; k < decimals; k++)
            {
                pow *= 10UL; // decimals <= 10, so pow <= 10^10, far below ulong.MaxValue: never overflows.
            }

            Mul128(magRaw, pow, out ulong prodHi, out ulong prodLo);

            if (!TryReduceBy2PowFractionalBitsWide(prodHi, prodLo, negative, out ulong redHi, out ulong redLo))
            {
                throw new InvalidOperationException("Fx.ToDisplayString: internal invariant broken reducing the scaled product.");
            }

            System.Collections.Generic.List<char> digits = new System.Collections.Generic.List<char>(24);
            if (redHi == 0 && redLo == 0)
            {
                digits.Add('0');
            }
            else
            {
                ulong hi = redHi;
                ulong lo = redLo;
                while (hi != 0 || lo != 0)
                {
                    uint digit = DivModSmall(ref hi, ref lo, 10);
                    digits.Add((char)('0' + digit));
                }
            }

            while (digits.Count < decimals + 1)
            {
                digits.Add('0');
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(digits.Count + 2);
            if (negative)
            {
                sb.Append('-');
            }

            for (int k = digits.Count - 1; k >= decimals; k--)
            {
                sb.Append(digits[k]);
            }

            if (decimals > 0)
            {
                sb.Append('.');
                for (int k = decimals - 1; k >= 0; k--)
                {
                    sb.Append(digits[k]);
                }
            }

            return sb.ToString();
        }

        // --------------------------------------------------------------
        // Operators — each exactly its static method (Q-015).
        // --------------------------------------------------------------

        /// <summary>Same as <see cref="Add"/>.</summary>
        public static Fx operator +(Fx a, Fx b) => Add(a, b);

        /// <summary>Same as <see cref="Sub"/>.</summary>
        public static Fx operator -(Fx a, Fx b) => Sub(a, b);

        /// <summary>Same as <see cref="Mul"/>.</summary>
        public static Fx operator *(Fx a, Fx b) => Mul(a, b);

        /// <summary>Same as <see cref="Div"/>.</summary>
        public static Fx operator /(Fx a, Fx b) => Div(a, b);

        /// <summary>Same as <see cref="Neg"/>.</summary>
        public static Fx operator -(Fx a) => Neg(a);

        /// <summary>Compares Raw for equality. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator ==(Fx a, Fx b) => a.Raw == b.Raw;

        /// <summary>Compares Raw for inequality. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator !=(Fx a, Fx b) => a.Raw != b.Raw;

        /// <summary>Compares Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator <(Fx a, Fx b) => a.Raw < b.Raw;

        /// <summary>Compares Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator <=(Fx a, Fx b) => a.Raw <= b.Raw;

        /// <summary>Compares Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator >(Fx a, Fx b) => a.Raw > b.Raw;

        /// <summary>Compares Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public static bool operator >=(Fx a, Fx b) => a.Raw >= b.Raw;

        // --------------------------------------------------------------
        // Equality, ordering, diagnostics — all agree with Raw (Q-015).
        // --------------------------------------------------------------

        /// <summary>Equality by Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public bool Equals(Fx other) => Raw == other.Raw;

        /// <summary>Equality by Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public override bool Equals(object? obj) => obj is Fx other && Equals(other);

        /// <summary>
        /// Hash of Raw. Never used for sim behaviour, ordering or hashing of
        /// sim state (spec/07-conventions.md "Runtime portability" rule 2);
        /// this override exists only for BCL collection interop.
        /// </summary>
        public override int GetHashCode() => Raw.GetHashCode();

        /// <summary>Ordering by Raw. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public int CompareTo(Fx other) => Raw.CompareTo(other.Raw);

        /// <summary>ToDisplayString(10), for diagnostics only. spec/08-interfaces-core.md §8.3 (Q-015).</summary>
        public override string ToString() => ToDisplayString(10);

        // --------------------------------------------------------------
        // Shared division/multiplication core
        // --------------------------------------------------------------

        private static Fx DivCore(long numeratorRaw, long denominatorRaw, string opName)
        {
            if (denominatorRaw == 0)
            {
                throw new DivideByZeroException($"Fx.{opName}: division by zero.");
            }

            ulong numMag = MagnitudeOf(numeratorRaw, out bool numNeg);
            ulong denMag = MagnitudeOf(denominatorRaw, out bool denNeg);
            bool negative = numNeg ^ denNeg;

            ulong hi = numMag >> (64 - FractionalBits);
            ulong lo = numMag << FractionalBits;

            if (!DivMod128(hi, lo, denMag, out ulong quotient, out ulong remainder))
            {
                throw new OverflowException($"Fx.{opName}: result overflow.");
            }

            ulong mag = quotient;
            if (negative && remainder != 0)
            {
                if (mag == ulong.MaxValue)
                {
                    throw new OverflowException($"Fx.{opName}: result overflow.");
                }

                mag += 1;
            }

            return new Fx(NarrowMagnitude(mag, negative, opName));
        }

        /// <summary>
        /// Reduces an exact 128-bit product by 2^FractionalBits, floors the
        /// signed result toward negative infinity, and requires the reduced
        /// magnitude to fit in 64 bits (the Mul narrowing case). False on overflow.
        /// </summary>
        private static bool TryReduceBy2PowFractionalBits(ulong hi, ulong lo, bool negative, out ulong magnitude)
        {
            ulong shiftedHi = hi >> FractionalBits;
            if (shiftedHi != 0)
            {
                magnitude = 0;
                return false;
            }

            ulong shiftedLo = (lo >> FractionalBits) | (hi << (64 - FractionalBits));
            bool exact = (lo & 0xFFFFFFFFUL) == 0;

            if (negative && !exact)
            {
                if (shiftedLo == ulong.MaxValue)
                {
                    magnitude = 0;
                    return false;
                }

                shiftedLo += 1;
            }

            magnitude = shiftedLo;
            return true;
        }

        /// <summary>
        /// As <see cref="TryReduceBy2PowFractionalBits"/>, but keeps the full
        /// (possibly &gt;64-bit) magnitude, for ToDisplayString's scaled
        /// display integer rather than an Fx.Raw.
        /// </summary>
        private static bool TryReduceBy2PowFractionalBitsWide(
            ulong hi, ulong lo, bool negative, out ulong resultHi, out ulong resultLo)
        {
            ulong shiftedHi = hi >> FractionalBits;
            ulong shiftedLo = (lo >> FractionalBits) | (hi << (64 - FractionalBits));
            bool exact = (lo & 0xFFFFFFFFUL) == 0;

            if (negative && !exact)
            {
                if (shiftedLo == ulong.MaxValue)
                {
                    if (shiftedHi == ulong.MaxValue)
                    {
                        resultHi = 0;
                        resultLo = 0;
                        return false;
                    }

                    shiftedLo = 0;
                    shiftedHi += 1;
                }
                else
                {
                    shiftedLo += 1;
                }
            }

            resultHi = shiftedHi;
            resultLo = shiftedLo;
            return true;
        }

        private static long NarrowMagnitude(ulong magnitude, bool negative, string opName)
        {
            if (!negative)
            {
                if (magnitude > long.MaxValue)
                {
                    throw new OverflowException($"Fx.{opName}: result overflow.");
                }

                return (long)magnitude;
            }

            if (magnitude > SignBitMagnitude)
            {
                throw new OverflowException($"Fx.{opName}: result overflow.");
            }

            if (magnitude == SignBitMagnitude)
            {
                return long.MinValue;
            }

            return -(long)magnitude;
        }

        private static ulong MagnitudeOf(long v, out bool negative)
        {
            if (v < 0)
            {
                negative = true;
                return v == long.MinValue ? SignBitMagnitude : (ulong)(-v);
            }

            negative = false;
            return (ulong)v;
        }

        // --------------------------------------------------------------
        // Hand-rolled 128-bit primitives (spec/08-interfaces-core.md §8.3,
        // "The 128-bit arithmetic is hand-rolled"). No Int128/UInt128,
        // Math.BigMul or BitOperations; plain integer code over uint64 halves.
        // --------------------------------------------------------------

        /// <summary>Unsigned 64x64-&gt;128 multiply: (hi, lo) = a * b.</summary>
        private static void Mul128(ulong a, ulong b, out ulong hi, out ulong lo)
        {
            ulong aLo = (uint)a;
            ulong aHi = a >> 32;
            ulong bLo = (uint)b;
            ulong bHi = b >> 32;

            ulong loLo = aLo * bLo;
            ulong hiLo = aHi * bLo;
            ulong loHi = aLo * bHi;
            ulong hiHi = aHi * bHi;

            ulong cross = hiLo + (loLo >> 32) + (uint)loHi;
            lo = (cross << 32) | (uint)loLo;
            hi = hiHi + (cross >> 32) + (loHi >> 32);
        }

        /// <summary>
        /// Unsigned 128-by-64 divide: (hi, lo) / divisor -&gt; (quotient,
        /// remainder). False if the true quotient does not fit in 64 bits.
        /// divisor must be nonzero (checked by every caller before this is
        /// reached). Binary shift-subtract long division: a fixed
        /// 128-iteration loop, no allocation, no hardware 128-bit divide
        /// (none exists on netstandard2.1).
        /// </summary>
        private static bool DivMod128(ulong hi, ulong lo, ulong divisor, out ulong quotient, out ulong remainder)
        {
            ulong rem = 0;
            ulong quo = 0;
            bool overflow = false;

            for (int i = 127; i >= 0; i--)
            {
                bool carry = (rem & 0x8000000000000000UL) != 0;
                ulong bit = i >= 64 ? (hi >> (i - 64)) & 1UL : (lo >> i) & 1UL;
                rem = unchecked((rem << 1) | bit);

                if (carry || rem >= divisor)
                {
                    rem = unchecked(rem - divisor);
                    if (i < 64)
                    {
                        quo |= 1UL << i;
                    }
                    else
                    {
                        overflow = true;
                    }
                }
            }

            quotient = quo;
            remainder = rem;
            return !overflow;
        }

        /// <summary>
        /// Divides the unsigned 128-bit (hi, lo) pair in place by a small
        /// (&lt;= 2^32-1) divisor, e.g. 10 for decimal digit extraction, using
        /// only native 64-bit unsigned division/modulo by a nonzero constant
        /// (no hand-rolled 128-bit step needed: each intermediate value is
        /// bounded by divisor * 2^32, which always fits a ulong here).
        /// </summary>
        private static uint DivModSmall(ref ulong hi, ref ulong lo, uint divisor)
        {
            ulong qHi = hi / divisor;
            ulong rem = hi % divisor;

            ulong loHi = lo >> 32;
            ulong t = (rem << 32) | loHi;
            ulong qLoHi = t / divisor;
            rem = t % divisor;

            ulong loLo = (uint)lo;
            t = (rem << 32) | loLo;
            ulong qLoLo = t / divisor;
            rem = t % divisor;

            hi = qHi;
            lo = (qLoHi << 32) | qLoLo;
            return (uint)rem;
        }

        /// <summary>Bit length of a nonzero uint64 (0 for v == 0). Hand-rolled: no BitOperations on netstandard2.1.</summary>
        private static int BitLength(ulong v)
        {
            int n = 0;
            while (v != 0)
            {
                v >>= 1;
                n++;
            }

            return n;
        }
    }
}
