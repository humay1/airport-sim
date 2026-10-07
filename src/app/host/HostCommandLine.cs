using System;
using System.Collections.Generic;

namespace AirportSim.App.Host
{
    /// <summary>Recognises <c>-airportsim-checkpoints &lt;days&gt; &lt;outputPath&gt;</c>. Spec: 16 §16.8 (Q-114).</summary>
    internal sealed class HostCommandLine : IHostCommandLine
    {
        internal const string Token = "-airportsim-checkpoints";

        private const uint MaxDays = 298261;

        public bool TryParse(IReadOnlyList<string> args, out CheckpointRunRequest request)
        {
            request = default;
            if (args == null)
            {
                return false;
            }

            int at = -1;
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], Token, StringComparison.Ordinal))
                {
                    if (at >= 0)
                    {
                        return false;
                    }

                    at = i;
                }
            }

            if (at < 0 || at + 2 >= args.Count)
            {
                return false;
            }

            string path = args[at + 2];
            if (!TryDays(args[at + 1], out uint days) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            request = new CheckpointRunRequest(days, path);
            return true;
        }

        /// <summary>ASCII digits, no sign, no leading zero, 1 to 298 261.</summary>
        private static bool TryDays(string text, out uint days)
        {
            days = 0;
            if (text == null || text.Length == 0 || text.Length > 6 || text[0] == '0')
            {
                return false;
            }

            uint value = 0;
            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }

                value = (value * 10) + (uint)(c - '0');
            }

            if (value > MaxDays)
            {
                return false;
            }

            days = value;
            return true;
        }
    }
}
