using System;
using System.Text;
using Xunit;
using static AirportSim.Sim.Core.Tests.RngTestSupport;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// T-002. IRandomService and RandomServiceFactory against 08-interfaces-core.md
    /// §8.8 (Q-019, Q-023), and the service as a host wires it into TickContext.Rng
    /// (§8.11a: Build creates the RNG service from MasterSeed).
    /// </summary>
    public sealed class RandomServiceTests
    {
        // Row 1 of the §8.8 golden table: MasterSeed 0, "sim.flow.showup".
        private const ulong Row1Out0 = 0x4D8ADDC1EA523EA8UL;
        private const ulong Row1Out1 = 0x881053D9C83E81ECUL;
        private const ulong Row1Out2 = 0x9F943EEE723DAD43UL;
        private const ulong Row1Out3 = 0x41FB063846C7BC01UL;

        private const uint TicksPerDay = (uint)SimConstants.TICKS_PER_SIM_DAY;

        // ------------------------------------------------------------ service

        [Theory]
        [InlineData(0UL)]
        [InlineData(1UL)]
        [InlineData(12345UL)]
        [InlineData(ulong.MaxValue)]
        public void test_random_service_master_seed_returns_create_argument(ulong masterSeed)
        {
            IRandomService service = RandomServiceFactory.Create(masterSeed);
            Assert.Equal(masterSeed, service.MasterSeed);
            service.Stream(new RngStreamName("sim.flow.showup")).NextUInt64();
            Assert.Equal(masterSeed, service.MasterSeed);
        }

        [Fact]
        public void test_random_service_stream_same_name_returns_same_live_stream()
        {
            IRandomService service = RandomServiceFactory.Create(0UL);
            IRandomStream first = service.Stream(new RngStreamName("sim.flow.showup"));
            Assert.Equal(Row1Out0, first.NextUInt64());

            // A separately built name with an equal value (ordinal equality) is the same stream,
            // and fetching it again neither resets nor forks it.
            string sameValue = new string("sim.flow.showup".ToCharArray());
            IRandomStream second = service.Stream(new RngStreamName(sameValue));
            Assert.Same(first, second);
            Assert.Equal(Row1Out1, second.NextUInt64());
            Assert.Equal(Row1Out2, first.NextUInt64());
            Assert.Equal(Row1Out3, service.Stream(new RngStreamName("sim.flow.showup")).NextUInt64());
        }

        [Fact]
        [Trait("Category", "Budget")]
        public void test_random_service_stream_repeat_call_allocates_zero_bytes()
        {
            // §8.8: later Stream calls for a name neither allocate nor reset it (Q-023 "Cost").
            IRandomService service = RandomServiceFactory.Create(7UL);
            var a = new RngStreamName("sim.flow.showup");
            var b = new RngStreamName("sim.airside.taxi");
            ulong sink = service.Stream(a).NextUInt64() ^ service.Stream(b).NextUInt64();
            for (int i = 0; i < 100; i++)
            {
                sink ^= service.Stream(a).NextUInt64();
                sink ^= service.Stream(b).ComputeStateHash();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++)
            {
                sink ^= service.Stream(a).NextUInt64();
                sink ^= (ulong)service.Stream(b).NextInt(0, 3);
                sink ^= service.MasterSeed;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
            Assert.NotEqual(0UL, sink);
        }

        [Fact]
        public void test_random_service_stream_default_name_throws_argument_exception()
        {
            // Q-023: Stream(default(RngStreamName)) throws ArgumentException.
            IRandomService service = RandomServiceFactory.Create(0UL);
            Assert.Throws<ArgumentException>(() => service.Stream(default(RngStreamName)));
            // The rejected call leaves the service usable and the named stream untouched.
            Assert.Equal(Row1Out0, service.Stream(new RngStreamName("sim.flow.showup")).NextUInt64());
        }

        [Fact]
        public void test_random_service_streams_are_independent_under_any_interleaving()
        {
            // Rule 3 of 02-determinism: drawing from one stream never shifts another.
            // Streams are fetched lazily in a random order and drawn in a random
            // interleaving; each must still follow its own fresh reference exactly.
            const ulong seed = 0x5EED0002B0000001UL;
            var gen = new RngGen(seed);
            for (int i = 0; i < 100; i++)
            {
                ulong master = gen.Next();
                IRandomService service = RandomServiceFactory.Create(master);
                var references = new ReferenceStream?[Names.Length];
                var names = new RngStreamName[Names.Length];
                for (int k = 0; k < Names.Length; k++)
                {
                    names[k] = new RngStreamName(Names[k]);
                }
                for (int op = 0; op < 200; op++)
                {
                    int k = gen.Below(Names.Length);
                    references[k] ??= new ReferenceStream(master, Names[k]);
                    ulong expected = references[k]!.NextUInt64();
                    ulong got = service.Stream(names[k]).NextUInt64();
                    if (expected != got)
                    {
                        Assert.Fail(At(seed, i) + ", op " + op + ": " + Names[k] + " drew " + Hex(got) + ", expected " + Hex(expected));
                    }
                }
            }
        }

        [Fact]
        public void test_random_service_stream_created_after_other_draws_starts_fresh()
        {
            IRandomService service = RandomServiceFactory.Create(0UL);
            IRandomStream other = service.Stream(new RngStreamName("sim.schedule.jitter"));
            for (int i = 0; i < 1000; i++)
            {
                other.NextInt(0, 17);
            }
            IRandomStream late = service.Stream(new RngStreamName("sim.flow.showup"));
            Assert.Equal(new[] { Row1Out0, Row1Out1, Row1Out2, Row1Out3 },
                new[] { late.NextUInt64(), late.NextUInt64(), late.NextUInt64(), late.NextUInt64() });
        }

        [Fact]
        public void test_random_service_same_seed_two_services_produce_identical_sequences()
        {
            const ulong seed = 0x5EED0002B0000002UL;
            var gen = new RngGen(seed);
            for (int i = 0; i < 50; i++)
            {
                ulong master = gen.Next();
                string name = Names[gen.Below(Names.Length)];
                IRandomStream a = RandomServiceFactory.Create(master).Stream(new RngStreamName(name));
                IRandomStream b = RandomServiceFactory.Create(master).Stream(new RngStreamName(name));
                for (int d = 0; d < 100; d++)
                {
                    if (a.NextUInt64() != b.NextUInt64()) Assert.Fail(At(seed, i) + ", draw " + d + ": same seed diverged");
                }
                Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
            }
        }

        [Fact]
        public void test_random_service_different_master_seeds_produce_different_streams()
        {
            var name = new RngStreamName("sim.flow.showup");
            IRandomStream a = RandomServiceFactory.Create(0UL).Stream(name);
            IRandomStream b = RandomServiceFactory.Create(1UL).Stream(name);
            Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
            Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
        }

        [Fact]
        public void test_random_service_stream_seed_is_master_xor_name_hash()
        {
            // §8.8: seed = MasterSeed XOR FNV1a64(utf8(name)). Two (seed, name) pairs
            // with the same XOR therefore give bit-identical streams.
            const string nameA = "sim.flow.showup";
            const string nameB = "sim.airside.taxi";
            ulong fnvA = Fnv1a64(Encoding.UTF8.GetBytes(nameA));
            ulong fnvB = Fnv1a64(Encoding.UTF8.GetBytes(nameB));
            Assert.Equal(0x80AA6E48500EF830UL, fnvA);
            Assert.Equal(0xC6381A17FBE07F29UL, fnvB);

            const ulong seed = 0x5EED0002B0000003UL;
            var gen = new RngGen(seed);
            for (int i = 0; i < 50; i++)
            {
                ulong m1 = gen.Next();
                ulong m2 = m1 ^ fnvA ^ fnvB;
                IRandomStream a = RandomServiceFactory.Create(m1).Stream(new RngStreamName(nameA));
                IRandomStream b = RandomServiceFactory.Create(m2).Stream(new RngStreamName(nameB));
                for (int d = 0; d < 8; d++)
                {
                    if (a.NextUInt64() != b.NextUInt64()) Assert.Fail(At(seed, i) + ", draw " + d + ": equal seeds gave different streams");
                }
            }
        }

        // ------------------------------------------------------------ inside a host

        [Theory]
        [InlineData(0UL)]
        [InlineData(12345UL)]
        [InlineData(ulong.MaxValue)]
        public void test_random_service_host_context_rng_uses_config_master_seed(ulong masterSeed)
        {
            var probe = new RngProbe(6, "sim.flow.showup", 1);
            ISimHost host = BuildHost(masterSeed, new CheckpointRecorder(), probe);
            host.Step(1);
            Assert.True(probe.Ticked);
            Assert.Equal(masterSeed, probe.ObservedMasterSeed);
            Assert.Equal(new ReferenceStream(masterSeed, "sim.flow.showup").NextUInt64(), probe.FirstTickFirstDraw);
        }

        [Fact]
        public void test_random_service_host_stream_matches_golden_vector()
        {
            // Build creates the service with RandomServiceFactory (§8.8, §8.11a), so the
            // first draw a system makes is the golden vector's first word.
            var probe = new RngProbe(6, "sim.flow.showup", 1);
            ISimHost host = BuildHost(0UL, new CheckpointRecorder(), probe);
            host.Step(1);
            Assert.Equal(Row1Out0, probe.FirstTickFirstDraw);
        }

        [Fact]
        public void test_random_service_host_stream_persists_across_ticks_and_steps()
        {
            const int draws = 3;
            var probe = new RngProbe(6, "sim.schedule.jitter", draws);
            ISimHost host = BuildHost(12345UL, new CheckpointRecorder(), probe);
            var reference = new ReferenceStream(12345UL, "sim.schedule.jitter");

            uint[] chunks = { 1, 1, 5, 10, 37 };
            ulong ticks = 0;
            foreach (uint chunk in chunks)
            {
                host.Step(chunk);
                for (uint t = 0; t < chunk; t++)
                {
                    AdvanceLikeProbe(reference, draws, ticks == 0);
                    ticks++;
                }
                Assert.Equal(ticks, host.CurrentTick);
                Assert.Equal(reference.ComputeStateHash(), probe.ComputeStateHash());
            }
        }

        [Fact]
        public void test_random_service_host_headless_day_same_seed_identical_checkpoints()
        {
            var sinkA = new CheckpointRecorder();
            var sinkB = new CheckpointRecorder();
            ISimHost a = BuildHost(20260926UL, sinkA,
                new RngProbe(6, "sim.baggage.belt_0", 2), new RngProbe(13, "sim.incident.roll", 5));
            ISimHost b = BuildHost(20260926UL, sinkB,
                new RngProbe(6, "sim.baggage.belt_0", 2), new RngProbe(13, "sim.incident.roll", 5));

            a.Step(TicksPerDay);
            b.Step(TicksPerDay);

            Assert.Equal(24, sinkA.Recorded.Count);
            AssertCheckpointsEqual(sinkA.Recorded, sinkB.Recorded);
            Assert.Equal(a.WorldStateHash(), b.WorldStateHash());
        }

        [Fact]
        public void test_random_service_host_headless_day_system_hashes_follow_reference_streams()
        {
            // Each probe's hash is its stream's hash (§8.8 "Stream hash"), so every
            // checkpoint's SystemHashes entry must equal the reference stream after
            // t + 1 ticks of that probe's draws.
            const ulong master = 99UL;
            var sink = new CheckpointRecorder();
            ISimHost host = BuildHost(master, sink,
                new RngProbe(6, "sim.baggage.belt_0", 2), new RngProbe(13, "sim.incident.roll", 5));
            host.Step(TicksPerDay);

            var refA = new ReferenceStream(master, "sim.baggage.belt_0");
            var refB = new ReferenceStream(master, "sim.incident.roll");
            ulong executed = 0;
            Assert.Equal(24, sink.Recorded.Count);
            foreach (Checkpoint cp in sink.Recorded)
            {
                while (executed <= cp.Tick)
                {
                    AdvanceLikeProbe(refA, 2, executed == 0);
                    AdvanceLikeProbe(refB, 5, executed == 0);
                    executed++;
                }
                Assert.Equal(2, cp.SystemHashes.Length);
                Assert.Equal(refA.ComputeStateHash(), cp.SystemHashes[0]);
                Assert.Equal(refB.ComputeStateHash(), cp.SystemHashes[1]);
            }
        }

        [Fact]
        public void test_random_service_host_different_seed_changes_every_checkpoint()
        {
            var sinkA = new CheckpointRecorder();
            var sinkB = new CheckpointRecorder();
            BuildHost(1UL, sinkA, new RngProbe(6, "sim.baggage.belt_0", 2)).Step(TicksPerDay);
            BuildHost(2UL, sinkB, new RngProbe(6, "sim.baggage.belt_0", 2)).Step(TicksPerDay);
            Assert.Equal(sinkA.Recorded.Count, sinkB.Recorded.Count);
            for (int i = 0; i < sinkA.Recorded.Count; i++)
            {
                Assert.NotEqual(sinkA.Recorded[i].SystemHashes[0], sinkB.Recorded[i].SystemHashes[0]);
                Assert.NotEqual(sinkA.Recorded[i].WorldHash, sinkB.Recorded[i].WorldHash);
            }
        }

        [Fact]
        public void test_random_service_host_chunked_steps_match_single_day_step()
        {
            // Q-014 A1/A8: chunking is invisible.
            var whole = new CheckpointRecorder();
            ISimHost one = BuildHost(4242UL, whole,
                new RngProbe(5, "sim.turnaround.job_order", 1), new RngProbe(9, "sim.staff.shift", 4));
            one.Step(TicksPerDay);

            const ulong seed = 0x5EED0002B0000004UL;
            var gen = new RngGen(seed);
            var chunked = new CheckpointRecorder();
            ISimHost two = BuildHost(4242UL, chunked,
                new RngProbe(5, "sim.turnaround.job_order", 1), new RngProbe(9, "sim.staff.shift", 4));
            ulong remaining = TicksPerDay;
            while (remaining > 0)
            {
                uint chunk = (uint)(1 + gen.Below(1200));
                if (chunk > remaining) chunk = (uint)remaining;
                two.Step(chunk);
                remaining -= chunk;
            }

            Assert.Equal(one.CurrentTick, two.CurrentTick);
            AssertCheckpointsEqual(whole.Recorded, chunked.Recorded);
            Assert.Equal(one.WorldStateHash(), two.WorldStateHash());
        }

        [Fact]
        public void test_random_service_host_system_stream_unaffected_by_another_system()
        {
            // A second system drawing heavily from its own stream, and ticking first,
            // must not shift the first system's stream (02-determinism rule 3).
            var aloneSink = new CheckpointRecorder();
            BuildHost(31UL, aloneSink, new RngProbe(6, "sim.baggage.belt_0", 2)).Step(TicksPerDay);

            var sharedSink = new CheckpointRecorder();
            BuildHost(31UL, sharedSink,
                new RngProbe(5, "sim.turnaround.job_order", 40),
                new RngProbe(6, "sim.baggage.belt_0", 2)).Step(TicksPerDay);

            Assert.Equal(aloneSink.Recorded.Count, sharedSink.Recorded.Count);
            for (int i = 0; i < aloneSink.Recorded.Count; i++)
            {
                Assert.Equal(aloneSink.Recorded[i].SystemHashes[0], sharedSink.Recorded[i].SystemHashes[1]);
            }
        }

        /// <summary>Advances a reference stream by one tick of <see cref="RngProbe"/>'s draws.</summary>
        private static void AdvanceLikeProbe(ReferenceStream reference, int drawsPerTick, bool firstTick)
        {
            int i = 0;
            if (firstTick)
            {
                reference.NextUInt64();
                i = 1;
            }
            for (; i < drawsPerTick; i++)
            {
                reference.NextInt(0, 1000);
            }
        }
    }
}
