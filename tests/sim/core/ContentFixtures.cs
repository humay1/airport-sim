using System.Collections.Generic;
using System.Linq;
using AirportSim.Sim.Core;

namespace AirportSim.Sim.Core.Tests
{
    /// <summary>Content definition builders shared by ContentTests and ContentIndexFactoryTests.</summary>
    internal static class ContentFixtures
    {
        internal static readonly ContentKind[] AllKinds =
        {
            ContentKind.SizeCategory, ContentKind.Aircraft, ContentKind.PaxProfile, ContentKind.QueueProfile,
        };

        internal static ContentId Id(string s) => new ContentId(s);

        internal static SizeCategoryDefinition Size(string id, int ordinal) => new SizeCategoryDefinition(Id(id), ordinal);

        internal static AircraftDefinition Aircraft(string id, string size) => new AircraftDefinition(Id(id), Id(size));

        internal static PaxProfileDefinition Pax(string id) =>
            new PaxProfileDefinition(Id(id), Fx.FromRaw(5L << 31), new[] { new ShowUpBucket(120U, 300U), new ShowUpBucket(60U, 700U) });

        internal static QueueProfileDefinition Queue(string id) =>
            new QueueProfileDefinition(Id(id), Fx.FromRaw(3L << 32), 250, Fx.FromRaw(10L << 32), Fx.FromRaw(2L << 32), DelayCategory.SecurityQueue);

        internal static List<string> Ids(IReadOnlyList<ContentId> ids) => ids.Select(i => i.Value).ToList();

        internal static List<string> OrdinalSorted(IEnumerable<string> ids)
        {
            var list = ids.ToList();
            list.Sort(string.CompareOrdinal);
            return list;
        }
    }
}
