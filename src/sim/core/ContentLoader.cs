using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AirportSim.Sim.Core
{
    /// <summary>
    /// Builds the one <see cref="IContentLoader"/> implementation. Spec:
    /// 08-interfaces-core.md §8.11 "The loader" (Q-011). Stateless, caches
    /// nothing, reads nothing but its argument (07-conventions.md "Factories").
    /// </summary>
    public static class ContentLoaderFactory
    {
        /// <summary>Creates a fresh loader. Never throws.</summary>
        public static IContentLoader Create()
        {
            return new ContentLoader();
        }
    }

    /// <summary>
    /// The one <see cref="IContentLoader"/> implementation: a hand-written,
    /// package-free JSON subset parser (<see cref="JsonParser"/>) plus the field
    /// and cross-file validation of 08-interfaces-core.md §8.11 and
    /// 04-data-schemas.md "Phase 0/1 content fields". Every failure throws
    /// <see cref="FormatException"/> (07-conventions.md "Error handling", Q-030).
    /// </summary>
    internal sealed class ContentLoader : IContentLoader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <inheritdoc/>
        public IReadOnlyList<IContentDefinition> Load(IContentSource source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            IReadOnlyList<string>? rawFiles = source.Files();
            if (rawFiles is null)
            {
                throw new ArgumentException("IContentSource.Files() returned null", nameof(source));
            }

            // Order: files are read in ordinal path order, whatever Files()
            // returns, so the result never depends on file-system enumeration
            // (08 §8.11, 07-conventions.md "Runtime portability" rule 1).
            var sorted = new List<string>(rawFiles);
            sorted.Sort(StringComparer.Ordinal);

            var entries = new List<(string Path, IContentDefinition Def)>();
            for (int i = 0; i < sorted.Count; i++)
            {
                string path = sorted[i];
                if (!TryKindDirectory(path, out ContentKind kind, out int kindDirectorySlash) || !path.EndsWith(".json", StringComparison.Ordinal))
                {
                    continue; // Not one of the four kind directories, or not a *.json file — ignored (§8.11).
                }

                if (path.IndexOf('/', kindDirectorySlash + 1) >= 0)
                {
                    // Only files directly inside a kind directory are definitions; a
                    // nested *.json is a load failure, not silently ignored (Q-031 C3).
                    throw JsonParser.Fail(path, "a *.json file must be directly inside its kind directory, not nested further");
                }

                byte[] bytes = source.ReadAll(path);
                IContentDefinition def = ParseDefinition(path, kind, bytes);
                entries.Add((path, def));
            }

            ValidateCrossFile(entries);

            var result = new IContentDefinition[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                result[i] = entries[i].Def;
            }

            return result;
        }

        // ---------------------------------------------------------- directories

        private static bool TryKindDirectory(string path, out ContentKind kind, out int directorySlash)
        {
            int slash = path.IndexOf('/');
            if (slash < 0)
            {
                kind = default;
                directorySlash = -1;
                return false;
            }

            directorySlash = slash;
            string dir = path.Substring(0, slash);
            switch (dir)
            {
                case "size_categories": kind = ContentKind.SizeCategory; return true;
                case "aircraft": kind = ContentKind.Aircraft; return true;
                case "pax_profiles": kind = ContentKind.PaxProfile; return true;
                case "queue_profiles": kind = ContentKind.QueueProfile; return true;
                default: kind = default; return false;
            }
        }

        // ---------------------------------------------------------- per-file parse

        private static IContentDefinition ParseDefinition(string path, ContentKind kind, byte[] bytes)
        {
            string text = DecodeUtf8Strict(bytes, path);
            JsonValue root = JsonParser.Parse(text, path);
            if (!(root is JsonObject obj))
            {
                throw JsonParser.Fail(path, "the document's top-level value must be an object");
            }

            switch (kind)
            {
                case ContentKind.SizeCategory: return ParseSizeCategory(obj, path);
                case ContentKind.Aircraft: return ParseAircraft(obj, path);
                case ContentKind.PaxProfile: return ParsePaxProfile(obj, path);
                default: return ParseQueueProfile(obj, path);
            }
        }

        private static string DecodeUtf8Strict(byte[] bytes, string path)
        {
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw JsonParser.Fail(path, "invalid UTF-8");
            }
        }

        // ---------------------------------------------------------- kind parsers

        private static SizeCategoryDefinition ParseSizeCategory(JsonObject obj, string path)
        {
            RequireExactKeys(obj, path, new[] { "schema_version", "id", "ordinal" });
            RequireSchemaVersionOne(obj, path);
            ContentId id = RequireId(obj, path);
            JsonInteger ordinalNode = RequireIntegerNode(obj, path, "ordinal", id.Value);
            int ordinal = RequireInt32(ordinalNode, path, "ordinal");
            return new SizeCategoryDefinition(id, ordinal);
        }

        private static AircraftDefinition ParseAircraft(JsonObject obj, string path)
        {
            RequireExactKeys(obj, path, new[] { "schema_version", "id", "size_category" });
            RequireSchemaVersionOne(obj, path);
            ContentId id = RequireId(obj, path);
            string sizeCategory = RequireStringValue(obj, path, "size_category", id.Value);
            return new AircraftDefinition(id, new ContentId(sizeCategory));
        }

        private static PaxProfileDefinition ParsePaxProfile(JsonObject obj, string path)
        {
            RequireExactKeys(obj, path, new[] { "schema_version", "id", "walk_speed_mps", "show_up_curve" });
            RequireSchemaVersionOne(obj, path);
            ContentId id = RequireId(obj, path);
            string walkSpeedRaw = RequireStringValue(obj, path, "walk_speed_mps", id.Value);
            Fx walkSpeed = ParseDecimalField(walkSpeedRaw, path, "walk_speed_mps", id.Value);
            if (walkSpeed <= Fx.Zero)
            {
                throw JsonParser.Fail(path, "walk_speed_mps must be > 0 (id '" + id.Value + "')");
            }

            IReadOnlyList<ShowUpBucket> curve = ParseShowUpCurve(obj, path, id.Value);
            return new PaxProfileDefinition(id, walkSpeed, curve);
        }

        private static QueueProfileDefinition ParseQueueProfile(JsonObject obj, string path)
        {
            RequireExactKeys(obj, path, new[]
            {
                "schema_version", "id", "service_rate_per_server_per_minute", "capacity_standing",
                "threshold_wait_minutes", "hysteresis_minutes", "delay_category",
            });
            RequireSchemaVersionOne(obj, path);
            ContentId id = RequireId(obj, path);

            string rateRaw = RequireStringValue(obj, path, "service_rate_per_server_per_minute", id.Value);
            Fx rate = ParseDecimalField(rateRaw, path, "service_rate_per_server_per_minute", id.Value);
            if (rate < Fx.Zero)
            {
                throw JsonParser.Fail(path, "service_rate_per_server_per_minute must be >= 0 (id '" + id.Value + "')");
            }

            JsonInteger capacityNode = RequireIntegerNode(obj, path, "capacity_standing", id.Value);
            int capacity = RequireInt32(capacityNode, path, "capacity_standing");
            if (capacity <= 0)
            {
                throw JsonParser.Fail(path, "capacity_standing must be > 0 (id '" + id.Value + "')");
            }

            string thresholdRaw = RequireStringValue(obj, path, "threshold_wait_minutes", id.Value);
            Fx threshold = ParseDecimalField(thresholdRaw, path, "threshold_wait_minutes", id.Value);

            string hysteresisRaw = RequireStringValue(obj, path, "hysteresis_minutes", id.Value);
            Fx hysteresis = ParseDecimalField(hysteresisRaw, path, "hysteresis_minutes", id.Value);
            if (hysteresis < Fx.Zero || !(hysteresis < threshold))
            {
                throw JsonParser.Fail(path, "hysteresis_minutes must satisfy 0 <= hysteresis_minutes < threshold_wait_minutes (id '" + id.Value + "')");
            }

            string categoryRaw = RequireStringValue(obj, path, "delay_category", id.Value);
            DelayCategory category;
            if (string.Equals(categoryRaw, "security_queue", StringComparison.Ordinal))
            {
                category = DelayCategory.SecurityQueue;
            }
            else if (string.Equals(categoryRaw, "immigration_queue", StringComparison.Ordinal))
            {
                category = DelayCategory.ImmigrationQueue;
            }
            else
            {
                throw JsonParser.Fail(path, "delay_category must be 'security_queue' or 'immigration_queue' (id '" + id.Value + "')");
            }

            return new QueueProfileDefinition(id, rate, capacity, threshold, hysteresis, category);
        }

        private static IReadOnlyList<ShowUpBucket> ParseShowUpCurve(JsonObject obj, string path, string id)
        {
            if (!obj.TryGet("show_up_curve", out JsonValue v) || !(v is JsonArray arr))
            {
                throw JsonParser.Fail(path, "'show_up_curve' must be an array (id '" + id + "')");
            }

            var buckets = new List<ShowUpBucket>(arr.Items.Count);
            for (int i = 0; i < arr.Items.Count; i++)
            {
                if (!(arr.Items[i] is JsonObject bucketObj))
                {
                    throw JsonParser.Fail(path, "show_up_curve[" + i.ToString(CultureInfo.InvariantCulture) + "] must be an object (id '" + id + "')");
                }

                RequireExactKeys(bucketObj, path, new[] { "minutes_before_std", "share_permille" });
                JsonInteger minutesNode = RequireIntegerNode(bucketObj, path, "minutes_before_std", id);
                JsonInteger shareNode = RequireIntegerNode(bucketObj, path, "share_permille", id);
                uint minutes = RequireUInt32(minutesNode, path, "minutes_before_std");
                uint share = RequireUInt32(shareNode, path, "share_permille");
                buckets.Add(new ShowUpBucket(minutes, share));
            }

            ulong sum = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i > 0 && buckets[i].MinutesBeforeStd <= buckets[i - 1].MinutesBeforeStd)
                {
                    throw JsonParser.Fail(path, "show_up_curve must be strictly ascending in minutes_before_std (id '" + id + "')");
                }

                sum += buckets[i].SharePermille;
            }

            if (sum != 1000UL)
            {
                throw JsonParser.Fail(path, "show_up_curve share_permille must sum to exactly 1000 (id '" + id + "')");
            }

            return buckets.AsReadOnly();
        }

        // ---------------------------------------------------------- field helpers

        private static void RequireExactKeys(JsonObject obj, string path, string[] required)
        {
            if (obj.Members.Count != required.Length)
            {
                throw JsonParser.Fail(path, "object must have exactly the keys: " + string.Join(", ", required));
            }

            for (int i = 0; i < obj.Members.Count; i++)
            {
                string key = obj.Members[i].Key;
                bool known = false;
                for (int k = 0; k < required.Length; k++)
                {
                    if (string.Equals(required[k], key, StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw JsonParser.Fail(path, "unknown key '" + key + "'");
                }
            }
        }

        private static void RequireSchemaVersionOne(JsonObject obj, string path)
        {
            JsonInteger version = RequireIntegerNode(obj, path, "schema_version", "?");
            if (version.Negative || !string.Equals(version.Digits, "1", StringComparison.Ordinal))
            {
                throw JsonParser.Fail(path, "schema_version must be 1");
            }
        }

        private static ContentId RequireId(JsonObject obj, string path)
        {
            if (!obj.TryGet("id", out JsonValue v) || !(v is JsonString s))
            {
                throw JsonParser.Fail(path, "'id' must be a string");
            }

            return new ContentId(s.Value);
        }

        private static string RequireStringValue(JsonObject obj, string path, string key, string id)
        {
            if (!obj.TryGet(key, out JsonValue v) || !(v is JsonString s))
            {
                throw JsonParser.Fail(path, "'" + key + "' must be a string (id '" + id + "')");
            }

            return s.Value;
        }

        private static JsonInteger RequireIntegerNode(JsonObject obj, string path, string key, string id)
        {
            if (!obj.TryGet(key, out JsonValue v) || !(v is JsonInteger n))
            {
                throw JsonParser.Fail(path, "'" + key + "' must be an integer (id '" + id + "')");
            }

            return n;
        }

        private static Fx ParseDecimalField(string raw, string path, string field, string id)
        {
            try
            {
                return Fx.Parse(raw);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                throw JsonParser.Fail(path, field + " '" + raw + "' is not a valid decimal for id '" + id + "': " + ex.Message);
            }
        }

        private static bool TryMagnitude(string digits, out ulong value)
        {
            return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static int RequireInt32(JsonInteger n, string path, string field)
        {
            if (!TryMagnitude(n.Digits, out ulong mag))
            {
                throw JsonParser.Fail(path, field + " is out of range");
            }

            if (!n.Negative)
            {
                if (mag > int.MaxValue)
                {
                    throw JsonParser.Fail(path, field + " is out of range");
                }

                return (int)mag;
            }

            const ulong intMinMagnitude = 1UL << 31;
            if (mag > intMinMagnitude)
            {
                throw JsonParser.Fail(path, field + " is out of range");
            }

            return mag == intMinMagnitude ? int.MinValue : -(int)mag;
        }

        private static uint RequireUInt32(JsonInteger n, string path, string field)
        {
            if (n.Negative)
            {
                throw JsonParser.Fail(path, field + " must not be negative");
            }

            if (!TryMagnitude(n.Digits, out ulong mag) || mag > uint.MaxValue)
            {
                throw JsonParser.Fail(path, field + " is out of range");
            }

            return (uint)mag;
        }

        // ---------------------------------------------------------- cross-file validation

        private static void ValidateCrossFile(List<(string Path, IContentDefinition Def)> entries)
        {
            // Ids unique across all files and all kinds (08 §8.11).
            var seenIds = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                string idValue = entries[i].Def.Id.Value;
                if (seenIds.ContainsKey(idValue))
                {
                    throw JsonParser.Fail(entries[i].Path, "duplicate content id '" + idValue + "'");
                }

                seenIds.Add(idValue, true);
            }

            // Size ordinals unique.
            var seenOrdinals = new Dictionary<int, string>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Def is SizeCategoryDefinition sc)
                {
                    if (seenOrdinals.TryGetValue(sc.Ordinal, out string? firstId))
                    {
                        throw JsonParser.Fail(entries[i].Path,
                            "duplicate size category ordinal " + sc.Ordinal.ToString(CultureInfo.InvariantCulture)
                            + " (ids '" + firstId + "' and '" + sc.Id.Value + "')");
                    }

                    seenOrdinals.Add(sc.Ordinal, sc.Id.Value);
                }
            }

            // AircraftDefinition.SizeCategory resolves to a known size category id.
            var sizeCategoryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Def is SizeCategoryDefinition sc)
                {
                    sizeCategoryIds.Add(sc.Id.Value);
                }
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Def is AircraftDefinition ac && !sizeCategoryIds.Contains(ac.SizeCategory.Value))
                {
                    throw JsonParser.Fail(entries[i].Path,
                        "aircraft '" + ac.Id.Value + "' references unresolved size_category '" + ac.SizeCategory.Value + "'");
                }
            }
        }
    }
}
