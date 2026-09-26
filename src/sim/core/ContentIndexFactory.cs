using System;
using System.Collections.Generic;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Builds a read-only, immutable-for-the-session <see cref="IContentIndex"/> from
    /// validated definitions. Spec: 08-interfaces-core.md §8.11a. Sorts each kind's ids
    /// by ordinal comparison and throws on a duplicate id, across all kinds, not just
    /// within one. This is the one piece of real logic T-026 owns (Q-011); parsing
    /// <c>data/</c> into definitions is T-027's <c>IContentLoader</c>.
    /// </summary>
    public static class ContentIndexFactory
    {
        /// <summary>
        /// Builds the index from <paramref name="definitions"/>. The result does not
        /// change when the caller later mutates the list it passed in.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// An element is null, an element's <see cref="ContentId.Value"/> is null, or two
        /// elements share a <see cref="ContentId"/>.
        /// </exception>
        public static IContentIndex Create(IReadOnlyList<IContentDefinition> definitions)
        {
            if (definitions is null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            var byId = new Dictionary<ContentId, IContentDefinition>();
            var idsByKind = new Dictionary<ContentKind, List<ContentId>>();

            foreach (IContentDefinition def in definitions)
            {
                if (def is null)
                {
                    throw new ArgumentException("a content definition element is null", nameof(definitions));
                }

                if (def.Id.Value is null)
                {
                    throw new ArgumentException("a content definition's id has a null Value", nameof(definitions));
                }

                if (byId.ContainsKey(def.Id))
                {
                    throw new ArgumentException("duplicate content id: " + def.Id.Value, nameof(definitions));
                }

                byId.Add(def.Id, def);

                if (!idsByKind.TryGetValue(def.Kind, out List<ContentId>? ids))
                {
                    ids = new List<ContentId>();
                    idsByKind.Add(def.Kind, ids);
                }

                ids.Add(def.Id);
            }

            var sortedByKind = new Dictionary<ContentKind, IReadOnlyList<ContentId>>();
            foreach (KeyValuePair<ContentKind, List<ContentId>> kv in idsByKind)
            {
                kv.Value.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
                sortedByKind[kv.Key] = kv.Value.AsReadOnly();
            }

            return new ContentIndex(byId, sortedByKind);
        }

        private sealed class ContentIndex : IContentIndex
        {
            private readonly IReadOnlyDictionary<ContentId, IContentDefinition> _byId;
            private readonly IReadOnlyDictionary<ContentKind, IReadOnlyList<ContentId>> _idsByKind;

            public ContentIndex(
                IReadOnlyDictionary<ContentId, IContentDefinition> byId,
                IReadOnlyDictionary<ContentKind, IReadOnlyList<ContentId>> idsByKind)
            {
                _byId = byId;
                _idsByKind = idsByKind;
            }

            public bool TryGet<T>(ContentId id, out T definition) where T : IContentDefinition
            {
                if (id.Value is null)
                {
                    throw new ArgumentException("id.Value is null", nameof(id));
                }

                if (_byId.TryGetValue(id, out IContentDefinition? found) && found is T typed)
                {
                    definition = typed;
                    return true;
                }

                definition = default!;
                return false;
            }

            public IReadOnlyList<ContentId> AllOf(ContentKind kind)
            {
                return _idsByKind.TryGetValue(kind, out IReadOnlyList<ContentId>? ids) ? ids : Array.Empty<ContentId>();
            }
        }
    }
}
