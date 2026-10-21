namespace DandaUtils.Editor.UnityInternalsGenerator.Output
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    public static class ReportWriter
    {
        public const string NotSelectedLabel = "NotSelected";

        public static (string md, string json) Build(IReadOnlyList<ReportEntry> entries, string unityVersion, string host)
        {
            var sorted = entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            return (BuildMarkdown(sorted, unityVersion, host), BuildJson(sorted, unityVersion, host));
        }

        /// <summary>Selected entries per status (every <see cref="WrapStatus"/>, enum order), then <see cref="NotSelectedLabel"/> with the count of unselected entries.</summary>
        public static IReadOnlyList<KeyValuePair<string, int>> StatusCounts(IEnumerable<ReportEntry> entries)
        {
            var counts = new Dictionary<WrapStatus, int>();
            var notSelected = 0;
            foreach (var e in entries)
            {
                if (e.Selected == false)
                {
                    notSelected++;
                    continue;
                }
                counts.TryGetValue(e.Status, out var n);
                counts[e.Status] = n + 1;
            }

            var statuses = (WrapStatus[])Enum.GetValues(typeof(WrapStatus));
            var result = statuses.Select(s => new KeyValuePair<string, int>(s.ToString(), counts.TryGetValue(s, out var n) ? n : 0)).ToList();
            result.Add(new KeyValuePair<string, int>(NotSelectedLabel, notSelected));
            return result;
        }

        private static string BuildMarkdown(List<ReportEntry> sorted, string unityVersion, string host)
        {
            var sb = new StringBuilder();
            sb.Append("# Unity Internals Generation Report\n\n");
            sb.Append("- Unity version: ").Append(unityVersion).Append('\n');
            sb.Append("- Host assembly: ").Append(host).Append("\n\n");
            sb.Append("| Status | Count |\n|---|---|\n");
            foreach (var pair in StatusCounts(sorted)) sb.Append("| ").Append(pair.Key).Append(" | ").Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append(" |\n");

            var statuses = (WrapStatus[])Enum.GetValues(typeof(WrapStatus));

            foreach (var status in statuses)
            {
                if (status == WrapStatus.Wrapped) continue;
                var group = sorted.Where(e => e.Selected && e.Status == status).ToList();
                if (group.Count == 0) continue;
                sb.Append("\n## ").Append(status.ToString()).Append("\n\n");
                foreach (var e in group) sb.Append("- `").Append(e.Id).Append("` — ").Append(e.Rule).Append(" — ").Append(OneLine(e.Reason)).Append('\n');
            }

            return sb.ToString();
        }

        private static string OneLine(string s) => (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

        /// <summary>Only selected entries are listed (unselected ones would dominate the file); their number is <c>notSelectedCount</c>.</summary>
        private static string BuildJson(List<ReportEntry> all, string unityVersion, string host)
        {
            var sorted = all.Where(e => e.Selected).ToList();
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("    \"unityVersion\": ").Append(Quote(unityVersion)).Append(",\n");
            sb.Append("    \"host\": ").Append(Quote(host)).Append(",\n");
            sb.Append("    \"notSelectedCount\": ").Append((all.Count - sorted.Count).ToString(CultureInfo.InvariantCulture)).Append(",\n");
            if (sorted.Count == 0)
            {
                sb.Append("    \"entries\": []\n}\n");
                return sb.ToString();
            }

            sb.Append("    \"entries\": [\n");
            for (var i = 0; i < sorted.Count; i++)
            {
                var e = sorted[i];
                sb.Append("        {\"id\": ").Append(Quote(e.Id));
                sb.Append(", \"rule\": ").Append(Quote(e.Rule));
                sb.Append(", \"status\": ").Append(Quote(e.Status.ToString()));
                sb.Append(", \"selected\": ").Append(e.Selected ? "true" : "false");
                sb.Append(", \"reason\": ").Append(Quote(e.Reason)).Append('}');
                sb.Append(i < sorted.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("    ]\n}\n");
            return sb.ToString();
        }

        internal static string Quote(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
