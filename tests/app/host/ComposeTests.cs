using System;
using System.Collections.Generic;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-031. ISimComposer.Compose (16 §16.4) over the Phase 1 checkpoints
    /// bundle with content data/, compared with the test's own composition
    /// (Kit), which follows §16.4 steps 1 to 4 through the published factories.
    /// </summary>
    public sealed class ComposeTests
    {
        /// <summary>The registered systems of a composed sim, in 08 §8.5 registry order.</summary>
        internal static List<ISimSystem> Registered(in ComposedSim sim)
        {
            var list = new List<ISimSystem>();
            ISimSystem?[] all = { sim.World, sim.Schedule, sim.Airside, sim.Flow, sim.Turnaround, sim.Delay };
            foreach (ISimSystem? s in all)
            {
                if (s != null)
                {
                    list.Add(s);
                }
            }

            return list;
        }

        internal static List<string> Names(in ComposedSim sim)
        {
            return Registered(sim).ConvertAll(s => s.Name);
        }

        internal static ComposedSim Compose(MemoryBundle bundle, string content, ICheckpointSink sink)
        {
            return HostFactory.CreateSimComposer(B.Content(content)).Compose(bundle, sink);
        }

        [Fact]
        public void test_compose_constructs_in_dependency_order_and_registers_in_registry_order()
        {
            // The systems listed in reverse registry order: registration follows
            // the registry (16 §16.3, §16.4), not the bundle.
            MemoryBundle bundle = B.Phase1Bundle().Put("bundle.json", B.BundleJson("12345", B.Delay, B.Turnaround, B.Flow, B.Airside, B.Schedule, B.World));
            var sink = new RecordingSink();
            ComposedSim sim = Compose(bundle, B.Phase1Content, sink);

            Assert.NotNull(sim.Host);
            Assert.Equal(0UL, sim.Host.CurrentTick);
            List<ISimSystem> registered = Registered(sim);
            Assert.Equal(6, registered.Count);
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal(B.RegistryOrder[i], registered[i].Name);
                Assert.Equal(B.RegistryIds[i], registered[i].Id.Value);
            }

            // Every checkpoint goes to the given sink, and its system hashes are
            // in registry order (08 §8.9): read right after the Step that ran
            // tick 600, each equals that system's own hash.
            sim.Host.Step(601);
            Assert.Equal(new List<ulong> { 0UL, 600UL }, sink.Ticks);
            ulong[] last = sink.SystemHashes[1];
            Assert.Equal(6, last.Length);
            for (int i = 0; i < 6; i++)
            {
                Assert.True(registered[i].ComputeStateHash() == last[i], "checkpoint column " + i + " is not " + registered[i].Name + "'s hash");
            }

            Assert.Equal(sim.Host.WorldStateHash(), sink.WorldHashes[1]);

            // The whole day equals the reference composition: dependency-order
            // construction (world; flow(world); schedule(flow); airside(schedule,
            // flow, turnaroundRegistered); turnaround(schedule); delay), each with
            // builder.Services, then registry-order registration.
            sim.Host.Step(B.Day - 601);
            KitRun kit = Kit.Compose(B.Phase1Bundle(), B.Phase1Content, B.Seed, B.RegistryOrder);
            kit.Host.Step(B.Day);
            Dumps.AssertBytesEqual(kit.Dump(B.Seed), Dumps.Render(B.Seed, Names(sim), sink), "ISimComposer.Compose's day");
        }

        [Fact]
        public void test_compose_rejects_airside_without_schedule()
        {
            // Control: the same files with sim.schedule listed compose.
            MemoryBundle ok = B.Phase1Bundle().Put("bundle.json", B.BundleJson("12345", B.World, B.Schedule, B.Flow, B.Airside));
            ComposedSim sim = Compose(ok, B.Phase1Content, new RecordingSink());
            Assert.NotNull(sim.Airside);
            Assert.NotNull(sim.Schedule);

            // 16 §16.4: a listed system whose required downward interface is not
            // listed is a load failure. Every file is present, so the failure can
            // only be the missing system.
            var cases = new List<(string Label, string[] Systems)>
            {
                ("airside without schedule", new[] { B.World, B.Flow, B.Airside }),
                ("airside without schedule, everything else listed", new[] { B.World, B.Airside, B.Flow, B.Turnaround, B.Delay }),
                ("turnaround without schedule", new[] { B.World, B.Flow, B.Turnaround }),
                ("flow without world", new[] { B.Schedule, B.Flow }),
            };

            foreach ((string label, string[] systems) in cases)
            {
                MemoryBundle bundle = B.Phase1Bundle().Put("bundle.json", B.BundleJson("12345", systems));
                Exception? e = Record.Exception(() => Compose(bundle, B.Phase1Content, new RecordingSink()));
                Assert.True(e is FormatException, label + ": expected a FormatException load failure (07 \"Error handling\"), got " + (e == null ? "no exception" : e.GetType().Name + ": " + e.Message));
            }
        }

        [Fact]
        public void test_compose_same_bundle_twice_gives_identical_checkpoints()
        {
            // 16 §16.4: composition is a pure function of the bundle's bytes and
            // the content data. Two composers, two sessions, one day each.
            var first = new RecordingSink();
            ComposedSim a = Compose(B.Phase1Bundle(), B.Phase1Content, first);
            a.Host.Step(B.Day);

            var second = new RecordingSink();
            ComposedSim b = Compose(B.Phase1Bundle(), B.Phase1Content, second);
            b.Host.Step(B.Day);

            Assert.Equal(24, first.Ticks.Count);
            Dumps.AssertBytesEqual(Dumps.Render(B.Seed, Names(a), first), Dumps.Render(B.Seed, Names(b), second), "the second session");
            Assert.Equal(a.Host.WorldStateHash(), b.Host.WorldStateHash());
        }
    }
}
