using System;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Tools.SimHarness.Tests.HarnessTestKit;

namespace AirportSim.Tools.SimHarness.Tests
{
    /// <summary>
    /// T-006. HarnessGates in process, against 19-interfaces-harness.md §19.1–§19.3
    /// (Q-025, Q-026, Q-027): one run per composer call, the NoOp script, the run
    /// comparison and its first-difference location, the divergence seam, the
    /// interim save/load replay and promotion's vacuous pass.
    /// </summary>
    public sealed class HarnessGatesTests
    {
        private static readonly IContentIndex Content = new EmptyContent();

        // ------------------------------------------------------------ passing gates

        [Fact]
        public void test_harness_gates_same_process_deterministic_composer_passes_with_exact_report()
        {
            const uint ticks = 1400;
            GateResult r = HarnessGates.SameProcess(Content, NoSystems, 12345UL, ticks);
            Assert.True(r.Passed);
            Assert.Equal("PASS determinism_same_process ticks=1400 checkpoints=3 final="
                + Hex16(EmptyCompositionFinalHash(ticks)), r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_with_probe_systems_passes()
        {
            var composer = new CountingComposer((b, call) =>
            {
                b.Register(new Probe(5));
                b.Register(new Probe(9, salt: 7));
            });
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 1UL, 1300);
            Assert.True(r.Passed, r.Report);
            Assert.StartsWith("PASS determinism_same_process ticks=1300 checkpoints=3 final=", r.Report);
            Assert.Equal(2, composer.Calls);
        }

        [Fact]
        public void test_harness_gates_save_load_deterministic_composer_passes_with_exact_report()
        {
            // §19.2: B replays A's CommandLogSince(0) (which holds the pending script
            // too) and must equal the uninterrupted run U.
            GateResult r = HarnessGates.SaveLoad(Content, NoSystems, 12345UL, 1000, 500);
            Assert.True(r.Passed, r.Report);
            Assert.Equal("PASS determinism_save_load ticks=1000 checkpoints=2 final="
                + Hex16(EmptyCompositionFinalHash(1000)), r.Report);
        }

        [Theory]
        [InlineData(1000u, 1u)]
        [InlineData(1000u, 100u)]
        [InlineData(1000u, 101u)]
        [InlineData(1000u, 999u)]
        [InlineData(2u, 1u)]
        [InlineData(1301u, 600u)]
        public void test_harness_gates_save_load_any_valid_save_point_passes(uint ticks, uint saveAt)
        {
            var composer = new CountingComposer((b, call) => b.Register(new Probe(6)));
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 99UL, ticks, saveAt);
            Assert.True(r.Passed, r.Report);
            Assert.Equal("PASS determinism_save_load ticks=" + ticks + " checkpoints=" + CheckpointCount(ticks) + " final=",
                r.Report.Substring(0, r.Report.Length - 16));
        }

        [Fact]
        public void test_harness_gates_promotion_passes_vacuously_with_exact_report()
        {
            // §19.2: before T-010 the second run differs in nothing; the gate still compares.
            GateResult r = HarnessGates.Promotion(Content, NoSystems, 12345UL, TicksPerDay);
            Assert.True(r.Passed, r.Report);
            Assert.Equal("PASS determinism_promotion ticks=14400 checkpoints=24 final="
                + Hex16(EmptyCompositionFinalHash(TicksPerDay)), r.Report);
        }

