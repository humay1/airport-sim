using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// ContentIndexFactory and the content definition types, 08 §8.11 and
    /// §8.11a (Q-011; T-026 owns the factory). Create sorts by ordinal id and
    /// throws on a duplicate id across all kinds. AllOf returns the ids of one
    /// kind in ordinal order. ContentKind is fixed by the definition type.
    /// Content is immutable for the session. ContentId uses ordinal equality
    /// and order.
    /// </summary>
    public sealed class ContentIndexFactoryTests
    {
        private static readonly ContentKind[] AllKinds =
        {
            ContentKind.SizeCategory, ContentKind.Aircraft, ContentKind.PaxProfile, ContentKind.QueueProfile,
        };

        private static ContentId Id(string s) => new ContentId(s);

        private static SizeCategoryDefinition Size(string id, int ordinal) => new SizeCategoryDefinition(Id(id), ordinal);

        private static AircraftDefinition Aircraft(string id, string size) => new AircraftDefinition(Id(id), Id(size));

        private static PaxProfileDefinition Pax(string id) =>
            new PaxProfileDefinition(Id(id), Fx.FromRaw(5L << 31), new[] { new ShowUpBucket(120U, 300U), new ShowUpBucket(60U, 700U) });

        private static QueueProfileDefinition Queue(string id) =>
            new QueueProfileDefinition(Id(id), Fx.FromRaw(3L << 32), 250, Fx.FromRaw(10L << 32), Fx.FromRaw(2L << 32), DelayCategory.security_queue);

        private static List<string> Ids(IReadOnlyList<ContentId> ids) => ids.Select(i => i.Value).ToList();

        private static List<string> OrdinalSorted(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            list.Sort(string.CompareOrdinal);
            return list;
        }

        // ------------------------------------------------------------- ContentId

        [Fact]
        public void test_content_ids_compare_ordinally_not_by_default_hash()
        {
            Assert.True(Id(new string('a', 3)) == Id("aaa"), "equal text in distinct string instances must be equal");
            Assert.True(Id("abc") != Id("ABC"), "ordinal equality is case-sensitive");
            Assert.True(Id("é") != Id("é"), "ordinal equality does not normalise");
            Assert.True(Id("i") != Id("I"));
            Assert.True(Id("aaa").Equals(Id(new string('a', 3))));
            Assert.Equal(Id("aaa").GetHashCode(), Id(new string('a', 3)).GetHashCode());
            Assert.True(typeof(IEquatable<ContentId>).IsAssignableFrom(typeof(ContentId)));
            Assert.Equal("aircraft.a320", Id("aircraft.a320").Value);
        }

        // ----------------------------------------------------------- definitions

        [Fact]
        public void test_content_definitions_match_spec_shapes_and_kinds()
        {
            var v = new List<string>();
            foreach ((PayloadShapes.Shape shape, ContentKind kind) in PayloadShapes.Definitions)
            {
                Type t = shape.Type;
                if (!t.IsPublic || !PayloadShapes.IsReadOnlyStruct(t))
                {
                    v.Add($"{t.Name}: not a public readonly struct (07 L10)");
                }

                if (!typeof(IContentDefinition).IsAssignableFrom(t))
                {
                    v.Add($"{t.Name}: does not implement IContentDefinition");
                    continue;
                }

                var ctors = t.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (ctors.Length != 1 || !ctors[0].GetParameters().Select(p => p.ParameterType).SequenceEqual(shape.Members.Select(m => m.Type)))
                {
                    v.Add($"{t.Name}: expected one public constructor ({string.Join(", ", shape.Members.Select(m => m.Type.Name))})");
                    continue;
                }

                var args = new object?[shape.Members.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    args[i] = PayloadShapes.MakeValue(shape.Members[i].Type, i + 1);
                }

                var def = (IContentDefinition)ctors[0].Invoke(args);
                if (def.Kind != kind)
                {
                    v.Add($"{t.Name}: Kind {def.Kind}, expected {kind}");
                }

                if (!Equals(args[0], def.Id))
                {
                    v.Add($"{t.Name}: Id does not return the first constructor argument");
                }

                for (int i = 0; i < args.Length; i++)
                {
                    var p = t.GetProperty(shape.Members[i].Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (p == null || p.PropertyType != shape.Members[i].Type || p.SetMethod != null)
                    {
                        v.Add($"{t.Name}.{shape.Members[i].Name}: expected get-only {shape.Members[i].Type.Name}");
                    }
                    else if (!PayloadShapes.SameValue(args[i], p.GetValue(def)))
                    {
                        v.Add($"{t.Name}.{shape.Members[i].Name}: does not return constructor argument {i}");
                    }
                }
            }

            Assert.True(v.Count == 0, "08 §8.11:\n" + string.Join("\n", v));
        }

        [Fact]
        public void test_content_definitions_bind_by_name()
        {
            QueueProfileDefinition q = Queue("queue.security");
            Assert.Equal(Id("queue.security"), q.Id);
            Assert.Equal(3L << 32, q.ServiceRatePerServerPerMinute.Raw);
            Assert.Equal(250, q.CapacityStanding);
            Assert.Equal(10L << 32, q.ThresholdWaitMinutes.Raw);
            Assert.Equal(2L << 32, q.HysteresisMinutes.Raw);
            Assert.Equal(DelayCategory.security_queue, q.Category);

            PaxProfileDefinition p = Pax("pax.leisure");
            Assert.Equal(5L << 31, p.WalkSpeedMps.Raw);
            Assert.Equal(new[] { 120U, 60U }, p.ShowUpCurve.Select(b => b.MinutesBeforeStd));
            Assert.Equal(new[] { 300U, 700U }, p.ShowUpCurve.Select(b => b.SharePermille));

            Assert.Equal(Id("size.c"), Aircraft("aircraft.a320", "size.c").SizeCategory);
            Assert.Equal(3, Size("size.c", 3).Ordinal);
        }

        // ------------------------------------------------------------ the factory

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
        public void test_content_definition_ids_unique_across_all_kinds()
        {
            // 08 §8.11: ids are unique across all kinds; §8.11a: Create throws on a
            // duplicate. 07 "Error handling": a bad argument is ArgumentException.
            var crossKind = new List<IContentDefinition> { Size("shared", 1), Aircraft("shared", "shared") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(crossKind));

            var sameKind = new List<IContentDefinition> { Queue("q"), Pax("p"), Queue("q") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(sameKind));

            var farApart = new List<IContentDefinition> { Pax("dup"), Size("z", 0), Size("a", 0), Queue("dup") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(farApart));
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
    }
}
