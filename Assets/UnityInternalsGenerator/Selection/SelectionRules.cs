namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Immutable allowlist: ids in <see cref="Included"/> select a node and everything below it,
    /// ids in <see cref="Excluded"/> carve exceptions. The deepest rule on the ancestry chain wins.
    /// </summary>
    public sealed class SelectionRules
    {
        private const int c_MaxAncestorDepth = 1024;

        private static readonly SelectionRules s_All = new SelectionRules(isAll: true);
        private static readonly SelectionRules s_Empty = new SelectionRules(Array.Empty<string>(), Array.Empty<string>());

        private readonly string[] m_Included;
        private readonly string[] m_Excluded;
        private readonly bool m_IsAll;

        public SelectionRules(IEnumerable<string> included, IEnumerable<string> excluded)
        {
            if (included == null) throw new ArgumentNullException(nameof(included));
            if (excluded == null) throw new ArgumentNullException(nameof(excluded));

            m_Included = Normalize(included);
            m_Excluded = Normalize(excluded);
        }

        private SelectionRules(bool isAll)
        {
            m_Included = Array.Empty<string>();
            m_Excluded = Array.Empty<string>();
            m_IsAll = isAll;
        }

        /// <summary>Sentinel that selects everything, including members and types not yet known.</summary>
        public static SelectionRules All => s_All;

        /// <summary>No rules at all: nothing is selected.</summary>
        public static SelectionRules Empty => s_Empty;

        public IReadOnlyList<string> Included => m_Included;

        public IReadOnlyList<string> Excluded => m_Excluded;

        /// <summary>True when nothing can be selected: no Included ids and not <see cref="All"/>. Exclusions alone select nothing, so they still count as empty.</summary>
        public bool IsEmpty => m_IsAll == false && m_Included.Length == 0;

        /// <summary>True when the id is listed as included (true), excluded (false), or has no rule (null). Excluded wins if listed in both.</summary>
        public bool? RuleAt(string id)
        {
            if (m_IsAll || id == null) return null;
            if (Contains(m_Excluded, id)) return false;
            if (Contains(m_Included, id)) return true;
            return null;
        }

        /// <summary>Walks id and its ancestors; the first rule found decides. No rule at all (or a chain deeper than the guard, i.e. a cycle) means not selected.</summary>
        public bool IsSelected(string id, Func<string, string> parentOf)
        {
            if (m_IsAll) return true;
            if (parentOf == null) throw new ArgumentNullException(nameof(parentOf));

            var current = id;
            for (var depth = 0; current != null; depth++)
            {
                if (depth >= c_MaxAncestorDepth) return false;

                var rule = RuleAt(current);
                if (rule.HasValue) return rule.Value;

                current = parentOf(current);
            }

            return false;
        }

        /// <summary>Drops null entries, dedups and sorts ordinal. Null and empty-string entries are dropped, not rejected.</summary>
        private static string[] Normalize(IEnumerable<string> ids) => ids.Where(id => string.IsNullOrEmpty(id) == false).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        private static bool Contains(string[] sortedIds, string id) => Array.BinarySearch(sortedIds, id, StringComparer.Ordinal) >= 0;
    }

    public static class SelectionIds
    {
        /// <summary>Prefix of every namespace node id.</summary>
        public const string NamespacePrefix = "ns:";

        /// <summary>Node id for a namespace. Null or empty means the global namespace ("ns:").</summary>
        public static string Namespace(string ns) => NamespacePrefix + (ns ?? "");

        /// <summary>Full namespace name of a namespace node id ("ns:A.B" gives "A.B", global "ns:" gives ""); null for ids that are not namespace ids.</summary>
        public static string NamespaceName(string nsId) => nsId != null && nsId.StartsWith(NamespacePrefix, StringComparison.Ordinal) ? nsId.Substring(NamespacePrefix.Length) : null;

        /// <summary>Parent namespace id: "ns:A.B" gives "ns:A"; top-level "ns:A" and global "ns:" give null.</summary>
        public static string NamespaceParent(string nsId)
        {
            var name = NamespaceName(nsId);
            if (name == null) return null;

            var lastDot = name.LastIndexOf('.');
            if (lastDot < 0) return null;

            return NamespacePrefix + name.Substring(0, lastDot);
        }
    }
}
