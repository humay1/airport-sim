using System;
using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;
using Xunit;
using static AirportSim.Sim.Core.Tests.ContentFixtures;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// ContentId and the content definition types, 08 §8.11 (Q-011, Q-028):
    /// ContentId uses ordinal equality, ContentKind is fixed by the
    /// definition type, and ids are unique across all kinds.
    /// </summary>
    public sealed class ContentTests
    {
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
            Assert.Equal(DelayCategory.SecurityQueue, q.Category);

            PaxProfileDefinition p = Pax("pax.leisure");
            Assert.Equal(5L << 31, p.WalkSpeedMps.Raw);
            Assert.Equal(new[] { 120U, 60U }, p.ShowUpCurve.Select(b => b.MinutesBeforeStd));
            Assert.Equal(new[] { 300U, 700U }, p.ShowUpCurve.Select(b => b.SharePermille));

            Assert.Equal(Id("size.c"), Aircraft("aircraft.a320", "size.c").SizeCategory);
            Assert.Equal(3, Size("size.c", 3).Ordinal);
        }

        [Fact]
        public void test_content_definition_ids_unique_across_all_kinds()
        {
            // 08 §8.11: ids are unique across all kinds. §8.11a (Q-028): a duplicate
            // id makes Create throw ArgumentException.
            var crossKind = new List<IContentDefinition> { Size("shared", 1), Aircraft("shared", "shared") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(crossKind));

            var sameKind = new List<IContentDefinition> { Queue("q"), Pax("p"), Queue("q") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(sameKind));

            var farApart = new List<IContentDefinition> { Pax("dup"), Size("z", 0), Size("a", 0), Queue("dup") };
            Assert.Throws<ArgumentException>(() => ContentIndexFactory.Create(farApart));
        }
    }
}
