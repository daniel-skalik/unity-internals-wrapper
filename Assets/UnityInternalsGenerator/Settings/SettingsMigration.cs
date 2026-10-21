namespace DandaUtils.Editor.UnityInternalsGenerator.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;

    /// <summary>Pure helpers that upgrade and normalize the stored selection id lists.</summary>
    public static class SettingsMigration
    {
        /// <summary>Moves legacy excluded namespaces into the exception ids as "ns:" ids, clears them and normalizes. Returns true when anything changed.</summary>
        public static bool MigrateNamespaces(List<string> excludedNamespaces, List<string> excludedIds)
        {
            if (excludedNamespaces == null) throw new ArgumentNullException(nameof(excludedNamespaces));
            if (excludedIds == null) throw new ArgumentNullException(nameof(excludedIds));

            var merged = excludedIds.Concat(excludedNamespaces.Where(n => n != null).Select(SelectionIds.Namespace));
            var normalized = Normalize(merged);
            var changed = excludedNamespaces.Count > 0 || normalized.SequenceEqual(excludedIds, StringComparer.Ordinal) == false;
            if (changed == false) return false;

            excludedNamespaces.Clear();
            excludedIds.Clear();
            excludedIds.AddRange(normalized);
            return true;
        }

        /// <summary>Distinct, non-empty ids sorted ordinal.</summary>
        public static List<string> Normalize(IEnumerable<string> ids) => ids.Where(id => string.IsNullOrEmpty(id) == false).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
    }
}
