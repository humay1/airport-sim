using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Tests for the fixed-point type Fx (T-003), written from
    /// 02-determinism.md rule 4 and 08-interfaces-core.md §8.3 (as amended by
    /// Q-015) only. Every value below is read as the exact rational Raw / 2^32.
    /// The reference model is FxOracle (BigInteger) and, for Mul, Int128, both
    /// permitted in tests by 08 §8.3. Property-style tests loop over a seeded
    /// SplitMix64 sequence (07-conventions.md L4) and name the seed and the
    /// iteration on failure.
    /// </summary>
    public sealed class FxTests
    {
        private const long OneRaw = 1L << 32;
        private const long HalfRaw = 1L << 31;

        private static Fx R(long raw) => Fx.FromRaw(raw);

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Runs <paramref name="f"/> and classifies the result. Only the exact
        /// exception types 08 §8.3 names are recognised (07 "Error handling":
        /// tests assert the exact type); anything else propagates and fails
        /// the test.
        /// </summary>
        private static (FxOutcome Outcome, long Value) Run(Func<long> f)
        {
            try
            {
                return (FxOutcome.Ok, f());
            }
            catch (Exception e) when (e.GetType() == typeof(OverflowException))
            {
                return (FxOutcome.Overflow, 0L);
            }
            catch (Exception e) when (e.GetType() == typeof(DivideByZeroException))
            {
                return (FxOutcome.DivideByZero, 0L);
            }
            catch (Exception e) when (e.GetType() == typeof(ArgumentOutOfRangeException))
            {
                return (FxOutcome.ArgumentOutOfRange, 0L);
            }
        }

        private static string Describe((FxOutcome Outcome, long Value) r) =>
            r.Outcome switch
            {
                FxOutcome.Ok => "Raw " + r.Value.ToString(CultureInfo.InvariantCulture),
                FxOutcome.Overflow => "OverflowException",
                FxOutcome.DivideByZero => "DivideByZeroException",
                _ => "ArgumentOutOfRangeException",
            };

        private static void Expect(string what, (FxOutcome Outcome, long Value) expected, Func<long> actual)
        {
            (FxOutcome Outcome, long Value) got = Run(actual);
            if (got.Outcome != expected.Outcome || (got.Outcome == FxOutcome.Ok && got.Value != expected.Value))
            {
                Assert.Fail(what + ": expected " + Describe(expected) + ", got " + Describe(got));
            }
        }

        private static string At(ulong seed, int i) =>
            "seed 0x" + seed.ToString("X16", CultureInfo.InvariantCulture) + " iteration " + i.ToString(CultureInfo.InvariantCulture);

        private static string Raws(long a, long b) =>
            "(" + a.ToString(CultureInfo.InvariantCulture) + ", " + b.ToString(CultureInfo.InvariantCulture) + ")";

        private static (FxOutcome Outcome, long Value) ApplyFx(int op, long a, long b)
        {
            switch (op)
            {
                case 0: return Run(() => Fx.Add(R(a), R(b)).Raw);
                case 1: return Run(() => Fx.Sub(R(a), R(b)).Raw);
                case 2: return Run(() => Fx.Mul(R(a), R(b)).Raw);
                case 3: return Run(() => Fx.Div(R(a), R(b)).Raw);
                case 4: return Run(() => Fx.Sqrt(R(a)).Raw);
                case 5: return Run(() => Fx.FromRatio(a, b).Raw);
                case 6: return Run(() => Fx.Floor(R(a)));
                case 7: return Run(() => Fx.Ceil(R(a)));
                case 8: return Run(() => Fx.RoundHalfUp(R(a)));
                case 9: return Run(() => Fx.Frac(R(a)).Raw);
                case 10: return Run(() => Fx.Neg(R(a)).Raw);
                case 11: return Run(() => Fx.Abs(R(a)).Raw);
                default: throw new ArgumentOutOfRangeException(nameof(op));
            }
        }

        private static void CheckBinaryGolden(
            string name,
            (long A, long B, FxOutcome Outcome, long Expected)[] rows,
            Func<long, long, (FxOutcome, long)> oracle,
            Func<long, long, long> actual)
        {
            foreach ((long a, long b, FxOutcome outcome, long expected) in rows)
            {
                (FxOutcome, long) model = oracle(a, b);
                if (model != (outcome, outcome == FxOutcome.Ok ? expected : 0L))
                {
                    Assert.Fail("golden literal for " + name + Raws(a, b) + " disagrees with the reference oracle");
                }
                Expect(name + Raws(a, b), (outcome, expected), () => actual(a, b));
            }
        }

        private static string RepoRoot()
        {
            DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null
                   && !(File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))
                        && Directory.Exists(Path.Combine(d.FullName, "spec"))))
            {
                d = d.Parent;
            }
            Assert.True(d != null, "repository root (CLAUDE.md + spec/) not found above " + AppContext.BaseDirectory);
            return d!.FullName;
        }

        private static string StripComments(string source)
        {
            string noBlock = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            return Regex.Replace(noBlock, @"//[^\n]*", " ");
        }

        // -------------------------------------------------------- conformance

        [Fact]
        public void test_fx_type_is_readonly_value_type_of_eight_bytes()
        {
            // 08 §8.3: struct Fx { int64 Raw }, a public readonly struct (Q-015).
            Type t = typeof(Fx);
            Assert.True(t.IsValueType, "Fx must be a value type");
            Assert.True(t.IsPublic, "Fx must be public");
            Assert.Contains(t.GetCustomAttributes(false), a => a.GetType().Name == "IsReadOnlyAttribute");
            Assert.Equal(8, Unsafe.SizeOf<Fx>());
            FieldInfo[] instanceFields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo only = Assert.Single(instanceFields);
            Assert.Equal(typeof(long), only.FieldType);
        }

        [Fact]
        public void test_fx_type_implements_iequatable_and_icomparable_of_fx()
        {
            // 08 §8.3 Q-015: readonly struct Fx : IEquatable<Fx>, IComparable<Fx>.
            Assert.True(typeof(IEquatable<Fx>).IsAssignableFrom(typeof(Fx)));
            Assert.True(typeof(IComparable<Fx>).IsAssignableFrom(typeof(Fx)));
        }

        [Fact]
        public void test_fx_type_has_no_public_constructor()
        {
            // 08 §8.3 Q-015: FromRaw is the only way in from a raw value.
            Assert.Empty(typeof(Fx).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }

        [Fact]
        public void test_fx_type_has_no_conversion_operators()
        {
            // 08 §8.3 Q-015: no implicit or explicit conversion operators.
            MethodInfo[] methods = typeof(Fx).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.DoesNotContain(methods, m => m.Name == "op_Implicit" || m.Name == "op_Explicit");
        }

        [Fact]
        public void test_fx_public_surface_is_exactly_the_specified_members()
        {
            // 07 L5: public iff the spec names it. 08 §8.3 lists the operations,
            // Q-015 adds FromRaw, the operators, Equals/GetHashCode/CompareTo/ToString
            // and the Raw property. Trigonometry, exp, log, Lerp are deliberately absent.
            var expected = new SortedSet<string>(StringComparer.Ordinal)
            {
                "FromInt", "FromRaw", "FromRatio", "Parse",
                "Zero", "One", "MinValue", "MaxValue",
                "Add", "Sub", "Mul", "Div", "Neg",
                "Abs", "Min", "Max", "Clamp",
                "Floor", "Ceil", "RoundHalfUp", "Frac", "Sqrt",
                "ToDisplayString",
                "Raw", "get_Raw",
                "op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_UnaryNegation",
                "op_Equality", "op_Inequality", "op_LessThan", "op_LessThanOrEqual",
                "op_GreaterThan", "op_GreaterThanOrEqual",
                "Equals", "GetHashCode", "CompareTo", "ToString",
            };
            var actual = new SortedSet<string>(StringComparer.Ordinal);
            foreach (MemberInfo m in typeof(Fx).GetMembers(
                         BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                actual.Add(m.Name);
            }
            Assert.Equal(string.Join(",", expected), string.Join(",", actual));
        }

        [Fact]
        public void test_fx_operations_have_specified_signatures()
        {
            // 08 §8.3 IDL + Q-015: static operations taking their operands;
            // Floor/Ceil/RoundHalfUp -> int64, Frac/Sqrt -> Fx; ToDisplayString(int) instance.
            Type fx = typeof(Fx);
            void Static(string name, Type ret, params Type[] args)
            {
                bool found = fx.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m =>
                    m.Name == name
                    && m.ReturnType == ret
                    && m.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType)
                        .SequenceEqual(args));
                Assert.True(found, "missing public static " + ret.Name + " Fx." + name + "(" + string.Join(", ", args.Select(a => a.Name)) + ")");
            }

            Static("FromInt", fx, typeof(long));
            Static("FromRaw", fx, typeof(long));
            Static("FromRatio", fx, typeof(long), typeof(long));
            Static("Parse", fx, typeof(string));
            foreach (string binary in new[] { "Add", "Sub", "Mul", "Div", "Min", "Max" })
            {
                Static(binary, fx, fx, fx);
            }
            foreach (string unary in new[] { "Neg", "Abs", "Frac", "Sqrt" })
            {
                Static(unary, fx, fx);
            }
            Static("Clamp", fx, fx, fx, fx);
            Static("Floor", typeof(long), fx);
            Static("Ceil", typeof(long), fx);
            Static("RoundHalfUp", typeof(long), fx);

            MethodInfo? display = fx.GetMethod("ToDisplayString", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            Assert.True(display != null && display.ReturnType == typeof(string), "missing instance string ToDisplayString(int)");

            PropertyInfo? raw = fx.GetProperty("Raw", BindingFlags.Public | BindingFlags.Instance);
            Assert.True(raw != null && raw.PropertyType == typeof(long) && raw.CanRead && raw.SetMethod == null,
                "Raw must be a public get-only long property");

            foreach (string name in new[] { "Zero", "One", "MinValue", "MaxValue" })
            {
                FieldInfo? f = fx.GetField(name, BindingFlags.Public | BindingFlags.Static);
                Assert.True(f != null && f.FieldType == fx && f.IsInitOnly, name + " must be a public static readonly Fx field");
            }
        }

        [Fact]
        public void test_fx_constants_have_specified_raw_values()
        {
            // 08 §8.3 Q-015: MinValue Raw = int64.MinValue, MaxValue Raw = int64.MaxValue,
            // default(Fx) is Zero.
            Assert.Equal(0L, Fx.Zero.Raw);
            Assert.Equal(OneRaw, Fx.One.Raw);
            Assert.Equal(long.MinValue, Fx.MinValue.Raw);
            Assert.Equal(long.MaxValue, Fx.MaxValue.Raw);
            Assert.Equal(0L, default(Fx).Raw);
            Assert.True(default(Fx) == Fx.Zero);
        }

        [Fact]
        public void test_fx_sim_source_declares_fx_without_wide_integer_or_checked_helpers()
        {
            // 08 §8.3: the 128-bit arithmetic and leading-zero count are hand-rolled;
            // no Int128/UInt128/BigInteger/Math.BigMul/BitOperations in sim assemblies.
            // 07 "Error handling": no exception comes from a checked context.
            // 07 L6: public type Fx lives in src/sim/core/Fx.cs.
            string root = RepoRoot();
            string fxFile = Path.Combine(root, "src", "sim", "core", "Fx.cs");
            Assert.True(File.Exists(fxFile), "07 L6 requires src/sim/core/Fx.cs");
            Assert.Matches(new Regex(@"\breadonly\s+(partial\s+)?struct\s+Fx\b"), StripComments(File.ReadAllText(fxFile)));

            var banned = new Regex(@"\b(BigInteger|Int128|UInt128|BitOperations|BigMul)\b|\bSystem\.Numerics\b|\bchecked\s*[({]");
            string simDir = Path.Combine(root, "src", "sim");
            var offenders = new List<string>();
            foreach (string file in Directory.EnumerateFiles(simDir, "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                Match m = banned.Match(StripComments(File.ReadAllText(file)));
                if (m.Success)
                {
                    offenders.Add(Path.GetRelativePath(root, file) + ": " + m.Value);
                }
            }
            Assert.True(offenders.Count == 0, "banned wide-integer / checked usage in src/sim: " + string.Join("; ", offenders));
        }

        // -------------------------------------------------------- round trips

        [Fact]
        public void test_fx_from_raw_any_value_round_trips_raw()
        {
            // 08 §8.3 Q-015: FromRaw(r) has Raw = r, never throws.
            const ulong seed = 0x526177526F756E64UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 100_000; i++)
            {
                long r = i < 64 ? unchecked((long)(1UL << i)) * (i % 2 == 0 ? 1 : -1) : FxOracle.NextRaw(g);
                if (Fx.FromRaw(r).Raw != r)
                {
                    Assert.Fail(At(seed, i) + ": FromRaw(" + r + ").Raw != " + r);
                }
            }
            Assert.Equal(long.MinValue, Fx.FromRaw(long.MinValue).Raw);
            Assert.Equal(long.MaxValue, Fx.FromRaw(long.MaxValue).Raw);
        }

        [Fact]
        public void test_fx_from_int_in_range_round_trips_through_floor()
        {
            // 08 §8.3 Q-015: FromInt(v) = v exactly for -2^31 <= v <= 2^31-1.
            const ulong seed = 0x46726F6D496E7431UL;
            var g = new SplitMix64(seed);
            var values = new List<long> { 0, 1, -1, int.MaxValue, int.MinValue, int.MaxValue - 1L, int.MinValue + 1L };
            for (int i = 0; i < 50_000; i++)
            {
                values.Add(unchecked((int)g.Next()));
            }
            for (int i = 0; i < values.Count; i++)
            {
                long v = values[i];
                Fx x = Fx.FromInt(v);
                if (x.Raw != v << 32 || Fx.Floor(x) != v || Fx.Ceil(x) != v || Fx.RoundHalfUp(x) != v || Fx.Frac(x).Raw != 0)
                {
                    Assert.Fail(At(seed, i) + ": FromInt(" + v + ") does not round-trip (Raw " + x.Raw + ")");
                }
            }
        }

        [Fact]
        public void test_fx_from_int_out_of_range_throws_overflow()
        {
            // 08 §8.3 Q-015: OverflowException unless -2^31 <= v <= 2^31-1.
            Assert.Throws<OverflowException>(() => Fx.FromInt(1L << 31));
            Assert.Throws<OverflowException>(() => Fx.FromInt(-(1L << 31) - 1));
            Assert.Throws<OverflowException>(() => Fx.FromInt(long.MaxValue));
            Assert.Throws<OverflowException>(() => Fx.FromInt(long.MinValue));
        }

        [Fact]
        public void test_fx_from_ratio_raw_over_two_pow_32_reproduces_raw()
        {
            // 08 §8.3: FromRatio is exact, then floored; r / 2^32 is exactly representable.
            const ulong seed = 0x526174696F526177UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 100_000; i++)
            {
                long r = FxOracle.NextRaw(g);
                long got = Fx.FromRatio(r, OneRaw).Raw;
                if (got != r)
                {
                    Assert.Fail(At(seed, i) + ": FromRatio(" + r + ", 2^32).Raw = " + got);
                }
            }
        }

        [Fact]
        public void test_fx_parse_of_display_string_round_trips_within_one_raw_unit()
        {
            // 08 §8.3: ToDisplayString(10) floors to a multiple of 1e-10 and Parse floors
            // to a multiple of 2^-32, so Parse(x.ToDisplayString(10)) is x or x - 2^-32.
            const ulong seed = 0x446973706C617952UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 20_000; i++)
            {
                long r = FxOracle.NextRaw(g);
                string s = R(r).ToDisplayString(10);
                long back = Fx.Parse(s).Raw;
                if (back > r || r - back > 1)
                {
                    Assert.Fail(At(seed, i) + ": Raw " + r + " displayed \"" + s + "\" parsed back to Raw " + back);
                }
            }
        }

        // --------------------------------------------------------------- Parse

        [Fact]
        public void test_fx_parse_golden_vectors_match_committed_raw()
        {
            // 08 §8.3 Q-015 Parse grammar and "Test oracle" golden vectors.
            foreach ((string text, FxOutcome outcome, long expected) in FxGolden.ParseGolden)
            {
                (FxOutcome o, long r, bool formatError) = FxOracle.Parse(text);
                if (formatError || o != outcome || (o == FxOutcome.Ok && r != expected))
                {
                    Assert.Fail("golden literal for Parse(\"" + text + "\") disagrees with the reference oracle");
                }
                Expect("Parse(\"" + text + "\")", (outcome, expected), () => Fx.Parse(text).Raw);
            }
        }

        [Fact]
        public void test_fx_parse_random_decimals_match_oracle()
        {
            // 08 §8.3 Q-015: the decimal value is taken exactly and then floored.
            const ulong seed = 0x5061727365526E64UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 30_000; i++)
            {
                var sb = new StringBuilder();
                if ((g.Next() & 1) == 1)
                {
                    sb.Append('-');
                }
                ulong magnitudeKind = g.Next() % 4;
                ulong intPart = magnitudeKind switch
                {
                    0 => 0UL,
                    1 => g.Next() % 10,
                    2 => g.Next() % 2147483648UL,
                    _ => g.Next() % 4294967296UL,
                };
                sb.Append(intPart.ToString(CultureInfo.InvariantCulture));
                int fracDigits = (int)(g.Next() % 11);
                if (fracDigits > 0)
                {
                    sb.Append('.');
                    for (int k = 0; k < fracDigits; k++)
                    {
                        sb.Append((char)('0' + (int)(g.Next() % 10)));
                    }
                }
                string text = sb.ToString();
                (FxOutcome o, long r, bool formatError) = FxOracle.Parse(text);
                Assert.False(formatError, "generator produced an invalid string " + text);
                Expect(At(seed, i) + ": Parse(\"" + text + "\")", (o, r), () => Fx.Parse(text).Raw);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("+1")]
        [InlineData(" 1")]
        [InlineData("1 ")]
        [InlineData("1\n")]
        [InlineData("01")]
        [InlineData("00")]
        [InlineData("-01")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("-.5")]
        [InlineData("-")]
        [InlineData("--1")]
        [InlineData("1e3")]
        [InlineData("1E3")]
        [InlineData("1,5")]
        [InlineData("1_000")]
        [InlineData("1.5.5")]
        [InlineData("0.-1")]
        [InlineData("1.12345678901")]
        [InlineData("0x10")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("١")]
        [InlineData("１")]
        [InlineData("−1")]
        public void test_fx_parse_malformed_input_throws_format_exception(string text)
        {
            // 08 §8.3 Q-015: the whole ASCII string must match
            // -?(0|[1-9][0-9]*)(\.[0-9]{1,10})? ; any other mismatch is FormatException.
            Assert.Throws<FormatException>(() => Fx.Parse(text));
        }

        [Fact]
        public void test_fx_parse_null_throws_argument_null()
        {
            // 08 §8.3 Q-015: null throws ArgumentNullException.
            Assert.Throws<ArgumentNullException>(() => Fx.Parse(null!));
        }

        [Fact]
        public void test_fx_parse_out_of_range_throws_overflow()
        {
            // 08 §8.3 Q-015: a grammatical but out-of-range value throws OverflowException.
            Assert.Throws<OverflowException>(() => Fx.Parse("2147483648"));
            Assert.Throws<OverflowException>(() => Fx.Parse("2147483648.0"));
            Assert.Throws<OverflowException>(() => Fx.Parse("-2147483649"));
            Assert.Throws<OverflowException>(() => Fx.Parse("-2147483648.0000000001"));
            Assert.Throws<OverflowException>(() => Fx.Parse("99999999999999999999999999999999"));
            Assert.Throws<OverflowException>(() => Fx.Parse("-99999999999999999999999999999999.5"));
            Assert.Throws<OverflowException>(() => Fx.Parse("99999999999"));
            Assert.Throws<OverflowException>(() => Fx.Parse("-99999999999.1234567890"));
        }

        [Theory]
        [InlineData("99999999999x")]
        [InlineData("99999999999.")]
        [InlineData("-99999999999.12345678901")]
        [InlineData("099999999999")]
        [InlineData("-099999999999")]
        [InlineData("+99999999999")]
        [InlineData("99999999999 ")]
        [InlineData(" -99999999999")]
        [InlineData("99999999999e0")]
        [InlineData("99999999999.5.5")]
        [InlineData("99999999999999999999999999999999,5")]
        public void test_fx_parse_malformed_input_with_out_of_range_magnitude_throws_format_exception(string text)
        {
            // 08 §8.3 Q-015 A12: OverflowException is only for a string that
            // matches the grammar. A mismatch is FormatException even when its
            // integer part alone would overflow, so grammar is checked first.
            Assert.Throws<FormatException>(() => Fx.Parse(text));
        }

        [Fact]
        public void test_fx_parse_negative_tenth_is_not_negation_of_positive_tenth()
        {
            // 08 §8.3 Q-015: "-0.1" is floor(-0.1 * 2^32), not -Parse("0.1").
            Assert.Equal(429496729L, Fx.Parse("0.1").Raw);
            Assert.Equal(-429496730L, Fx.Parse("-0.1").Raw);
            Assert.Equal(-429496729L, Fx.Neg(Fx.Parse("0.1")).Raw);
            Assert.Equal(0L, Fx.Parse("-0").Raw);
            Assert.Equal(0L, Fx.Parse("-0.0").Raw);
        }

        [Fact]
        public void test_fx_parse_and_display_under_non_invariant_culture_are_unchanged()
        {
            // 07 "Runtime portability" rule 4 and 08 §8.3: invariant ASCII regardless
            // of the current culture (de-DE uses ',' decimals; sv-SE uses U+2212 minus).
            CultureInfo savedCulture = CultureInfo.CurrentCulture;
            CultureInfo savedUi = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string name in new[] { "de-DE", "sv-SE", "ar-SA" })
                {
                    CultureInfo c = CultureInfo.GetCultureInfo(name);
                    CultureInfo.CurrentCulture = c;
                    CultureInfo.CurrentUICulture = c;
                    Assert.Equal(OneRaw + HalfRaw, Fx.Parse("1.5").Raw);
                    Assert.Equal(-9663676416L, Fx.Parse("-2.25").Raw);
                    Assert.Throws<FormatException>(() => Fx.Parse("1,5"));
                    Assert.Equal("-2.50", Fx.FromRatio(-5, 2).ToDisplayString(2));
                    Assert.Equal("1234567.1250", Fx.FromRatio(9876537, 8).ToDisplayString(4));
                    Assert.Equal("0.5000000000", Fx.FromRatio(1, 2).ToString());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
                CultureInfo.CurrentUICulture = savedUi;
            }
        }

        // ----------------------------------------------------- ToDisplayString

        [Fact]
        public void test_fx_to_display_string_golden_vectors_match_committed_text()
        {
            // 08 §8.3 Q-015: floored to a multiple of 10^-d, invariant ASCII,
            // no leading zeros, exactly d fraction digits, zero has no sign.
            foreach ((long raw, int d, string expected) in FxGolden.DisplayGolden)
            {
                Assert.True(FxOracle.Display(raw, d) == expected,
                    "golden literal for ToDisplayString(" + raw + ", " + d + ") disagrees with the reference oracle");
                Assert.Equal(expected, R(raw).ToDisplayString(d));
            }
        }

        [Fact]
        public void test_fx_to_display_string_random_values_match_oracle()
        {
            // 08 §8.3 Q-015 ToDisplayString rules, d from 0 to 10.
            const ulong seed = 0x446973704F72636CUL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 30_000; i++)
            {
                long r = FxOracle.NextRaw(g);
                int d = (int)(g.Next() % 11);
                string expected = FxOracle.Display(r, d);
                string got = R(r).ToDisplayString(d);
                if (!string.Equals(expected, got, StringComparison.Ordinal))
                {
                    Assert.Fail(At(seed, i) + ": Raw " + r + " at d=" + d + " expected \"" + expected + "\", got \"" + got + "\"");
                }
            }
        }

        [Fact]
        public void test_fx_to_display_string_zero_result_has_no_sign()
        {
            // 08 §8.3 Q-015: a result equal to zero has no sign.
            Assert.Equal("0", Fx.Zero.ToDisplayString(0));
            Assert.Equal("0.0000000000", Fx.Zero.ToDisplayString(10));
            Assert.Equal("0.0", R(1).ToDisplayString(1));
            Assert.Equal("-0.1", R(-1).ToDisplayString(1));
        }

        [Fact]
        public void test_fx_to_display_string_decimals_out_of_range_throws()
        {
            // 08 §8.3 Q-015: d outside 0..10 throws ArgumentOutOfRangeException.
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.One.ToDisplayString(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.One.ToDisplayString(11));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.One.ToDisplayString(int.MinValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.One.ToDisplayString(int.MaxValue));
        }

        [Fact]
        public void test_fx_to_string_equals_display_string_with_ten_decimals()
        {
            // 08 §8.3 Q-015: ToString() returns ToDisplayString(10).
            const ulong seed = 0x546F537472696E67UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 2_000; i++)
            {
                Fx x = R(FxOracle.NextRaw(g));
                if (!string.Equals(x.ToDisplayString(10), x.ToString(), StringComparison.Ordinal))
                {
                    Assert.Fail(At(seed, i) + ": ToString() differs from ToDisplayString(10) for Raw " + x.Raw);
                }
            }
        }

        // ------------------------------------------------ truncation direction

        [Fact]
        public void test_fx_mul_negative_operand_truncates_toward_negative_infinity()
        {
            // 08 §8.3: every narrowing floors toward negative infinity.
            Assert.Equal(-1L, Fx.Mul(R(-1), R(1)).Raw);            // -2^-64 -> -2^-32
            Assert.Equal(0L, Fx.Mul(R(1), R(1)).Raw);              // +2^-64 -> 0
            Assert.Equal(-1L, Fx.Mul(Fx.FromRatio(-1, 2), R(1)).Raw);
            Assert.Equal(0L, Fx.Mul(Fx.FromRatio(1, 2), R(1)).Raw);
            Assert.Equal(-1L, Fx.Mul(R(1), R(-1)).Raw);
            Assert.Equal(0L, Fx.Mul(R(-1), R(-1)).Raw);
            Assert.Equal(-4294967298L, Fx.Mul(Fx.FromRatio(-1, 3), Fx.FromInt(3)).Raw);
        }

        [Fact]
        public void test_fx_div_negative_result_truncates_toward_negative_infinity()
        {
            // 08 §8.3: Div floors toward negative infinity.
            Assert.Equal(-1L, Fx.Div(R(-1), Fx.FromInt(2)).Raw);
            Assert.Equal(0L, Fx.Div(R(1), Fx.FromInt(2)).Raw);
            Assert.Equal(-1431655766L, Fx.Div(Fx.FromInt(-1), Fx.FromInt(3)).Raw);
            Assert.Equal(-1431655766L, Fx.Div(Fx.One, Fx.FromInt(-3)).Raw);
            Assert.Equal(1431655765L, Fx.Div(Fx.FromInt(-1), Fx.FromInt(-3)).Raw);
            Assert.Equal(-1L, Fx.Div(R(-1), Fx.MaxValue).Raw);
        }

        [Fact]
        public void test_fx_from_ratio_negative_result_truncates_toward_negative_infinity()
        {
            // 08 §8.3: FromRatio is exact, then floored.
            Assert.Equal(-1431655766L, Fx.FromRatio(-1, 3).Raw);
            Assert.Equal(-1431655766L, Fx.FromRatio(1, -3).Raw);
            Assert.Equal(1431655765L, Fx.FromRatio(-1, -3).Raw);
            Assert.Equal(-1L, Fx.FromRatio(-1, long.MaxValue).Raw);
            Assert.Equal(0L, Fx.FromRatio(1, long.MaxValue).Raw);
        }

        [Fact]
        public void test_fx_floor_ceil_negative_fraction_bracket_value()
        {
            // 08 §8.3 Q-015: Floor = floor(x), Ceil = ceil(x), as int64, never throw.
            Assert.Equal(-1L, Fx.Floor(R(-1)));
            Assert.Equal(0L, Fx.Ceil(R(-1)));
            Assert.Equal(-2L, Fx.Floor(Fx.FromRatio(-3, 2)));
            Assert.Equal(-1L, Fx.Ceil(Fx.FromRatio(-3, 2)));
            Assert.Equal(1L, Fx.Floor(Fx.FromRatio(3, 2)));
            Assert.Equal(2L, Fx.Ceil(Fx.FromRatio(3, 2)));
            Assert.Equal(-2147483648L, Fx.Floor(Fx.MinValue));
            Assert.Equal(-2147483648L, Fx.Ceil(Fx.MinValue));
            Assert.Equal(2147483647L, Fx.Floor(Fx.MaxValue));
            Assert.Equal(2147483648L, Fx.Ceil(Fx.MaxValue));
        }

        [Fact]
        public void test_fx_frac_negative_value_is_in_zero_to_one()
        {
            // 08 §8.3 Q-015: Frac = x - Floor(x), in [0, 1).
            Assert.Equal(OneRaw - 1, Fx.Frac(R(-1)).Raw);
            Assert.Equal(3221225472L, Fx.Frac(Fx.FromRatio(-1, 4)).Raw);
            Assert.Equal(0L, Fx.Frac(Fx.FromInt(-3)).Raw);
            Assert.Equal(0L, Fx.Frac(Fx.MinValue).Raw);
            Assert.Equal(OneRaw - 1, Fx.Frac(Fx.MaxValue).Raw);

            const ulong seed = 0x4672616352616E67UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 50_000; i++)
            {
                long r = FxOracle.NextRaw(g);
                long frac = Fx.Frac(R(r)).Raw;
                long floor = Fx.Floor(R(r));
                if (frac < 0 || frac >= OneRaw || (BigInteger)floor * FxOracle.Two32 + frac != r)
                {
                    Assert.Fail(At(seed, i) + ": Raw " + r + " gave Floor " + floor + ", Frac Raw " + frac);
                }
            }
        }

        [Fact]
        public void test_fx_round_half_up_halves_round_toward_positive_infinity()
        {
            // 08 §8.3 Q-015: RoundHalfUp = floor(x + 1/2): -0.5 -> 0, -1.5 -> -1, 2.5 -> 3.
            Assert.Equal(0L, Fx.RoundHalfUp(Fx.FromRatio(-1, 2)));
            Assert.Equal(-1L, Fx.RoundHalfUp(Fx.FromRatio(-3, 2)));
            Assert.Equal(3L, Fx.RoundHalfUp(Fx.FromRatio(5, 2)));
            Assert.Equal(1L, Fx.RoundHalfUp(Fx.FromRatio(1, 2)));
            Assert.Equal(-1L, Fx.RoundHalfUp(R(-HalfRaw - 1)));
            Assert.Equal(0L, Fx.RoundHalfUp(R(HalfRaw - 1)));
            Assert.Equal(-2L, Fx.RoundHalfUp(Fx.FromRatio(-5, 2)));
        }

        [Fact]
        public void test_fx_round_half_up_extremes_do_not_throw()
        {
            // 08 §8.3 Q-015: RoundHalfUp never throws, MaxValue included.
            Assert.Equal(2147483648L, Fx.RoundHalfUp(Fx.MaxValue));
            Assert.Equal(-2147483648L, Fx.RoundHalfUp(Fx.MinValue));
        }

        [Fact]
        public void test_fx_floor_ceil_round_frac_random_values_match_oracle()
        {
            // 08 §8.3 Q-015 table rows Floor, Ceil, RoundHalfUp, Frac.
            const ulong seed = 0x526F756E64696E67UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 100_000; i++)
            {
                long r = FxOracle.NextRaw(g);
                Fx x = R(r);
                if (Fx.Floor(x) != FxOracle.Floor(r) || Fx.Ceil(x) != FxOracle.Ceil(r)
                    || Fx.RoundHalfUp(x) != FxOracle.RoundHalfUp(r) || Fx.Frac(x).Raw != FxOracle.Frac(r))
                {
                    Assert.Fail(At(seed, i) + ": rounding family disagrees with the oracle for Raw " + r);
                }
            }
        }

        // ------------------------------------------------ golden bit-exactness

        [Fact]
        public void test_fx_mul_golden_vectors_match_committed_raw()
        {
            // 08 §8.3 "Test oracle": golden vectors pin Mul across processes and machines.
            CheckBinaryGolden("Mul", FxGolden.MulGolden, FxOracle.Mul, (a, b) => Fx.Mul(R(a), R(b)).Raw);
        }

        [Fact]
        public void test_fx_div_golden_vectors_match_committed_raw()
        {
            CheckBinaryGolden("Div", FxGolden.DivGolden, FxOracle.Div, (a, b) => Fx.Div(R(a), R(b)).Raw);
        }

        [Fact]
        public void test_fx_from_ratio_golden_vectors_match_committed_raw()
        {
            CheckBinaryGolden("FromRatio", FxGolden.FromRatioGolden, FxOracle.FromRatio, (n, d) => Fx.FromRatio(n, d).Raw);
        }

        [Fact]
        public void test_fx_sqrt_golden_vectors_match_committed_raw()
        {
            foreach ((long a, FxOutcome outcome, long expected) in FxGolden.SqrtGolden)
            {
                if (FxOracle.Sqrt(a) != (outcome, expected))
                {
                    Assert.Fail("golden literal for Sqrt(" + a + ") disagrees with the reference oracle");
                }
                Expect("Sqrt(" + a + ")", (outcome, expected), () => Fx.Sqrt(R(a)).Raw);
            }
        }

        [Fact]
        public void test_fx_golden_digest_of_seeded_sequence_matches_committed_value()
        {
            // 08 §8.3 "Test oracle": the committed digest pins Add, Sub, Mul, Div, Sqrt,
            // FromRatio, Floor, Ceil, RoundHalfUp, Frac, Neg and Abs, outcome and value,
            // over 20 000 seeded operations. It feeds determinism_same_process and
            // determinism_cross_process indirectly: same inputs, same bits, forever.
            ulong seed = FxGolden.DigestSeed;
            var g = new SplitMix64(seed);
            ulong oracleHash = FxOracle.FnvOffset;
            ulong fxHash = FxOracle.FnvOffset;
            int firstDivergence = -1;
            string divergence = string.Empty;
            for (int i = 0; i < FxGolden.DigestLength; i++)
            {
                int op = (int)(g.Next() % FxOracle.OpCount);
                long a = FxOracle.NextRaw(g);
                long b = FxOracle.NextRaw(g);
                (FxOutcome Outcome, long Value) model = FxOracle.Apply(op, a, b);
                (FxOutcome Outcome, long Value) got = ApplyFx(op, a, b);
                oracleHash = FxOracle.FnvInt64(FxOracle.FnvByte(oracleHash, (byte)model.Outcome), model.Outcome == FxOutcome.Ok ? model.Value : 0L);
                fxHash = FxOracle.FnvInt64(FxOracle.FnvByte(fxHash, (byte)got.Outcome), got.Outcome == FxOutcome.Ok ? got.Value : 0L);
                if (firstDivergence < 0 && (model.Outcome != got.Outcome || (got.Outcome == FxOutcome.Ok && model.Value != got.Value)))
                {
                    firstDivergence = i;
                    divergence = "op " + op + " on " + Raws(a, b) + ": expected " + Describe(model) + ", got " + Describe(got);
                }
            }
            Assert.True(oracleHash == FxGolden.DigestExpected, "committed digest disagrees with the reference oracle");
            Assert.True(fxHash == FxGolden.DigestExpected,
                "Fx digest 0x" + fxHash.ToString("X16", CultureInfo.InvariantCulture) + " != golden; first divergence at "
                + At(seed, firstDivergence) + ": " + divergence);
        }

        // ------------------------------------------------ wide oracle sweeps

        [Fact]
        public void test_fx_mul_wide_random_range_matches_int128_oracle()
        {
            // 08 §8.3: Mul through a 128-bit intermediate, floored, OverflowException out of range.
            // Oracle: Int128 product, arithmetic shift right 32 (which floors).
            const ulong seed = 0x4D756C496E743132UL;
            var g = new SplitMix64(seed);
            Int128 min = long.MinValue;
            Int128 max = long.MaxValue;
            for (int i = 0; i < 200_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                long b = FxOracle.NextRaw(g);
                Int128 exact = (Int128)a * b >> 32;
                (FxOutcome, long) expected = exact < min || exact > max ? (FxOutcome.Overflow, 0L) : (FxOutcome.Ok, (long)exact);
                Expect(At(seed, i) + ": Mul" + Raws(a, b), expected, () => Fx.Mul(R(a), R(b)).Raw);
            }
        }

        [Fact]
        public void test_fx_div_wide_random_range_matches_biginteger_oracle()
        {
            // 08 §8.3: Div through a 128-by-64-bit division, floored; DivideByZeroException
            // for b = 0; OverflowException out of range (including long.MinValue / -1 cases).
            const ulong seed = 0x446976426967496EUL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 200_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                long b = FxOracle.NextRaw(g);
                Expect(At(seed, i) + ": Div" + Raws(a, b), FxOracle.Div(a, b), () => Fx.Div(R(a), R(b)).Raw);
            }
        }

        [Fact]
        public void test_fx_from_ratio_wide_random_range_matches_biginteger_oracle()
        {
            // 08 §8.3 Q-015 table row FromRatio.
            const ulong seed = 0x526174696F426967UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 200_000; i++)
            {
                long n = FxOracle.NextRaw(g);
                long d = FxOracle.NextRaw(g);
                Expect(At(seed, i) + ": FromRatio" + Raws(n, d), FxOracle.FromRatio(n, d), () => Fx.FromRatio(n, d).Raw);
            }
        }

        [Fact]
        public void test_fx_sqrt_wide_random_range_matches_biginteger_oracle()
        {
            // 08 §8.3 Q-015: Sqrt Raw = floor(sqrt(x.Raw * 2^32)) exactly;
            // ArgumentOutOfRangeException if x < 0.
            const ulong seed = 0x5371727442696749UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 200_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                Expect(At(seed, i) + ": Sqrt(" + a + ")", FxOracle.Sqrt(a), () => Fx.Sqrt(R(a)).Raw);
            }
        }

        [Fact]
        public void test_fx_sqrt_result_is_greatest_root_not_exceeding_input()
        {
            // 08 §8.3 Q-015 "exact-stable": r*r <= x.Raw * 2^32 < (r+1)*(r+1).
            const ulong seed = 0x5371727442726B74UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 100_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                if (a < 0)
                {
                    a = a == long.MinValue ? long.MaxValue : -a;
                }
                BigInteger n = (BigInteger)a * FxOracle.Two32;
                BigInteger r = Fx.Sqrt(R(a)).Raw;
                if (r < 0 || r * r > n || (r + 1) * (r + 1) <= n)
                {
                    Assert.Fail(At(seed, i) + ": Sqrt(Raw " + a + ") = Raw " + r + " is not the floor root");
                }
            }
        }

        [Fact]
        public void test_fx_sqrt_perfect_squares_are_exact()
        {
            // 08 §8.3 Q-015: sqrt(k^2) = k for integers; sqrt(Raw k^2) = Raw (k << 16).
            for (long k = 0; k <= 46340; k++)
            {
                long got = Fx.Sqrt(Fx.FromInt(k * k)).Raw;
                if (got != k << 32)
                {
                    Assert.Fail("Sqrt(" + (k * k) + ") = Raw " + got + ", expected " + k);
                }
            }
            const ulong seed = 0x5065726665637453UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 50_000; i++)
            {
                long k = (long)(g.Next() % 3037000499UL);
                long got = Fx.Sqrt(R(k * k)).Raw;
                if (got != k << 16)
                {
                    Assert.Fail(At(seed, i) + ": Sqrt(Raw " + (k * k) + ") = Raw " + got + ", expected Raw " + (k << 16));
                }
            }
        }

        [Fact]
        public void test_fx_add_sub_neg_abs_wide_random_range_match_oracle()
        {
            // 08 §8.3 Q-015: Add, Sub, Neg, Abs are exact; OverflowException out of range;
            // Fx never wraps and never saturates.
            const ulong seed = 0x4164645375624E67UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 200_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                long b = FxOracle.NextRaw(g);
                string at = At(seed, i);
                Expect(at + ": Add" + Raws(a, b), FxOracle.Add(a, b), () => Fx.Add(R(a), R(b)).Raw);
                Expect(at + ": Sub" + Raws(a, b), FxOracle.Sub(a, b), () => Fx.Sub(R(a), R(b)).Raw);
                Expect(at + ": Neg(" + a + ")", FxOracle.Neg(a), () => Fx.Neg(R(a)).Raw);
                Expect(at + ": Abs(" + a + ")", FxOracle.Abs(a), () => Fx.Abs(R(a)).Raw);
            }
        }

        // ------------------------------------------------------------- throws

        [Fact]
        public void test_fx_div_by_zero_throws_divide_by_zero()
        {
            // 08 §8.3 Q-015: DivideByZeroException if b = 0.
            Assert.Throws<DivideByZeroException>(() => Fx.Div(Fx.One, Fx.Zero));
            Assert.Throws<DivideByZeroException>(() => Fx.Div(Fx.Zero, Fx.Zero));
            Assert.Throws<DivideByZeroException>(() => Fx.Div(Fx.MinValue, Fx.Zero));
            Assert.Throws<DivideByZeroException>(() => Fx.Div(Fx.MaxValue, default(Fx)));
        }

        [Fact]
        public void test_fx_from_ratio_zero_denominator_throws_divide_by_zero()
        {
            // 08 §8.3 Q-015: FromRatio throws DivideByZeroException if d = 0.
            Assert.Throws<DivideByZeroException>(() => Fx.FromRatio(1, 0));
            Assert.Throws<DivideByZeroException>(() => Fx.FromRatio(0, 0));
            Assert.Throws<DivideByZeroException>(() => Fx.FromRatio(long.MinValue, 0));
        }

        [Fact]
        public void test_fx_div_min_value_by_negative_epsilon_throws_overflow()
        {
            // 08 §8.3: long.MinValue / -1 is checked explicitly before any BCL
            // operator could throw; the result is out of range, so OverflowException.
            Assert.Throws<OverflowException>(() => Fx.Div(Fx.MinValue, R(-1)));
        }

        [Fact]
        public void test_fx_div_min_value_by_negative_one_throws_overflow()
        {
            // 08 §8.3 Q-015: MinValue / -1 = 2^31, out of range.
            Assert.Throws<OverflowException>(() => Fx.Div(Fx.MinValue, Fx.FromInt(-1)));
            Assert.Throws<OverflowException>(() => Fx.FromRatio(long.MinValue, -1));
            Assert.Equal(long.MinValue, Fx.Div(Fx.MinValue, Fx.One).Raw);
        }

        [Fact]
        public void test_fx_mul_overflow_throws_overflow()
        {
            // 08 §8.3 Q-015: Mul throws OverflowException if out of range.
            Assert.Throws<OverflowException>(() => Fx.Mul(Fx.FromInt(65536), Fx.FromInt(32768)));
            Assert.Throws<OverflowException>(() => Fx.Mul(Fx.MaxValue, Fx.FromInt(2)));
            Assert.Throws<OverflowException>(() => Fx.Mul(Fx.MinValue, Fx.FromInt(-1)));
            Assert.Throws<OverflowException>(() => Fx.Mul(Fx.MinValue, Fx.MinValue));
            Assert.Equal(long.MinValue, Fx.Mul(Fx.FromInt(-65536), Fx.FromInt(32768)).Raw);
            Assert.Equal(6871947673600000000L, Fx.Mul(Fx.FromInt(40000), Fx.FromInt(40000)).Raw);
        }

        [Fact]
        public void test_fx_add_sub_overflow_throws_overflow()
        {
            // 08 §8.3 Q-015: Add and Sub are exact; OverflowException out of range.
            Assert.Throws<OverflowException>(() => Fx.Add(Fx.MaxValue, R(1)));
            Assert.Throws<OverflowException>(() => Fx.Add(Fx.MinValue, R(-1)));
            Assert.Throws<OverflowException>(() => Fx.Add(Fx.MinValue, Fx.MinValue));
            Assert.Throws<OverflowException>(() => Fx.Sub(Fx.MinValue, R(1)));
            Assert.Throws<OverflowException>(() => Fx.Sub(Fx.MaxValue, R(-1)));
            Assert.Throws<OverflowException>(() => Fx.Sub(Fx.Zero, Fx.MinValue));
            Assert.Equal(long.MaxValue, Fx.Add(Fx.MaxValue, Fx.Zero).Raw);
            Assert.Equal(-1L, Fx.Add(Fx.MinValue, Fx.MaxValue).Raw);
            Assert.Equal(-long.MaxValue, Fx.Sub(Fx.Zero, Fx.MaxValue).Raw);
            Assert.Equal(long.MinValue, Fx.Sub(R(-1), Fx.MaxValue).Raw);
        }

        [Fact]
        public void test_fx_neg_abs_min_value_throws_overflow()
        {
            // 08 §8.3 Q-015: Neg and Abs throw OverflowException for MinValue.
            Assert.Throws<OverflowException>(() => Fx.Neg(Fx.MinValue));
            Assert.Throws<OverflowException>(() => Fx.Abs(Fx.MinValue));
            Assert.Equal(-long.MaxValue, Fx.Neg(Fx.MaxValue).Raw);
            Assert.Equal(long.MaxValue, Fx.Abs(R(long.MinValue + 1)).Raw);
            Assert.Equal(1L, Fx.Abs(R(-1)).Raw);
        }

        [Fact]
        public void test_fx_sqrt_negative_throws_argument_out_of_range()
        {
            // 08 §8.3 Q-015: Sqrt throws ArgumentOutOfRangeException if x < 0.
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.Sqrt(R(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.Sqrt(Fx.MinValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fx.Sqrt(Fx.FromInt(-4)));
        }

        [Fact]
        public void test_fx_clamp_lo_greater_than_hi_throws_argument()
        {
            // 08 §8.3 Q-015: Clamp throws ArgumentException if lo > hi.
            Assert.Throws<ArgumentException>(() => Fx.Clamp(Fx.Zero, Fx.One, Fx.Zero));
            Assert.Throws<ArgumentException>(() => Fx.Clamp(Fx.Zero, R(1), R(0)));
            Assert.Throws<ArgumentException>(() => Fx.Clamp(Fx.Zero, Fx.MaxValue, Fx.MinValue));
        }

        [Fact]
        public void test_fx_operators_overflow_throw_like_static_methods()
        {
            // 08 §8.3 Q-015: each operator is exactly its static method.
            Assert.Throws<OverflowException>(() => Fx.MaxValue + R(1));
            Assert.Throws<OverflowException>(() => Fx.MinValue - R(1));
            Assert.Throws<OverflowException>(() => Fx.FromInt(65536) * Fx.FromInt(32768));
            Assert.Throws<OverflowException>(() => Fx.MinValue / R(-1));
            Assert.Throws<DivideByZeroException>(() => Fx.One / Fx.Zero);
            Assert.Throws<OverflowException>(() => -Fx.MinValue);
        }

        // ------------------------------------------------ min, max, clamp

        [Fact]
        public void test_fx_min_max_clamp_return_specified_bounds()
        {
            // 08 §8.3 Q-015: Min, Max as named; Clamp(x, lo, hi) = Max(lo, Min(x, hi)).
            Fx half = Fx.FromRatio(1, 2);
            Assert.Equal(0L, Fx.Min(Fx.One, Fx.Zero).Raw);
            Assert.Equal(OneRaw, Fx.Max(Fx.One, Fx.Zero).Raw);
            Assert.Equal(long.MinValue, Fx.Min(Fx.MinValue, Fx.MaxValue).Raw);
            Assert.Equal(long.MaxValue, Fx.Max(Fx.MinValue, Fx.MaxValue).Raw);
            Assert.Equal(OneRaw, Fx.Clamp(Fx.FromInt(5), Fx.Zero, Fx.One).Raw);
            Assert.Equal(0L, Fx.Clamp(Fx.FromInt(-5), Fx.Zero, Fx.One).Raw);
            Assert.Equal(HalfRaw, Fx.Clamp(half, Fx.Zero, Fx.One).Raw);
            Assert.Equal(HalfRaw, Fx.Clamp(Fx.MaxValue, half, half).Raw);

            const ulong seed = 0x436C616D70526E67UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 50_000; i++)
            {
                long x = FxOracle.NextRaw(g);
                long p = FxOracle.NextRaw(g);
                long q = FxOracle.NextRaw(g);
                long lo = Math.Min(p, q);
                long hi = Math.Max(p, q);
                long expected = Math.Max(lo, Math.Min(x, hi));
                if (Fx.Clamp(R(x), R(lo), R(hi)).Raw != expected
                    || Fx.Min(R(x), R(p)).Raw != Math.Min(x, p)
                    || Fx.Max(R(x), R(p)).Raw != Math.Max(x, p))
                {
                    Assert.Fail(At(seed, i) + ": Min/Max/Clamp disagree for x=" + x + " lo=" + lo + " hi=" + hi);
                }
            }
        }

        // ------------------------------------------- operators and comparison

        [Fact]
        public void test_fx_operators_match_static_methods()
        {
            // 08 §8.3 Q-015: + - * / and unary - are exactly Add, Sub, Mul, Div, Neg.
            const ulong seed = 0x4F70657261746F72UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 50_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                long b = FxOracle.NextRaw(g);
                string at = At(seed, i) + Raws(a, b);
                Expect(at + " +", Run(() => Fx.Add(R(a), R(b)).Raw), () => (R(a) + R(b)).Raw);
                Expect(at + " -", Run(() => Fx.Sub(R(a), R(b)).Raw), () => (R(a) - R(b)).Raw);
                Expect(at + " *", Run(() => Fx.Mul(R(a), R(b)).Raw), () => (R(a) * R(b)).Raw);
                Expect(at + " /", Run(() => Fx.Div(R(a), R(b)).Raw), () => (R(a) / R(b)).Raw);
                Expect(at + " unary -", Run(() => Fx.Neg(R(a)).Raw), () => (-R(a)).Raw);
            }
        }

        [Fact]
        public void test_fx_comparisons_and_equality_agree_with_raw()
        {
            // 08 §8.3 Q-015: == != < <= > >= compare Raw; Equals, CompareTo and
            // GetHashCode agree with Raw.
            const ulong seed = 0x436F6D7061726552UL;
            var g = new SplitMix64(seed);
            for (int i = 0; i < 100_000; i++)
            {
                long a = FxOracle.NextRaw(g);
                long b = g.Next() % 4 == 0 ? a : FxOracle.NextRaw(g);
                Fx x = R(a);
                Fx y = R(b);
                bool ok = (x == y) == (a == b)
                          && (x != y) == (a != b)
                          && (x < y) == (a < b)
                          && (x <= y) == (a <= b)
                          && (x > y) == (a > b)
                          && (x >= y) == (a >= b)
                          && x.Equals(y) == (a == b)
                          && x.Equals((object)y) == (a == b)
                          && Math.Sign(x.CompareTo(y)) == Math.Sign(a.CompareTo(b))
                          && (a != b || x.GetHashCode() == y.GetHashCode());
                if (!ok)
                {
                    Assert.Fail(At(seed, i) + ": comparison or equality disagrees with Raw for " + Raws(a, b));
                }
            }
            Assert.False(Fx.One.Equals((object)OneRaw));
            Assert.False(Fx.Zero.Equals(null));
            Assert.Equal(Fx.Zero.GetHashCode(), default(Fx).GetHashCode());
        }

        // -------------------------------------------------------------- budget

        private static Fx[] BudgetInputs(ulong seed, int count)
        {
            // Magnitudes in [1/16, 256) with random sign, so Mul and Div never overflow.
            var g = new SplitMix64(seed);
            var xs = new Fx[count];
            for (int i = 0; i < count; i++)
            {
                long magnitude = (1L << 28) + (long)(g.Next() % ((256UL << 32) - (1UL << 28)));
                xs[i] = Fx.FromRaw((g.Next() & 1) == 0 ? magnitude : -magnitude);
            }
            return xs;
        }

        private static long ExerciseAllOperations(Fx[] xs, int rounds)
        {
            long acc = 0;
            for (int round = 0; round < rounds; round++)
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    Fx a = xs[i];
                    Fx b = xs[(i + 1) % xs.Length];
                    Fx mn = Fx.Min(a, b);
                    Fx mx = Fx.Max(a, b);
                    acc ^= Fx.Add(a, b).Raw ^ Fx.Sub(a, b).Raw ^ Fx.Mul(a, b).Raw ^ Fx.Div(a, b).Raw;
                    acc ^= Fx.Neg(a).Raw ^ Fx.Abs(a).Raw ^ mn.Raw ^ mx.Raw ^ Fx.Clamp(a, mn, mx).Raw;
                    acc ^= Fx.Frac(a).Raw ^ Fx.Sqrt(Fx.Abs(a)).Raw;
                    acc ^= (a + b - a * b / b + -a).Raw;
                    acc += Fx.Floor(a) + Fx.Ceil(a) + Fx.RoundHalfUp(a);
                    acc += Fx.FromInt(i).Raw + Fx.FromRatio(i, 7).Raw + Fx.FromRaw(i).Raw;
                    acc += (a == b ? 1 : 0) + (a != b ? 2 : 0) + (a < b ? 4 : 0) + (a <= b ? 8 : 0)
                           + (a > b ? 16 : 0) + (a >= b ? 32 : 0);
                    acc += a.CompareTo(b) + (a.Equals(b) ? 1 : 0) + a.GetHashCode();
                    acc += Fx.Zero.Raw + Fx.One.Raw + Fx.MinValue.Raw + Fx.MaxValue.Raw;
                }
            }
            return acc;
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_fx_all_arithmetic_operations_allocate_zero_bytes()
        {
            // T-003 budget and 03-module-map.md "Allocation": Fx is a value type, every
            // operation allocates zero bytes and boxing anywhere is a rejection.
            // (ToDisplayString is excluded: it returns a new string by definition.)
            Fx[] xs = BudgetInputs(0x416C6C6F63467801UL, 256);
            long warm = ExerciseAllOperations(xs, 4);
            long before = GC.GetAllocatedBytesForCurrentThread();
            long acc = ExerciseAllOperations(xs, 100);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            GC.KeepAlive(warm + acc);
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_fx_parse_valid_input_allocates_zero_bytes()
        {
            // T-003 budget: zero allocation for all operations; Parse of a valid
            // string needs no intermediate objects.
            string[] inputs = { "0", "1", "-1", "0.5", "2.25", "-0.1", "123456.789", "2147483647.9999999999", "-2147483648" };
            long warm = 0;
            for (int i = 0; i < 64; i++)
            {
                warm += Fx.Parse(inputs[i % inputs.Length]).Raw;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            long acc = 0;
            for (int i = 0; i < 10_000; i++)
            {
                acc += Fx.Parse(inputs[i % inputs.Length]).Raw;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            GC.KeepAlive(warm + acc);
        }
    }
}
