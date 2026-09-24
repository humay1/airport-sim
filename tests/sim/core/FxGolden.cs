namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Golden vectors for Fx (08-interfaces-core.md §8.3 "Test oracle"): fixed
    /// inputs and their expected Raw outputs, committed as literals. They were
    /// derived once from FxOracle (BigInteger) and pin Fx's results across
    /// processes, machines and time. Each golden test also re-checks every row
    /// against FxOracle, so a mistyped literal fails loudly rather than
    /// silently redefining Fx. Raw operands: value = Raw / 2^32.
    /// Handpicked boundary rows come first; the remainder were drawn from
    /// FxOracle.NextRaw with the seed noted per table.
    /// </summary>
    internal static class FxGolden
    {
        /// <summary>Seed and length of the digest sequence in FxOracle.Apply.</summary>
        internal const ulong DigestSeed = 0x0F1E2D3C4B5A6978UL;
        internal const int DigestLength = 20000;
        internal const ulong DigestExpected = 0x207B97F36E4447B6UL;

        // Random rows: Mul seed 0x6D756C0000000001, Div 0x6469760000000002,
        // FromRatio 0x7261740000000003, Sqrt 0x7371720000000004 (24 rows each).

        internal static readonly (long A, long B, FxOutcome Outcome, long Expected)[] MulGolden =
        {
            (1L, 1L, FxOutcome.Ok, 0L),
            (-1L, 1L, FxOutcome.Ok, -1L),
            (1L, -1L, FxOutcome.Ok, -1L),
            (-1L, -1L, FxOutcome.Ok, 0L),
            (-2147483648L, 1L, FxOutcome.Ok, -1L),
            (171798691840000L, 171798691840000L, FxOutcome.Ok, 6871947673600000000L),
            (281474976710656L, 140737488355328L, FxOutcome.Overflow, 0L),
            (-281474976710656L, 140737488355328L, FxOutcome.Ok, long.MinValue),
            (long.MinValue, 4294967296L, FxOutcome.Ok, long.MinValue),
            (long.MinValue, -4294967296L, FxOutcome.Overflow, 0L),
            (long.MinValue, long.MinValue, FxOutcome.Overflow, 0L),
            (long.MaxValue, long.MaxValue, FxOutcome.Overflow, 0L),
            (long.MaxValue, 1L, FxOutcome.Ok, 2147483647L),
            (long.MinValue, 1L, FxOutcome.Ok, -2147483648L),
            (12884901888L, -1431655765L, FxOutcome.Ok, -4294967295L),
            (-42896069L, 930974711704245165L, FxOutcome.Ok, -9298127952618620L),
            (44099823L, -68669112124304403L, FxOutcome.Ok, -705080034734909L),
            (828749L, -13018L, FxOutcome.Ok, -3L),
            (14898074L, 3528561274871741L, FxOutcome.Ok, 12239619853574L),
            (47270L, 1653593477L, FxOutcome.Ok, 18199L),
            (6745L, -2147483648L, FxOutcome.Ok, -3373L),
            (-187911028838688392L, 4334622460920603L, FxOutcome.Overflow, 0L),
            (-38160287944118853L, -191695055732896969L, FxOutcome.Overflow, 0L),
            (61L, 4294967296L, FxOutcome.Ok, 61L),
            (-105L, -233179897L, FxOutcome.Ok, 5L),
            (3743948004126981L, -181505904187L, FxOutcome.Ok, -158219753698953822L),
            (-559638269680182L, 1L, FxOutcome.Ok, -130301L),
            (-2061378488165L, -5469L, FxOutcome.Ok, 2624857L),
            (2147483648L, -25871636893393L, FxOutcome.Ok, -12935818446697L),
            (109623166L, -7752428511L, FxOutcome.Ok, -197870135L),
            (4L, 2147483648L, FxOutcome.Ok, 2L),
            (-7120775534L, 19598551007466L, FxOutcome.Ok, -32493118782485L),
            (2338564992037L, -3L, FxOutcome.Ok, -1634L),
            (27376893L, 250511550L, FxOutcome.Ok, 1596805L),
            (-25981286L, -222L, FxOutcome.Ok, 1L),
            (-696039707L, 1008384222594L, FxOutcome.Ok, -163418114847L),
            (12863357029L, 57917594L, FxOutcome.Ok, 173462249L),
            (62250L, 1927446422982L, FxOutcome.Ok, 27935844L),
            (-1373843470765L, -22792521L, FxOutcome.Ok, 7290708869L),
        };

        internal static readonly (long A, long B, FxOutcome Outcome, long Expected)[] DivGolden =
        {
            (4294967296L, 12884901888L, FxOutcome.Ok, 1431655765L),
            (-4294967296L, 12884901888L, FxOutcome.Ok, -1431655766L),
            (-1L, 8589934592L, FxOutcome.Ok, -1L),
            (1L, 8589934592L, FxOutcome.Ok, 0L),
            (long.MinValue, -1L, FxOutcome.Overflow, 0L),
            (long.MinValue, -4294967296L, FxOutcome.Overflow, 0L),
            (long.MinValue, 4294967296L, FxOutcome.Ok, long.MinValue),
            (4294967296L, 1L, FxOutcome.Overflow, 0L),
            (1L, long.MaxValue, FxOutcome.Ok, 0L),
            (-1L, long.MaxValue, FxOutcome.Ok, -1L),
            (long.MaxValue, long.MaxValue, FxOutcome.Ok, 4294967296L),
            (long.MinValue, long.MinValue, FxOutcome.Ok, 4294967296L),
            (30064771072L, 0L, FxOutcome.DivideByZero, 0L),
            (0L, 0L, FxOutcome.DivideByZero, 0L),
            (long.MaxValue, -4294967296L, FxOutcome.Ok, -9223372036854775807L),
            (3037822292684754881L, 4294967297L, FxOutcome.Ok, 3037822291977456761L),
            (5060360268L, -677062070L, FxOutcome.Ok, -32100575147L),
            (492879242605L, 1L, FxOutcome.Overflow, 0L),
            (-27577L, 3629L, FxOutcome.Ok, -32637727507L),
            (-1L, 9223372036854775806L, FxOutcome.Ok, -1L),
            (11234L, 49691261L, FxOutcome.Ok, 970988L),
            (-16844853507388L, -634420392110132L, FxOutcome.Ok, 114038098L),
            (-15255041729191L, 3665097597842L, FxOutcome.Ok, -17876715034L),
            (63953269568256336L, 467416421L, FxOutcome.Ok, 587649874773939537L),
            (long.MinValue, -176208461506L, FxOutcome.Ok, 224813728685686790L),
            (0L, -77050719686893996L, FxOutcome.Ok, 0L),
            (48762808770435L, -109237L, FxOutcome.Ok, -1917250280858319953L),
            (23892L, 1L, FxOutcome.Ok, 102615358636032L),
            (20002441L, -17521896042231529L, FxOutcome.Ok, -5L),
            (8530767881L, -1002812388457987L, FxOutcome.Ok, -36537L),
            (9223372036854775806L, -4294967296L, FxOutcome.Ok, -9223372036854775806L),
            (-66881945L, -4339189320413L, FxOutcome.Ok, 66200L),
            (-890635662069L, -38L, FxOutcome.Overflow, 0L),
            (16145716396L, -48L, FxOutcome.Ok, -1444694247735645526L),
            (-2102319139870L, -11136074531L, FxOutcome.Ok, 810823591954L),
            (1031492L, -3736279L, FxOutcome.Ok, -1185731689L),
            (122750244861L, -59L, FxOutcome.Ok, -8935733682270967218L),
            (-25L, 3049716016298668L, FxOutcome.Ok, -1L),
            (-340021L, -26049155L, FxOutcome.Ok, 56062435L),
        };

        internal static readonly (long A, long B, FxOutcome Outcome, long Expected)[] FromRatioGolden =
        {
            (1L, 3L, FxOutcome.Ok, 1431655765L),
            (-1L, 3L, FxOutcome.Ok, -1431655766L),
            (1L, -3L, FxOutcome.Ok, -1431655766L),
            (-1L, -3L, FxOutcome.Ok, 1431655765L),
            (0L, 5L, FxOutcome.Ok, 0L),
            (0L, -5L, FxOutcome.Ok, 0L),
            (7L, 1L, FxOutcome.Ok, 30064771072L),
            (1L, 0L, FxOutcome.DivideByZero, 0L),
            (0L, 0L, FxOutcome.DivideByZero, 0L),
            (long.MinValue, 1L, FxOutcome.Overflow, 0L),
            (long.MinValue, -1L, FxOutcome.Overflow, 0L),
            (2147483648L, 1L, FxOutcome.Overflow, 0L),
            (-2147483648L, 1L, FxOutcome.Ok, long.MinValue),
            (long.MaxValue, long.MaxValue, FxOutcome.Ok, 4294967296L),
            (long.MinValue, long.MinValue, FxOutcome.Ok, 4294967296L),
            (long.MaxValue, long.MinValue, FxOutcome.Ok, -4294967296L),
            (1L, long.MaxValue, FxOutcome.Ok, 0L),
            (-1L, long.MaxValue, FxOutcome.Ok, -1L),
            (-13328L, 12020608086073L, FxOutcome.Ok, -5L),
            (22L, -1178L, FxOutcome.Ok, -80211614L),
            (-3153820090623942335L, -3434L, FxOutcome.Overflow, 0L),
            (4294967297L, 22384493776084L, FxOutcome.Ok, 824085L),
            (-276107801072321202L, 205762L, FxOutcome.Overflow, 0L),
            (-13350680281399L, 763940L, FxOutcome.Ok, -75059213011441713L),
            (-9223372036854775807L, 2437850269L, FxOutcome.Overflow, 0L),
            (long.MinValue, 17260646134321L, FxOutcome.Ok, -2295052047812028L),
            (-9223372036854775807L, -697055334539L, FxOutcome.Ok, 56830611996293064L),
            (-6L, -193803660555267643L, FxOutcome.Ok, 0L),
            (-31921816L, 18L, FxOutcome.Ok, -7616841986051641L),
            (-2181025879446408L, -13737427111092L, FxOutcome.Ok, 681891503277L),
            (150638227677195L, 343940622220121661L, FxOutcome.Ok, 1881098L),
            (-4072969L, 16L, FxOutcome.Ok, -1093329290788864L),
            (6762473226159399418L, 13L, FxOutcome.Overflow, 0L),
            (46454813305405536L, -727067225980237L, FxOutcome.Ok, -274420159181L),
            (217483135L, -51556644869L, FxOutcome.Ok, -18117606L),
            (-6562133L, -2467429356042843L, FxOutcome.Ok, 11L),
            (1L, 0L, FxOutcome.DivideByZero, 0L),
            (-10495267933123L, 61912590397877908L, FxOutcome.Ok, -728073L),
            (61503132845329578L, 1L, FxOutcome.Overflow, 0L),
            (3396080925414L, -61112772386316719L, FxOutcome.Ok, -238675L),
            (-2L, -6L, FxOutcome.Ok, 1431655765L),
            (30688353977L, -26767L, FxOutcome.Ok, -4924178155911628L),
        };

        internal static readonly (long A, FxOutcome Outcome, long Expected)[] SqrtGolden =
        {
            (0L, FxOutcome.Ok, 0L),
            (4294967296L, FxOutcome.Ok, 4294967296L),
            (17179869184L, FxOutcome.Ok, 8589934592L),
            (8589934592L, FxOutcome.Ok, 6074000999L),
            (1L, FxOutcome.Ok, 65536L),
            (long.MaxValue, FxOutcome.Ok, 199032864766430L),
            (-1L, FxOutcome.ArgumentOutOfRange, 0L),
            (long.MinValue, FxOutcome.ArgumentOutOfRange, 0L),
            (1073741824L, FxOutcome.Ok, 2147483648L),
            (9663676416L, FxOutcome.Ok, 6442450944L),
            (2844249631227382344L, FxOutcome.Ok, 110525830228873L),
            (-1692293735411833510L, FxOutcome.ArgumentOutOfRange, 0L),
            (-1L, FxOutcome.ArgumentOutOfRange, 0L),
            (32120867436934L, FxOutcome.Ok, 371427079196L),
            (200L, FxOutcome.Ok, 926819L),
            (40218347051087L, FxOutcome.Ok, 415615790464L),
            (-297028873004557347L, FxOutcome.ArgumentOutOfRange, 0L),
            (-546721000178L, FxOutcome.ArgumentOutOfRange, 0L),
            (4294967296L, FxOutcome.Ok, 4294967296L),
            (-53787373468842L, FxOutcome.ArgumentOutOfRange, 0L),
            (87428L, FxOutcome.Ok, 19377832L),
            (-70853973895777L, FxOutcome.ArgumentOutOfRange, 0L),
            (-33504714371L, FxOutcome.ArgumentOutOfRange, 0L),
            (-4294967296L, FxOutcome.ArgumentOutOfRange, 0L),
            (4076L, FxOutcome.Ok, 4184051L),
            (-990303L, FxOutcome.ArgumentOutOfRange, 0L),
            (-12339982474418L, FxOutcome.ArgumentOutOfRange, 0L),
            (-247423361L, FxOutcome.ArgumentOutOfRange, 0L),
            (4294967297L, FxOutcome.Ok, 4294967296L),
            (-1L, FxOutcome.ArgumentOutOfRange, 0L),
            (-2878358410L, FxOutcome.ArgumentOutOfRange, 0L),
            (2958630458L, FxOutcome.Ok, 3564718931L),
            (long.MinValue, FxOutcome.ArgumentOutOfRange, 0L),
            (-1837406944087546289L, FxOutcome.ArgumentOutOfRange, 0L),
        };

        internal static readonly (string Text, FxOutcome Outcome, long Expected)[] ParseGolden =
        {
            ("0", FxOutcome.Ok, 0L),
            ("1", FxOutcome.Ok, 4294967296L),
            ("-1", FxOutcome.Ok, -4294967296L),
            ("0.5", FxOutcome.Ok, 2147483648L),
            ("2.25", FxOutcome.Ok, 9663676416L),
            ("0.1", FxOutcome.Ok, 429496729L),
            ("-0.1", FxOutcome.Ok, -429496730L),
            ("2147483647.9999999999", FxOutcome.Ok, long.MaxValue),
            ("2147483648", FxOutcome.Overflow, 0L),
            ("-2147483648", FxOutcome.Ok, long.MinValue),
            ("-2147483648.0000000001", FxOutcome.Overflow, 0L),
            ("-0", FxOutcome.Ok, 0L),
            ("-0.0", FxOutcome.Ok, 0L),
            ("0.0000000001", FxOutcome.Ok, 0L),
            ("-0.0000000001", FxOutcome.Ok, -1L),
            ("1.0000000001", FxOutcome.Ok, 4294967296L),
            ("123456.789", FxOutcome.Ok, 530242871224172L),
            ("-123456.789", FxOutcome.Ok, -530242871224173L),
            ("3.1415926535", FxOutcome.Ok, 13493037704L),
        };

        internal static readonly (long Raw, int Decimals, string Expected)[] DisplayGolden =
        {
            (1073741824L, 1, "0.2"),
            (-1073741824L, 1, "-0.3"),
            (0L, 0, "0"),
            (0L, 3, "0.000"),
            (-1L, 0, "-1"),
            (-1L, 10, "-0.0000000003"),
            (1L, 10, "0.0000000002"),
            (-21474836480L, 2, "-5.00"),
            (long.MinValue, 0, "-2147483648"),
            (long.MaxValue, 10, "2147483647.9999999997"),
            (long.MinValue, 10, "-2147483648.0000000000"),
            (6442450944L, 0, "1"),
            (-6442450944L, 0, "-2"),
        };
    }
}
