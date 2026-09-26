using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AirportSim.Sim.Core;
using Xunit;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>
    /// Test-only helpers for the T-027 content loader tests, written from
    /// 08-interfaces-core.md §8.11 "The loader", 04-data-schemas.md "Phase 0/1
    /// content fields", 11 §11.6 and 07 "Error handling" (Q-030), never from an
    /// implementation. The on-disk fixture is tests/fixtures/content/valid,
    /// listed by valid.files; every failure case is that tree with one file
    /// replaced or added, in memory.
    /// </summary>
    internal static class LoaderTestKit
    {
        private const string FixtureDir = "tests/fixtures/content";

        /// <summary>An IContentSource over in-memory files, returning Files() in the order given.</summary>
        internal sealed class MemorySource : IContentSource
        {
            private readonly Dictionary<string, byte[]> _files;
            private readonly List<string> _order;

            public MemorySource(IEnumerable<KeyValuePair<string, byte[]>> files)
            {
                _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                _order = new List<string>();
                foreach (KeyValuePair<string, byte[]> f in files)
                {
                    _files.Add(f.Key, f.Value);
                    _order.Add(f.Key);
                }
            }

            public IReadOnlyList<string> Order => _order;

            public IReadOnlyList<string> Files()
            {
                return _order.ToArray();
            }

            public byte[] ReadAll(string path)
            {
                return (byte[])_files[path].Clone();
            }

            /// <summary>This source with <paramref name="path"/> replaced, or appended if absent.</summary>
            public MemorySource With(string path, byte[] bytes)
            {
                var list = _order.Select(p => new KeyValuePair<string, byte[]>(p, p == path ? bytes : _files[p])).ToList();
                if (!_files.ContainsKey(path))
                {
                    list.Add(new KeyValuePair<string, byte[]>(path, bytes));
                }
                return new MemorySource(list);
            }

            public MemorySource With(string path, string text)
            {
                return With(path, Utf8(text));
            }

            public MemorySource Without(string path)
            {
                return new MemorySource(_order.Where(p => p != path).Select(p => new KeyValuePair<string, byte[]>(p, _files[p])));
            }

            public MemorySource InOrder(IEnumerable<string> order)
            {
                return new MemorySource(order.Select(p => new KeyValuePair<string, byte[]>(p, _files[p])));
            }
        }

        internal static byte[] Utf8(string text)
        {
            return new UTF8Encoding(false).GetBytes(text);
        }

        /// <summary>Walks up from the test binaries to the repository's fixture directory.</summary>
        internal static string FixtureRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, FixtureDir.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(Path.Combine(candidate, "valid.files")))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("fixture directory " + FixtureDir + " not found above " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// The valid fixture, with Files() in the manifest's order, which is
        /// deliberately not ordinal.
        /// </summary>
        internal static MemorySource Valid()
        {
            string root = FixtureRoot();
            string[] paths = File.ReadAllLines(Path.Combine(root, "valid.files"))
                .Where(l => l.Length > 0)
                .ToArray();
            string validRoot = Path.Combine(root, "valid");
            return new MemorySource(paths.Select(p => new KeyValuePair<string, byte[]>(
                p, File.ReadAllBytes(Path.Combine(validRoot, p.Replace('/', Path.DirectorySeparatorChar))))));
        }

        internal static IReadOnlyList<IContentDefinition> Load(IContentSource source)
        {
            return ContentLoaderFactory.Create().Load(source);
        }

        /// <summary>
        /// Asserts a load failure per Q-030: exactly FormatException, the message
        /// starting with the offending file's path, and naming one of the ids.
        /// </summary>
        internal static FormatException AssertLoadFails(IContentSource source, string[] paths, params string[] anyOfIds)
        {
            FormatException ex = Assert.Throws<FormatException>(() => Load(source));
            Assert.True(paths.Any(p => ex.Message.StartsWith(p, StringComparison.Ordinal)),
                "message must start with " + string.Join(" or ", paths) + ": " + ex.Message);
            if (anyOfIds.Length > 0)
            {
                Assert.True(anyOfIds.Any(id => ex.Message.Contains(id, StringComparison.Ordinal)),
                    "message must name " + string.Join(" or ", anyOfIds) + ": " + ex.Message);
            }
            return ex;
        }

        internal static FormatException AssertLoadFails(IContentSource source, string path, params string[] anyOfIds)
        {
            return AssertLoadFails(source, new[] { path }, anyOfIds);
        }

        // ------------------------------------------------------------ expected output of valid/

        /// <summary>The kind files of valid/ in ordinal path order.</summary>
        internal static readonly string[] ValidKindPathsOrdinal =
        {
            "aircraft/B777.json",
            "aircraft/a10.json",
            "aircraft/a2.json",
            "aircraft/a320.json",
            "pax_profiles/business.json",
            "pax_profiles/leisure.json",
            "queue_profiles/immigration_main.json",
            "queue_profiles/security_main.json",
            "size_categories/large.json",
            "size_categories/medium.json",
            "size_categories/small.json",
        };

        /// <summary>The definitions of valid/, in the same order as <see cref="ValidKindPathsOrdinal"/>.</summary>
        internal static IContentDefinition[] ExpectedValid()
        {
            return new IContentDefinition[]
            {
                new AircraftDefinition(new ContentId("aircraft.b777"), new ContentId("size.large")),
                new AircraftDefinition(new ContentId("aircraft.a10"), new ContentId("size.small")),
                new AircraftDefinition(new ContentId("aircraft.a2"), new ContentId("size.small")),
                new AircraftDefinition(new ContentId("aircraft.a320"), new ContentId("size.medium")),
                new PaxProfileDefinition(new ContentId("pax.business"), Fx.Parse("1.34"), new[]
                {
                    new ShowUpBucket(30U, 100U), new ShowUpBucket(60U, 600U), new ShowUpBucket(120U, 300U),
                }),
                new PaxProfileDefinition(new ContentId("pax.leisure"), Fx.Parse("0.0000000003"), new[] { new ShowUpBucket(0U, 1000U) }),
                new QueueProfileDefinition(new ContentId("queue.immigration_main"), Fx.Parse("0"), int.MaxValue,
                    Fx.Parse("0.5"), Fx.Parse("0"), DelayCategory.ImmigrationQueue),
                new QueueProfileDefinition(new ContentId("queue.security_main"), Fx.Parse("2.5"), 120,
                    Fx.Parse("10"), Fx.Parse("2.5"), DelayCategory.SecurityQueue),
                new SizeCategoryDefinition(new ContentId("size.large"), -3),
                new SizeCategoryDefinition(new ContentId("size.medium"), 2),
                new SizeCategoryDefinition(new ContentId("size.small"), 1),
            };
        }

        /// <summary>Field-by-field equality of two definition lists, order included.</summary>
        internal static void AssertSameDefinitions(IReadOnlyList<IContentDefinition> expected, IReadOnlyList<IContentDefinition> actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                AssertSameDefinition(expected[i], actual[i], i);
            }
        }

        private static void AssertSameDefinition(IContentDefinition e, IContentDefinition a, int index)
        {
            string at = "definition " + index + " (" + e.Id.Value + ")";
            Assert.True(e.GetType() == a.GetType(), at + ": type " + a.GetType().Name + ", expected " + e.GetType().Name);
            Assert.True(e.Id.Value == a.Id.Value, at + ": id " + a.Id.Value);
            Assert.True(e.Kind == a.Kind, at + ": kind " + a.Kind);
            switch (e)
            {
                case SizeCategoryDefinition s:
                    Assert.True(s.Ordinal == ((SizeCategoryDefinition)a).Ordinal, at + ": ordinal");
                    break;
                case AircraftDefinition ac:
                    Assert.True(ac.SizeCategory.Value == ((AircraftDefinition)a).SizeCategory.Value, at + ": size category");
                    break;
                case PaxProfileDefinition p:
                {
                    var pa = (PaxProfileDefinition)a;
                    Assert.True(p.WalkSpeedMps == pa.WalkSpeedMps, at + ": walk speed raw " + pa.WalkSpeedMps.Raw);
                    Assert.True(p.ShowUpCurve.Count == pa.ShowUpCurve.Count, at + ": curve length");
                    for (int k = 0; k < p.ShowUpCurve.Count; k++)
                    {
                        Assert.True(p.ShowUpCurve[k].MinutesBeforeStd == pa.ShowUpCurve[k].MinutesBeforeStd, at + ": bucket " + k + " minutes");
                        Assert.True(p.ShowUpCurve[k].SharePermille == pa.ShowUpCurve[k].SharePermille, at + ": bucket " + k + " share");
                    }
                    break;
                }
                case QueueProfileDefinition q:
                {
                    var qa = (QueueProfileDefinition)a;
                    Assert.True(q.ServiceRatePerServerPerMinute == qa.ServiceRatePerServerPerMinute, at + ": service rate");
                    Assert.True(q.CapacityStanding == qa.CapacityStanding, at + ": capacity");
                    Assert.True(q.ThresholdWaitMinutes == qa.ThresholdWaitMinutes, at + ": threshold");
                    Assert.True(q.HysteresisMinutes == qa.HysteresisMinutes, at + ": hysteresis");
                    Assert.True(q.Category == qa.Category, at + ": category");
                    break;
                }
                default:
                    Assert.Fail(at + ": unexpected definition type");
                    break;
            }
        }
    }
}
