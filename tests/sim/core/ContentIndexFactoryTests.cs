using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Core.Tests.ContentFixtures;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// ContentIndexFactory, 08 §8.11/§8.11a as amended by Q-028 (T-026 owns
    /// the factory). Create copies and sorts by ordinal id. A null list throws
    /// ArgumentNullException. A null element, a null id value or a duplicate
    /// id throws ArgumentException. AllOf returns one kind's ids in ordinal
    /// order. TryGet is a pure type-and-id match.
    /// </summary>
    public sealed class ContentIndexFactoryTests
    {
        [Fact]
        public void test_content_index_factory_null_list_throws_argument_null()
        {
            Assert.Throws<ArgumentNullException>(() => ContentIndexFactory.Create(null!));
        }

        [Fact]
        public void test_content_index_factory_empty_list_gives_empty_index()
        {
            IContentIndex index = ContentIndexFactory.Create(new List<IContentDefinition>());
            foreach (ContentKind k in AllKinds)
            {
                Assert.Empty(index.AllOf(k));
            }

            Assert.False(index.TryGet(Id("anything"), out SizeCategoryDefinition _));
        }

        [Fact]
        public void test_content_index_factory_sorts_by_ordinal_id()
        {
            // Ordinal order differs from culture order here: 'B' (66) < '_' (95) < 'a' (97).
            string[] input = { "size.b", "size.B", "size.a", "size._", "size.aa", "size.a-b", "size.é", "size.Z" };
            var defs = input.Select((s, i) => (IContentDefinition)Size(s, i)).ToList();

            IContentIndex index = ContentIndexFactory.Create(defs);

            Assert.Equal(
                new[] { "size.B", "size.Z", "size._", "size.a", "size.a-b", "size.aa", "size.b", "size.é" },
                Ids(index.AllOf(ContentKind.SizeCategory)));
        }

        [Fact]
        public void test_content_index_factory_sort_matches_ordinal_oracle_property()
        {
            const ulong seed = 0x7026_C0DEUL;
            var rng = new SplitMix64(seed);
            const string alphabet = "aAbB_.-09zZé";
            for (int iter = 0; iter < 50; iter++)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                int count = 1 + (int)(rng.Next() % 40UL);
                while (names.Count < count)
                {
                    int len = 1 + (int)(rng.Next() % 6UL);
                    var chars = new char[len];
                    for (int c = 0; c < len; c++)
                    {
                        chars[c] = alphabet[(int)(rng.Next() % (ulong)alphabet.Length)];
                    }

                    names.Add(new string(chars));
                }

                var defs = new List<IContentDefinition>();
                var byKind = AllKinds.ToDictionary(k => k, k => new List<string>());
                foreach (string name in names)
                {
                    ContentKind kind = AllKinds[(int)(rng.Next() % 4UL)];
                    byKind[kind].Add(name);
                    defs.Add(kind switch
                    {
                        ContentKind.SizeCategory => Size(name, 1),
                        ContentKind.Aircraft => Aircraft(name, "size.x"),
                        ContentKind.PaxProfile => Pax(name),
                        _ => Queue(name),
                    });
                }

                IContentIndex index = ContentIndexFactory.Create(defs);
                foreach (ContentKind k in AllKinds)
                {
                    List<string> expected = OrdinalSorted(byKind[k]);
                    List<string> actual = Ids(index.AllOf(k));
                    Assert.True(expected.SequenceEqual(actual), $"seed {seed:X}, iteration {iter}, kind {k}: [{string.Join(",", actual)}] != [{string.Join(",", expected)}]");
                }
            }
        }

        [Fact]
        public void test_content_index_factory_result_is_independent_of_input_order()
        {
            var defs = new List<IContentDefinition>
            {
                Queue("q.2"), Size("s.1", 1), Aircraft("a.2", "s.1"), Pax("p.1"), Aircraft("a.1", "s.1"), Queue("q.1"), Size("s.0", 0),
            };
            var reversed = Enumerable.Reverse(defs).ToList();

            IContentIndex a = ContentIndexFactory.Create(defs);
            IContentIndex b = ContentIndexFactory.Create(reversed);

            foreach (ContentKind k in AllKinds)
            {
                Assert.Equal(Ids(a.AllOf(k)), Ids(b.AllOf(k)));
            }

            Assert.Equal(new[] { "a.1", "a.2" }, Ids(a.AllOf(ContentKind.Aircraft)));
            Assert.Equal(new[] { "q.1", "q.2" }, Ids(a.AllOf(ContentKind.QueueProfile)));
            Assert.Equal(new[] { "p.1" }, Ids(a.AllOf(ContentKind.PaxProfile)));
            Assert.Equal(new[] { "s.0", "s.1" }, Ids(a.AllOf(ContentKind.SizeCategory)));
        }

        [Fact]
        public void test_content_index_factory_ids_differing_only_by_case_are_distinct()
        {
            var defs = new List<IContentDefinition> { Size("gate", 1), Size("Gate", 2), Size("GATE", 3) };
            IContentIndex index = ContentIndexFactory.Create(defs);
            Assert.Equal(new[] { "GATE", "Gate", "gate" }, Ids(index.AllOf(ContentKind.SizeCategory)));
            Assert.True(index.TryGet(Id("Gate"), out SizeCategoryDefinition g));
            Assert.Equal(2, g.Ordinal);
        }

        [Fact]
        public void test_content_index_factory_try_get_returns_each_kind_definition()
        {
            var defs = new List<IContentDefinition>
            {
                Size("size.c", 3), Aircraft("aircraft.a320", "size.c"), Pax("pax.business"), Queue("queue.immigration"),
            };
            IContentIndex index = ContentIndexFactory.Create(defs);

            Assert.True(index.TryGet(Id("size.c"), out SizeCategoryDefinition s));
            Assert.Equal(3, s.Ordinal);
            Assert.Equal(Id("size.c"), s.Id);

            Assert.True(index.TryGet(Id("aircraft.a320"), out AircraftDefinition a));
            Assert.Equal(Id("size.c"), a.SizeCategory);

            Assert.True(index.TryGet(Id("pax.business"), out PaxProfileDefinition p));
            Assert.Equal(5L << 31, p.WalkSpeedMps.Raw);
            Assert.Equal(2, p.ShowUpCurve.Count);

            Assert.True(index.TryGet(Id("queue.immigration"), out QueueProfileDefinition q));
            Assert.Equal(250, q.CapacityStanding);

            Assert.False(index.TryGet(Id("size.d"), out SizeCategoryDefinition _));
            Assert.False(index.TryGet(Id("SIZE.C"), out SizeCategoryDefinition _));
        }

        [Fact]
        public void test_content_index_factory_later_changes_to_input_list_do_not_leak_in()
        {
            var defs = new List<IContentDefinition> { Size("s.b", 1), Size("s.a", 0) };
            IContentIndex index = ContentIndexFactory.Create(defs);

            defs.Add(Size("s.c", 2));
            defs[0] = Size("s.z", 9);
            defs.RemoveAt(1);

            Assert.Equal(new[] { "s.a", "s.b" }, Ids(index.AllOf(ContentKind.SizeCategory)));
            Assert.True(index.TryGet(Id("s.b"), out SizeCategoryDefinition b));
            Assert.Equal(1, b.Ordinal);
            Assert.False(index.TryGet(Id("s.c"), out SizeCategoryDefinition _));
        }

        [Fact]
        public void test_content_index_factory_all_of_result_cannot_mutate_the_index()
        {
            var defs = new List<IContentDefinition> { Size("s.a", 0), Size("s.b", 1) };
            IContentIndex index = ContentIndexFactory.Create(defs);

            IReadOnlyList<ContentId> view = index.AllOf(ContentKind.SizeCategory);
            if (view is IList<ContentId> writable)
            {
                try
                {
                    writable[0] = Id("s.hacked");
                }
                catch (NotSupportedException)
                {
                }

                try
                {
                    writable.Add(Id("s.added"));
                }
                catch (NotSupportedException)
                {
                }
            }

            Assert.Equal(new[] { "s.a", "s.b" }, Ids(index.AllOf(ContentKind.SizeCategory)));
        }

        [Fact]
        public void test_content_index_factory_null_element_throws_argument_exception()
        {
            var defs = new List<IContentDefinition> { Size("s.a", 0), null!, Size("s.b", 1) };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(defs));
        }

        [Fact]
        public void test_content_index_factory_null_id_value_throws_argument_exception()
        {
            var viaDefault = new List<IContentDefinition> { Size("s.a", 0), new SizeCategoryDefinition(default, 1) };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(viaDefault));

            var viaNullString = new List<IContentDefinition> { new AircraftDefinition(new ContentId(null!), Id("s.a")), Size("s.a", 0) };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(viaNullString));
        }

        [Fact]
        public void test_content_index_factory_try_get_type_mismatch_returns_false_and_default()
        {
            var defs = new List<IContentDefinition> { Size("size.c", 3), Queue("queue.security") };
            IContentIndex index = ContentIndexFactory.Create(defs);

            Assert.False(index.TryGet(Id("size.c"), out AircraftDefinition a));
            Assert.Null(a.Id.Value);
            Assert.Null(a.SizeCategory.Value);

            Assert.False(index.TryGet(Id("queue.security"), out PaxProfileDefinition p));
            Assert.Null(p.Id.Value);
            Assert.Null(p.ShowUpCurve);
            Assert.Equal(0L, p.WalkSpeedMps.Raw);

            Assert.False(index.TryGet(Id("queue.security"), out SizeCategoryDefinition s));
            Assert.Equal(0, s.Ordinal);

            // A mismatch does not disturb the right-typed lookup.
            Assert.True(index.TryGet(Id("size.c"), out SizeCategoryDefinition ok));
            Assert.Equal(3, ok.Ordinal);
        }

        [Fact]
        public void test_content_index_factory_try_get_interface_type_matches_any_definition()
        {
            var defs = new List<IContentDefinition>
            {
                Size("size.c", 3), Aircraft("aircraft.a320", "size.c"), Pax("pax.business"), Queue("queue.immigration"),
            };
            IContentIndex index = ContentIndexFactory.Create(defs);

            Assert.True(index.TryGet(Id("size.c"), out IContentDefinition d1));
            Assert.Equal(3, Assert.IsType<SizeCategoryDefinition>(d1).Ordinal);
            Assert.Equal(ContentKind.SizeCategory, d1.Kind);

            Assert.True(index.TryGet(Id("aircraft.a320"), out IContentDefinition d2));
            Assert.Equal(Id("size.c"), Assert.IsType<AircraftDefinition>(d2).SizeCategory);

            Assert.True(index.TryGet(Id("pax.business"), out IContentDefinition d3));
            Assert.Equal(ContentKind.PaxProfile, d3.Kind);
            Assert.IsType<PaxProfileDefinition>(d3);

            Assert.True(index.TryGet(Id("queue.immigration"), out IContentDefinition d4));
            Assert.Equal(Id("queue.immigration"), d4.Id);
            Assert.IsType<QueueProfileDefinition>(d4);

            Assert.False(index.TryGet(Id("missing"), out IContentDefinition none));
            Assert.Null(none);
        }

        [Fact]
        public void test_content_index_factory_try_get_null_id_value_throws_argument_exception()
        {
            IContentIndex index = ContentIndexFactory.Create(new List<IContentDefinition> { Size("s.a", 0) });
            Assert.Throws<ArgumentException>(() => index.TryGet(default(ContentId), out SizeCategoryDefinition _));
            Assert.Throws<ArgumentException>(() => index.TryGet(new ContentId(null!), out IContentDefinition _));
        }
    }
}
