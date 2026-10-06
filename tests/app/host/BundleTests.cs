using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. The scenario bundle (16 §16.3) as ISimComposer.Compose reads it:
    /// listed systems need their files, unlisted ones are skipped, and
    /// bundle.json has the strict form (Q-069). A load failure throws
    /// FormatException (07 "Error handling", Q-030).
    /// </summary>
    public sealed class BundleTests
    {
        private static Exception? TryCompose(MemoryBundle bundle, string content)
        {
            return Record.Exception(() => ComposeTests.Compose(bundle, content, new RecordingSink()));
        }

        private static void AssertLoadFailure(MemoryBundle bundle, string content, string label, string named)
        {
            Exception? e = TryCompose(bundle, content);
            Assert.True(e is FormatException, label + ": expected a FormatException load failure, got " + (e == null ? "no exception" : e.GetType().Name + ": " + e.Message));
            Assert.True(e!.Message.Contains(named, StringComparison.Ordinal), label + ": the message does not name " + named + ": " + e.Message);
        }

        [Fact]
        public void test_bundle_rejects_listed_system_without_file()
        {
            // Control: the whole Phase 1 bundle composes.
            Exception? control = TryCompose(B.Phase1Bundle(), B.Phase1Content);
            Assert.True(control == null, "the complete Phase 1 bundle failed to compose: " + control);

            // Each file of a listed system, removed in turn, is a hard load
            // failure naming that file. sim.airside needs two (16 §16.3).
            foreach (string file in B.Phase1Files)
            {
                AssertLoadFailure(B.Phase1Bundle().Remove(file), B.Phase1Content, "no " + file, file);
            }

            AssertLoadFailure(B.Phase0Bundle().Remove("schedule.csv"), B.Phase0Content, "Phase 0 bundle, no schedule.csv", "schedule.csv");
            AssertLoadFailure(B.Phase1Bundle().Remove("bundle.json"), B.Phase1Content, "no bundle.json", "bundle.json");
        }

        [Fact]
        public void test_bundle_unlisted_system_is_not_registered()
        {
            // Every Phase 1 file is present, but only four systems are listed,
            // out of registry order. sim.airside and sim.delay are skipped, never
            // reordered (08 §8.5).
            string[] listed = { B.Turnaround, B.Flow, B.Schedule, B.World };
            MemoryBundle bundle = B.Phase1Bundle().Put("bundle.json", B.BundleJson("12345", listed));
            var sink = new RecordingSink();
            ComposedSim sim = ComposeTests.Compose(bundle, B.Phase1Content, sink);
            Assert.Null(sim.Airside);
            Assert.Null(sim.Delay);
            Assert.NotNull(sim.World);
            Assert.NotNull(sim.Schedule);
            Assert.NotNull(sim.Flow);
            Assert.NotNull(sim.Turnaround);
            Assert.Equal(new List<string> { B.World, B.Schedule, B.Flow, B.Turnaround }, ComposeTests.Names(sim));

            sim.Host.Step(B.Day);
            Assert.All(sink.SystemHashes, h => Assert.Equal(4, h.Length));
            KitRun kit = Kit.Compose(bundle, B.Phase1Content, B.Seed, listed);
            kit.Host.Step(B.Day);
            Dumps.AssertBytesEqual(kit.Dump(B.Seed), Dumps.Render(B.Seed, ComposeTests.Names(sim), sink), "the four-system day");

            // The Phase 0 bundle: world, schedule and flow, nothing else.
            ComposedSim phase0 = ComposeTests.Compose(B.Phase0Bundle(), B.Phase0Content, new RecordingSink());
            Assert.Null(phase0.Airside);
            Assert.Null(phase0.Turnaround);
            Assert.Null(phase0.Delay);
            Assert.Equal(new List<string> { B.World, B.Schedule, B.Flow }, ComposeTests.Names(phase0));

            // sim.delay alone needs no file (16 §16.3).
            var delaySink = new RecordingSink();
            ComposedSim delay = ComposeTests.Compose(new MemoryBundle().Put("bundle.json", B.BundleJson("12345", B.Delay)), B.Phase1Content, delaySink);
            Assert.Equal(new List<string> { B.Delay }, ComposeTests.Names(delay));
            delay.Host.Step(1);
            Assert.Single(delaySink.SystemHashes);
            Assert.Single(delaySink.SystemHashes[0]);
        }

        [Fact]
        public void test_bundle_json_strict_form_rejects_malformed()
        {
            // 16 §16.3 (Q-069): exactly three keys, schema_version 1, seed a
            // string of 1 to 20 ASCII digits fitting uint64 with no sign and no
            // leading zero, systems a non-empty array of distinct Phase 1 module
            // names. Anything else is a load failure naming bundle.json.
            const string Systems = "[ \"sim.world\", \"sim.schedule\", \"sim.flow\" ]";
            string[] bad =
            {
                "{ \"schema_version\": 1, \"schema_version\": 1, \"seed\": \"12345\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"seed\": \"12345\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": " + Systems + ", \"extra\": 1 }",
                "{ \"seed\": \"12345\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"12345\" }",
                "{ \"schema_version\": 2, \"seed\": \"12345\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": 12345, \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"012345\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"-1\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"+1\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \" 1\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"1.0\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"18446744073709551616\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"123456789012345678901\", \"systems\": " + Systems + " }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ \"sim.world\", \"sim.schedule\", \"sim.flow\", \"sim.world\" ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ \"sim.world\", \"sim.schedule\", \"sim.flow\", \"sim.baggage\" ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ \"sim.world\", \"sim.schedule\", \"sim.flow\", \"sim.core\" ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ \"sim.world\", \"sim.schedule\", \"SIM.FLOW\" ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": [ \"sim.world\", \"sim.schedule\", 4 ] }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": \"sim.world\" }",
                "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": " + Systems,
                string.Empty,
            };

            // Control: the same text in its valid form composes.
            string valid = "{ \"schema_version\": 1, \"seed\": \"12345\", \"systems\": " + Systems + " }";
            Exception? control = TryCompose(B.Phase0Bundle().Put("bundle.json", B.Utf8(valid)), B.Phase0Content);
            Assert.True(control == null, "the valid bundle.json failed: " + control);

            foreach (string text in bad)
            {
                AssertLoadFailure(B.Phase0Bundle().Put("bundle.json", B.Utf8(text)), B.Phase0Content, "bundle.json `" + text + "`", "bundle.json");
            }
        }

        [Fact]
        public void test_bundle_seed_is_the_master_seed()
        {
            // 16 §16.4 step 1: MasterSeed is the bundle's seed, parsed in full
            // over uint64, "0" and the maximum included. Two checkpoints (ticks 0
            // and 600) of each are compared with the reference composition.
            foreach ((string text, ulong seed) in new[] { ("0", 0UL), ("12345", 12345UL), ("18446744073709551615", ulong.MaxValue), ("9007199254740993", 9007199254740993UL) })
            {
                MemoryBundle bundle = B.Phase1Bundle().Put("bundle.json", B.BundleJson(text, B.RegistryOrder));
                var sink = new RecordingSink();
                ComposedSim sim = ComposeTests.Compose(bundle, B.Phase1Content, sink);
                sim.Host.Step(601);

                KitRun kit = Kit.Compose(bundle, B.Phase1Content, seed, B.RegistryOrder);
                kit.Host.Step(601);
                Dumps.AssertBytesEqual(kit.Dump(seed), Dumps.Render(seed, ComposeTests.Names(sim), sink), "seed " + text);
            }
        }
    }
}
