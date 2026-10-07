using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;
using B = AirportSim.App.Host.Tests.Bundles;

namespace AirportSim.App.Host.Tests
{
    /// <summary>
    /// T-054. ComposedSim.Content (16 §16.4, Q-130): Compose returns the content
    /// index that step 1 built from the composer's definitions, and the
    /// seven-argument constructor kept under 07 L10 sets it to null.
    /// </summary>
    public sealed class ComposeContentTests
    {
        private static readonly ContentKind[] Kinds = (ContentKind[])Enum.GetValues(typeof(ContentKind));

        /// <summary>Resolves a definition through the TryGet of its own concrete type (08 §8.11).</summary>
        private static bool Resolve(IContentIndex index, IContentDefinition d, out IContentDefinition found)
        {
            bool ok;
            switch (d.Kind)
            {
                case ContentKind.SizeCategory:
                    ok = index.TryGet(d.Id, out SizeCategoryDefinition size);
                    found = size;
                    return ok;
                case ContentKind.Aircraft:
                    ok = index.TryGet(d.Id, out AircraftDefinition aircraft);
                    found = aircraft;
                    return ok;
                case ContentKind.PaxProfile:
                    ok = index.TryGet(d.Id, out PaxProfileDefinition pax);
                    found = pax;
                    return ok;
                case ContentKind.QueueProfile:
                    ok = index.TryGet(d.Id, out QueueProfileDefinition queue);
                    found = queue;
                    return ok;
                default:
                    throw new InvalidOperationException("a content kind 08 §8.11 does not name: " + d.Kind);
            }
        }

        private static void AssertIndexIsExactly(IContentIndex? index, IReadOnlyList<IContentDefinition> content, string what)
        {
            Assert.True(index != null, what + ": ComposedSim.Content is null after ISimComposer.Compose (16 §16.4)");

            // Every definition the composer was created with resolves, as itself.
            foreach (IContentDefinition d in content)
            {
                Assert.True(Resolve(index!, d, out IContentDefinition found), what + ": " + d.Kind + " " + d.Id.Value + " does not resolve");
                Assert.Equal(d.Id.Value, found.Id.Value);
                Assert.Equal(d.Kind, found.Kind);
                if (d is SizeCategoryDefinition size)
                {
                    Assert.Equal(size.Ordinal, ((SizeCategoryDefinition)found).Ordinal);
                }

                if (d is AircraftDefinition aircraft)
                {
                    Assert.Equal(aircraft.SizeCategory.Value, ((AircraftDefinition)found).SizeCategory.Value);
                }
            }

            // And nothing else: each kind lists exactly the composer's ids, sorted (08 §8.11).
            foreach (ContentKind kind in Kinds)
            {
                List<string> expected = content.Where(d => d.Kind == kind).Select(d => d.Id.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();
                List<string> actual = index!.AllOf(kind).Select(id => id.Value).ToList();
                Assert.True(expected.SequenceEqual(actual), what + ": AllOf(" + kind + ") is [" + string.Join(", ", actual) + "], expected [" + string.Join(", ", expected) + "]");
            }
        }

        [Fact]
        public void test_compose_exposes_the_content_index()
        {
            // Phase 1: the repository's data/ content, every size category a to f.
            IReadOnlyList<IContentDefinition> phase1 = B.Content(B.Phase1Content);
            ComposedSim one = ComposeTests.Compose(B.Phase1Bundle(), B.Phase1Content, new RecordingSink());
            AssertIndexIsExactly(one.Content, phase1, "Phase 1 bundle, data/ content");

            // Phase 0: the harness content, which holds size_c only. An index built
            // from anything but the composer's own definitions fails here.
            IReadOnlyList<IContentDefinition> phase0 = B.Content(B.Phase0Content);
            ComposedSim zero = ComposeTests.Compose(B.Phase0Bundle(), B.Phase0Content, new RecordingSink());
            AssertIndexIsExactly(zero.Content, phase0, "Phase 0 bundle, phase0-content");
            Assert.False(zero.Content!.TryGet(new ContentId("size_a"), out SizeCategoryDefinition _), "the Phase 0 index resolves size_a, which its content does not hold");
            Assert.True(one.Content!.TryGet(new ContentId("size_a"), out SizeCategoryDefinition _), "control: the Phase 1 index resolves size_a");

            // One composer, many calls (Q-123): a second call exposes the same definitions.
            ISimComposer composer = HostFactory.CreateSimComposer(phase1);
            ComposedSim first = composer.Compose(B.Phase1Bundle(), new RecordingSink());
            ComposedSim second = composer.Compose(B.Phase1Bundle(), new RecordingSink());
            AssertIndexIsExactly(first.Content, phase1, "first call of one composer");
            AssertIndexIsExactly(second.Content, phase1, "second call of one composer");
        }

        [Fact]
        public void test_composed_sim_constructors_set_content_or_null()
        {
            // 16 §16.4, 07 L10: the eight-argument constructor takes Content last;
            // the kept seven-argument one sets it to null.
            var trace = new Trace();
            var host = new TraceHost(trace);
            var flow = new TraceFlow(trace);
            IContentIndex index = Kit.Index(B.Phase0Content);

            var eight = new ComposedSim(host, null, null, null, flow, null, null, index);
            Assert.Same(host, eight.Host);
            Assert.Same(flow, eight.Flow);
            Assert.Null(eight.World);
            Assert.Null(eight.Schedule);
            Assert.Null(eight.Airside);
            Assert.Null(eight.Turnaround);
            Assert.Null(eight.Delay);
            Assert.Same(index, eight.Content);

            var seven = new ComposedSim(host, null, null, null, flow, null, null);
            Assert.Same(host, seven.Host);
            Assert.Same(flow, seven.Flow);
            Assert.Null(seven.Content);
        }
    }
}