        [Fact]
        public void test_harness_gates_promotion_really_compares_runs()
        {
            // A stubbed comparison is wrong (§19.2): a divergent composer must fail it.
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call == 2, driftFromTick: 1000)));
            GateResult r = HarnessGates.Promotion(Content, composer.Compose, 12345UL, 1400);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_promotion tick=1200 at=system:0", r.Report);
        }

        // ------------------------------------------------------------ one run

        [Fact]
        public void test_harness_gates_compose_called_exactly_once_per_run_with_fresh_builder()
        {
            var same = new CountingComposer((b, call) => { });
            HarnessGates.SameProcess(Content, same.Compose, 1UL, 10);
            Assert.Equal(2, same.Calls);
            Assert.NotSame(same.Builders[0], same.Builders[1]);

            var promotion = new CountingComposer((b, call) => { });
            HarnessGates.Promotion(Content, promotion.Compose, 1UL, 10);
            Assert.Equal(2, promotion.Calls);
            Assert.NotSame(promotion.Builders[0], promotion.Builders[1]);

            var saveLoad = new CountingComposer((b, call) => { });
            HarnessGates.SaveLoad(Content, saveLoad.Compose, 1UL, 10, 5);
            Assert.Equal(3, saveLoad.Calls);
            Assert.NotSame(saveLoad.Builders[0], saveLoad.Builders[1]);
            Assert.NotSame(saveLoad.Builders[1], saveLoad.Builders[2]);
            Assert.NotSame(saveLoad.Builders[0], saveLoad.Builders[2]);

            var final = new CountingComposer((b, call) => { });
            HarnessGates.FinalHash(Content, final.Compose, 1UL, 10);
            Assert.Equal(1, final.Calls);
        }

        [Fact]
        public void test_harness_gates_run_builds_with_seed_and_content_and_steps_all_ticks()
        {
            Probe? probe = null;
            var content = new EmptyContent();
            var composer = new CountingComposer((b, call) =>
            {
                probe = new Probe(4);
                b.Register(probe);
            });
            HarnessGates.FinalHash(content, composer.Compose, 0xFEEDFACECAFEBEEFUL, 777);
            Assert.NotNull(probe);
            Assert.Equal(777UL, probe!.TicksSeen);
            Assert.Equal(0xFEEDFACECAFEBEEFUL, probe.ObservedMasterSeed);
            Assert.Same(content, probe.ObservedContent);
        }

        // ------------------------------------------------------------ FinalHash and the script

        [Theory]
        [InlineData(1u)]
        [InlineData(99u)]
        [InlineData(100u)]
        [InlineData(101u)]
        [InlineData(200u)]
        [InlineData(201u)]
        [InlineData(1000u)]
        [InlineData(14400u)]
        public void test_harness_gates_final_hash_includes_noop_every_100_ticks(uint ticks)
        {
            // With no systems the world hash is the tick count and the core section,
            // whose next Sequence counts the scripted NoOps (§19.2, 08 §8.9).
            string hash = HarnessGates.FinalHash(Content, NoSystems, 12345UL, ticks);
            Assert.Equal(Hex16(EmptyCompositionFinalHash(ticks)), hash);
        }

        [Fact]
        public void test_harness_gates_final_hash_is_sixteen_lowercase_hex_digits()
        {
            var composer = new CountingComposer((b, call) => b.Register(new Probe(3, salt: 0xABCDEFUL)));
            string hash = HarnessGates.FinalHash(Content, composer.Compose, 5UL, 50);
            Assert.Equal(16, hash.Length);
            foreach (char c in hash)
            {
                Assert.True((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'), "not lowercase hex: " + hash);
            }
        }

        [Fact]
        public void test_harness_gates_final_hash_same_inputs_same_output()
        {
            var a = new CountingComposer((b, call) => { b.Register(new Probe(5)); b.Register(new Probe(9, salt: 3)); });
            var c = new CountingComposer((b, call) => { b.Register(new Probe(5)); b.Register(new Probe(9, salt: 3)); });
            Assert.Equal(HarnessGates.FinalHash(Content, a.Compose, 42UL, 2000), HarnessGates.FinalHash(Content, c.Compose, 42UL, 2000));
        }

        [Fact]
        public void test_harness_gates_final_hash_seed_reaches_state()
        {
            SimComposer seeded = b => b.Register(new Probe(5) { FeedMasterSeed = true });
            Assert.NotEqual(HarnessGates.FinalHash(Content, seeded, 1UL, 10), HarnessGates.FinalHash(Content, seeded, 2UL, 10));
            // With no seed-dependent state, the seed is not in the hash (08 §8.9).
            Assert.Equal(HarnessGates.FinalHash(Content, NoSystems, 1UL, 10), HarnessGates.FinalHash(Content, NoSystems, 2UL, 10));
        }

        // ------------------------------------------------------------ divergence seam

        [Fact]
        public void test_harness_gates_same_process_divergent_system_fails_at_system_index()
        {
            // The second run's probe at registry index 1 drifts from tick 1000, so the
            // first differing checkpoint is 1200 (08 §8.9: checkpoints at 0, 600, 1200).
            var composer = new CountingComposer((b, call) =>
            {
                b.Register(new Probe(5));
                b.Register(new Probe(9, drifts: call == 2, driftFromTick: 1000));
            });
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 1400);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=1200 at=system:1", r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_divergent_first_system_fails_at_system_zero()
        {
            var composer = new CountingComposer((b, call) =>
            {
                b.Register(new Probe(5, drifts: call == 2, driftFromTick: 600));
                b.Register(new Probe(9, drifts: call == 2, driftFromTick: 600));
            });
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 1400);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=600 at=system:0", r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_system_count_mismatch_fails_at_shorter_length()
        {
            // §19.2: a SystemHashes length mismatch counts at the shorter length.
            var composer = new CountingComposer((b, call) =>
            {
                b.Register(new Probe(5));
                if (call == 1) b.Register(new Probe(9));
            });
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 700);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=0 at=system:1", r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_core_section_difference_fails_at_core()
        {
            // An id allocated during the second composition only (08 §8.4: Next is
            // callable during construction) puts a non-zero counter in the core
            // section; CoreHash is compared before SystemHashes.
            var composer = new CountingComposer((b, call) =>
            {
                if (call == 2) b.Services.Ids.Next(new SystemId(5));
                b.Register(new Probe(5, drifts: call == 2, driftFromTick: 0));
            });
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 700);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=0 at=core", r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_divergence_after_last_checkpoint_fails_at_final()
        {
            // Checkpoints at 0 and 600 agree; the drift at 650 shows only in the final hash.
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call == 2, driftFromTick: 650)));
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 700);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=700 at=final", r.Report);
        }

        [Fact]
        public void test_harness_gates_same_process_divergence_in_first_run_is_found_too()
        {
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call == 1, driftFromTick: 1)));
            GateResult r = HarnessGates.SameProcess(Content, composer.Compose, 12345UL, 1400);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_same_process tick=600 at=system:0", r.Report);
        }

        [Fact]
        public void test_harness_gates_save_load_runs_u_then_a_then_b()
        {
            // Q-029: U runs to the end first, then A stops at saveAt, then B runs to the end.
            var probes = new System.Collections.Generic.List<Probe>();
            var composer = new CountingComposer((b, call) =>
            {
                var p = new Probe(5);
                probes.Add(p);
                b.Register(p);
            });
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1000, 500);
            Assert.True(r.Passed, r.Report);
            Assert.Equal(new ulong[] { 1000, 500, 1000 }, probes.ConvertAll(p => p.TicksSeen).ToArray());
        }

        [Fact]
        public void test_harness_gates_save_load_every_run_different_fails_at_reload()
        {
            // All three compositions differ. The reload check (B against A at saveAt)
            // comes first, so it is what the gate reports.
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, salt: (ulong)call)));
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1000, 500);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_save_load tick=500 at=reload", r.Report);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void test_harness_gates_save_load_divergence_before_save_point_fails_at_reload(int divergentCall)
        {
            // Call 2 is A and call 3 is B (Q-029). Either one drifting before saveAt makes
            // B's hash at saveAt differ from A's, and B is not stepped past saveAt.
            var probes = new System.Collections.Generic.List<Probe>();
            var composer = new CountingComposer((b, call) =>
            {
                var p = new Probe(5, drifts: call == divergentCall, driftFromTick: 100);
                probes.Add(p);
                b.Register(p);
            });
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1000, 500);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_save_load tick=500 at=reload", r.Report);
            Assert.Equal(3, probes.Count);
            Assert.Equal(500UL, probes[2].TicksSeen);
        }

        [Fact]
        public void test_harness_gates_save_load_reload_divergence_wins_over_later_u_divergence()
        {
            // B differs from A before saveAt and from U everywhere: reload is reported.
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call == 3, driftFromTick: 0)));
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1300, 700);
            Assert.Equal("FAIL determinism_save_load tick=700 at=reload", r.Report);
        }

        [Fact]
        public void test_harness_gates_save_load_divergence_from_u_before_save_point_fails_at_checkpoint()
        {
            // A and B drift alike from tick 100, so the reload check passes; B then differs
            // from U at the first checkpoint after the drift.
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call >= 2, driftFromTick: 100)));
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1000, 500);
            Assert.False(r.Passed);
            Assert.Equal("FAIL determinism_save_load tick=600 at=system:0", r.Report);
        }

        [Theory]
        [InlineData(550UL, "FAIL determinism_save_load tick=600 at=system:0")]
        [InlineData(700UL, "FAIL determinism_save_load tick=1000 at=final")]
        public void test_harness_gates_save_load_divergence_after_save_point_fails_against_u(ulong driftFrom, string report)
        {
            // B alone drifts after saveAt: the reload check passes and the B-versus-U
            // comparison locates the difference (checkpoints at 0 and 600).
            var composer = new CountingComposer((b, call) => b.Register(new Probe(5, drifts: call == 3, driftFromTick: driftFrom)));
            GateResult r = HarnessGates.SaveLoad(Content, composer.Compose, 12345UL, 1000, 500);
            Assert.False(r.Passed);
            Assert.Equal(report, r.Report);
        }

        // ------------------------------------------------------------ arguments and exceptions

        [Fact]
        public void test_harness_gates_null_arguments_throw_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => HarnessGates.SameProcess(null!, NoSystems, 1UL, 10));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.SameProcess(Content, null!, 1UL, 10));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.SaveLoad(null!, NoSystems, 1UL, 10, 5));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.SaveLoad(Content, null!, 1UL, 10, 5));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.Promotion(null!, NoSystems, 1UL, 10));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.Promotion(Content, null!, 1UL, 10));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.FinalHash(null!, NoSystems, 1UL, 10));
            Assert.Throws<ArgumentNullException>(() => HarnessGates.FinalHash(Content, null!, 1UL, 10));
        }

        [Fact]
        public void test_harness_gates_zero_ticks_throws_argument_out_of_range()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => HarnessGates.SameProcess(Content, NoSystems, 1UL, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HarnessGates.SaveLoad(Content, NoSystems, 1UL, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HarnessGates.Promotion(Content, NoSystems, 1UL, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => HarnessGates.FinalHash(Content, NoSystems, 1UL, 0));
        }

        [Theory]
        [InlineData(10u, 0u)]
        [InlineData(10u, 10u)]
        [InlineData(10u, 11u)]
        [InlineData(1u, 0u)]
        [InlineData(1u, 1u)]
        public void test_harness_gates_save_load_save_point_outside_run_throws_argument_out_of_range(uint ticks, uint saveAt)
        {
            var composer = new CountingComposer((b, call) => { });
            Assert.Throws<ArgumentOutOfRangeException>(() => HarnessGates.SaveLoad(Content, composer.Compose, 1UL, ticks, saveAt));
            Assert.Equal(0, composer.Calls);
        }

        [Fact]
        public void test_harness_gates_composer_exception_propagates_unchanged()
        {
            var boom = new ComposerFailure();
            SimComposer compose = b => throw boom;
            Assert.Same(boom, Assert.Throws<ComposerFailure>(() => HarnessGates.SameProcess(Content, compose, 1UL, 10)));
            Assert.Same(boom, Assert.Throws<ComposerFailure>(() => HarnessGates.FinalHash(Content, compose, 1UL, 10)));
        }

        [Fact]
        public void test_harness_gates_sim_invariant_exception_propagates_unchanged()
        {
            // An exception escaping a tick leaves Step as SimInvariantException (08 §8.5a),
            // and the harness lets it through as is.
            SimComposer compose = b => b.Register(new Probe(5)
            {
                OnTick = ctx =>
                {
                    if (ctx.Tick == 42) throw new ComposerFailure();
                },
            });
            var ex = Assert.Throws<SimInvariantException>(() => HarnessGates.SameProcess(Content, compose, 1UL, 100));
            Assert.Equal(42UL, ex.Tick);
            Assert.IsType<ComposerFailure>(ex.InnerException);
            Assert.Throws<SimInvariantException>(() => HarnessGates.FinalHash(Content, compose, 1UL, 100));
        }

        private sealed class ComposerFailure : Exception
        {
        }
    }
}
