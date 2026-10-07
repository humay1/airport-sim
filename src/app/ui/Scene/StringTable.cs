using System;

namespace AirportSim.App.Ui
{
    /// <summary>A player-visible text's key, compared ordinally. Spec: 17 §17.4b.</summary>
    public readonly struct LocalisedKey : IEquatable<LocalisedKey>
    {
        /// <summary>Creates a key from its text, for example <c>ui.settings.title</c>.</summary>
        public LocalisedKey(string value)
        {
            Value = value;
        }

        /// <summary>The key's text: dot-separated lowercase segments (§17.4b).</summary>
        public string Value { get; }

        /// <summary>True when both keys hold the same <see cref="Value"/>, ordinally.</summary>
        public bool Equals(LocalisedKey other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is LocalisedKey other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }
    }

    /// <summary>The English text lookup. Spec: 17 §17.4b.</summary>
    public interface IStringTable
    {
        /// <summary>
        /// The file's text for <paramref name="key"/>; allocates nothing. Throws
        /// <see cref="ArgumentException"/> (<c>key</c>) for a key that is not one of the twelve.
        /// </summary>
        string Resolve(LocalisedKey key);
    }

    /// <summary>The loaded table: twelve texts, fixed after construction (§17.4b).</summary>
    internal sealed class StringTable : IStringTable
    {
        internal const int KeyCount = 12;

        private readonly string[] _texts;

        internal StringTable(string[] texts)
        {
            _texts = texts;
        }

        /// <summary>The slot of a Phase 1 key, or -1 when <paramref name="key"/> is null or not one of the twelve.</summary>
        internal static int SlotOf(string? key)
        {
            switch (key)
            {
                case "ui.settings.title": return 0;
                case "ui.settings.preset.low": return 1;
                case "ui.settings.preset.medium": return 2;
                case "ui.settings.preset.high": return 3;
                case "ui.settings.knob.draw_agents": return 4;
                case "ui.settings.knob.max_drawn_agents_per_node": return 5;
                case "ui.settings.knob.frame_rate_cap": return 6;
                case "ui.settings.knob.resolution_scale_percent": return 7;
                case "ui.settings.knob.anti_aliasing": return 8;
                case "ui.settings.value.on": return 9;
                case "ui.settings.value.off": return 10;
                case "ui.settings.value.uncapped": return 11;
                default: return -1;
            }
        }

        public string Resolve(LocalisedKey key)
        {
            int slot = SlotOf(key.Value);
            if (slot < 0)
            {
                throw new ArgumentException("not one of the Phase 1 string keys", nameof(key));
            }

            return _texts[slot];
        }
    }
}
