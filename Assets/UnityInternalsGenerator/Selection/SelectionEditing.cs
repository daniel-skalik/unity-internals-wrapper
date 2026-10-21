namespace DandaUtils.Editor.UnityInternalsGenerator.Selection
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public enum CheckState
    {
        None,
        Partial,
        Whole,
    }

    /// <summary>Pure tri-state editing of <see cref="SelectionRules"/> against a <see cref="Catalog"/>.</summary>
    public static class SelectionEditing
    {
        private const int c_MaxAncestorDepth = 1024;

        /// <summary>None and Partial become selected, Whole becomes unselected.</summary>
        public static SelectionRules Toggle(SelectionRules rules, Catalog catalog, string id) => Set(rules, catalog, id, StateOf(catalog, rules, id) != CheckState.Whole);

        /// <summary>Clears every rule beneath id, then adds a rule on id only when the inherited state differs from the wanted one.</summary>
        public static SelectionRules Set(SelectionRules rules, Catalog catalog, string id, bool selected)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            ThrowIfAll(rules);
            if (catalog.Contains(id) == false) return rules;

            var included = rules.Included.Where(r => IsSameOrDescendant(catalog, r, id) == false).ToList();
            var excluded = rules.Excluded.Where(r => IsSameOrDescendant(catalog, r, id) == false).ToList();

            var remaining = new SelectionRules(included, excluded);
            if (remaining.IsSelected(id, catalog.ParentOf) == selected) return remaining;

            if (selected) included.Add(id);
            else excluded.Add(id);
            return new SelectionRules(included, excluded);
        }

        /// <summary>Removes id from both lists (e.g. to drop a rule whose id is missing from the catalog).</summary>
        public static SelectionRules RemoveRule(SelectionRules rules, string id)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            ThrowIfAll(rules);

            return new SelectionRules(rules.Included.Where(r => string.Equals(r, id, StringComparison.Ordinal) == false), rules.Excluded.Where(r => string.Equals(r, id, StringComparison.Ordinal) == false));
        }

        private static void ThrowIfAll(SelectionRules rules)
        {
            if (ReferenceEquals(rules, SelectionRules.All)) throw new ArgumentException("SelectionRules.All is not editable");
        }

        public static CheckState StateOf(Catalog catalog, SelectionRules rules, string id) => States(catalog, rules).TryGetValue(id ?? "", out var state) ? state : CheckState.None;

        /// <summary>State of every catalog node in a single pass.</summary>
        public static IReadOnlyDictionary<string, CheckState> States(Catalog catalog, SelectionRules rules)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var states = new Dictionary<string, CheckState>(catalog.Nodes.Count, StringComparer.Ordinal);
            foreach (var root in catalog.ChildrenOf(null)) Compute(catalog, rules, root, rules.IsSelected(root.Id, catalog.ParentOf), states);
            return states;
        }

        private static CheckState Compute(Catalog catalog, SelectionRules rules, CatalogNode node, bool selected, Dictionary<string, CheckState> states)
        {
            var children = catalog.ChildrenOf(node.Id);
            if (children.Count == 0)
            {
                var leaf = selected ? CheckState.Whole : CheckState.None;
                states[node.Id] = leaf;
                return leaf;
            }

            var anySelected = selected;
            var allSelected = selected;
            foreach (var child in children)
            {
                var childSelected = rules.RuleAt(child.Id) ?? selected;
                var childState = Compute(catalog, rules, child, childSelected, states);
                if (childState != CheckState.None) anySelected = true;
                if (childState != CheckState.Whole) allSelected = false;
            }

            var state = allSelected ? CheckState.Whole : anySelected ? CheckState.Partial : CheckState.None;
            states[node.Id] = state;
            return state;
        }

        private static bool IsSameOrDescendant(Catalog catalog, string candidate, string ancestorId)
        {
            var current = candidate;
            var guard = 0;
            while (current != null && guard++ < c_MaxAncestorDepth)
            {
                if (string.Equals(current, ancestorId, StringComparison.Ordinal)) return true;
                current = catalog.ParentOf(current);
            }

            return false;
        }
    }
}
