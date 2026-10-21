namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    /// <summary>Pure: combines a <see cref="Catalog"/> and <see cref="SelectionRules"/> into the emitted set.</summary>
    public static class SelectionResolver
    {
        public static SelectionResult Resolve(Catalog catalog, SelectionRules rules)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var selected = new HashSet<string>(catalog.Nodes.Where(n => rules.IsSelected(n.Id, catalog.ParentOf)).Select(n => n.Id), StringComparer.Ordinal);
            var members = new List<CatalogNode>();
            var emittedTypes = new HashSet<string>(StringComparer.Ordinal);
            var requiredBy = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var node in catalog.Nodes)
            {
                if (selected.Contains(node.Id) == false || node.Status != WrapStatus.Wrapped) continue;
                if (node.Kind == NodeKind.Enum) emittedTypes.Add(node.Id);
                else if (node.IsType == false && node.Kind != NodeKind.Namespace) members.Add(node);
            }

            foreach (var member in members)
            {
                foreach (var dependency in member.DependencyTypeIds)
                {
                    if (catalog.Contains(dependency) == false) continue;
                    emittedTypes.Add(dependency);
                    Require(requiredBy, dependency, member.Id);
                }
            }

            foreach (var requirer in members.Select(m => m.Id).Concat(emittedTypes.ToList()).OrderBy(id => id, StringComparer.Ordinal))
            {
                for (var container = ParentTypeId(catalog, requirer); container != null; container = ParentTypeId(catalog, container))
                {
                    emittedTypes.Add(container);
                    Require(requiredBy, container, requirer);
                }
            }

            var autoIncluded = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var type in emittedTypes)
            {
                if (selected.Contains(type) == false && requiredBy.TryGetValue(type, out var by)) autoIncluded[type] = by;
            }

            var missing = rules.Included.Concat(rules.Excluded).Where(id => catalog.Contains(id) == false).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var fileCount = emittedTypes.Count(id => catalog.Get(id).IsTopLevelType);
            return new SelectionResult(Sorted(selected), members.Select(m => m.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(), Sorted(emittedTypes), autoIncluded, missing, fileCount);
        }

        private static void Require(Dictionary<string, string> requiredBy, string typeId, string requirerId)
        {
            if (requiredBy.TryGetValue(typeId, out var existing) && string.CompareOrdinal(existing, requirerId) <= 0) return;
            requiredBy[typeId] = requirerId;
        }

        private static string ParentTypeId(Catalog catalog, string id)
        {
            var parent = catalog.Get(catalog.ParentOf(id));
            return parent != null && parent.IsType ? parent.Id : null;
        }

        private static List<string> Sorted(IEnumerable<string> ids) => ids.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }
}
