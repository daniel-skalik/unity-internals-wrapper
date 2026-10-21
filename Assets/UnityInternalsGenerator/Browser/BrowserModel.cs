namespace DandaUtils.Editor.UnityInternalsGenerator.Browser
{
    using System;
    using System.Collections.Generic;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;

    public enum ShowFilter
    {
        All,
        Selected,
        Unselected,
    }

    public sealed class BrowserRow
    {
        public BrowserRow(string id, int depth, bool hasChildren, bool canExpand)
        {
            Id = id;
            Depth = depth;
            HasChildren = hasChildren;
            CanExpand = canExpand;
        }

        public string Id { get; }
        public int Depth { get; }

        /// <summary>True when the row has children it can show (whether or not they follow it in the rows).</summary>
        public bool HasChildren { get; }

        /// <summary>True when expanding or collapsing the row changes the rows. False for search-path ancestors, which always show their matching descendants.</summary>
        public bool CanExpand { get; }
    }

    /// <summary>Pure search / filter / expand logic that turns a <see cref="Catalog"/> into flat tree rows.</summary>
    public sealed class BrowserModel
    {
        private readonly Catalog m_Catalog;
        private readonly Dictionary<string, string> m_LowerNames;
        private readonly Dictionary<string, string> m_LowerParentPaths;

        public BrowserModel(Catalog catalog)
        {
            m_Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            m_LowerNames = new Dictionary<string, string>(catalog.Nodes.Count, StringComparer.Ordinal);
            m_LowerParentPaths = new Dictionary<string, string>(catalog.Nodes.Count, StringComparer.Ordinal);
            var containerPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in catalog.Nodes)
            {
                if (node.Kind == NodeKind.Namespace) continue;
                m_LowerNames[node.Id] = (node.Name ?? "").ToLowerInvariant();
                m_LowerParentPaths[node.Id] = LowerPath(catalog.Get(node.ParentId), containerPaths);
            }
        }

        /// <summary>
        /// Rows in tree order. Empty search follows <paramref name="expanded"/>. A search shows matches with all their ancestors;
        /// a matched container that is in <paramref name="expanded"/> also shows all its children (unfiltered by the search,
        /// still by Show / Status), recursively following <paramref name="expanded"/>.
        /// A query containing '.' is split at the last '.': the part after it must be contained in the name, the part before it
        /// must end the parent display path (namespace + declaring types) on a segment boundary. All matching is case-insensitive.
        /// Show and status filters apply to type and member nodes; namespaces are shown iff a descendant passes.
        /// </summary>
        public IReadOnlyList<BrowserRow> Rows(string search, ShowFilter show, WrapStatus? status, ISet<string> expanded, SelectionResult selection, IReadOnlyDictionary<string, CheckState> states)
        {
            var pass = new Pass(this, Query.Parse(search), show, status, expanded, selection, states);
            return pass.Run();
        }

        private string LowerPath(CatalogNode container, Dictionary<string, string> memo)
        {
            if (container == null) return "";
            if (memo.TryGetValue(container.Id, out var cached)) return cached;

            string path;
            if (container.Kind == NodeKind.Namespace) path = (SelectionIds.NamespaceName(container.Id) ?? "").ToLowerInvariant();
            else
            {
                var parentPath = LowerPath(m_Catalog.Get(container.ParentId), memo);
                var name = (container.Name ?? "").ToLowerInvariant();
                path = parentPath.Length == 0 ? name : parentPath + "." + name;
            }

            memo[container.Id] = path;
            return path;
        }

        private sealed class Query
        {
            private Query(string name, string prefix)
            {
                Name = name;
                Prefix = prefix;
                DottedPrefix = prefix == null ? null : "." + prefix;
            }

            /// <summary>Lowercased text the node name must contain (empty matches any name).</summary>
            public string Name { get; }

            /// <summary>Lowercased text the parent display path must end with on a segment boundary; null for undotted queries.</summary>
            public string Prefix { get; }

            public string DottedPrefix { get; }

            public static Query Parse(string search)
            {
                if (string.IsNullOrEmpty(search)) return null;

                var lower = search.ToLowerInvariant();
                var dot = lower.LastIndexOf('.');
                return dot < 0 ? new Query(lower, null) : new Query(lower.Substring(dot + 1), lower.Substring(0, dot));
            }

            public bool Matches(string lowerName, string lowerParentPath)
            {
                if (lowerName.Contains(Name) == false) return false;
                if (Prefix == null || Prefix.Length == 0) return true;
                return string.Equals(lowerParentPath, Prefix, StringComparison.Ordinal) || lowerParentPath.EndsWith(DottedPrefix, StringComparison.Ordinal);
            }
        }

        /// <summary>State of one <see cref="Rows"/> call.</summary>
        private sealed class Pass
        {
            private readonly BrowserModel m_Model;
            private readonly Catalog m_Catalog;
            private readonly Query m_Query;
            private readonly ShowFilter m_Show;
            private readonly WrapStatus? m_Status;
            private readonly ISet<string> m_Expanded;
            private readonly SelectionResult m_Selection;
            private readonly IReadOnlyDictionary<string, CheckState> m_States;
            private readonly bool m_Filtering;
            private readonly Dictionary<string, bool> m_FilterVisible = new Dictionary<string, bool>(StringComparer.Ordinal);
            private readonly HashSet<string> m_Matched = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> m_SearchVisible = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<BrowserRow> m_Rows = new List<BrowserRow>();

            public Pass(BrowserModel model, Query query, ShowFilter show, WrapStatus? status, ISet<string> expanded, SelectionResult selection, IReadOnlyDictionary<string, CheckState> states)
            {
                m_Model = model;
                m_Catalog = model.m_Catalog;
                m_Query = query;
                m_Show = show;
                m_Status = status;
                m_Expanded = expanded;
                m_Selection = selection;
                m_States = states;
                m_Filtering = show != ShowFilter.All || status.HasValue;
            }

            private bool Searching => m_Query != null;

            public IReadOnlyList<BrowserRow> Run()
            {
                var roots = m_Catalog.ChildrenOf(null);
                if (Searching)
                {
                    foreach (var root in roots) MarkSearchVisible(root);
                }

                foreach (var root in roots)
                {
                    if (Searching ? m_SearchVisible.Contains(root.Id) : FilterVisible(root)) Emit(root, 0, false);
                }
                return m_Rows;
            }

            private bool MarkSearchVisible(CatalogNode node)
            {
                var any = false;
                foreach (var child in m_Catalog.ChildrenOf(node.Id)) any |= MarkSearchVisible(child);

                if (node.Kind != NodeKind.Namespace && m_Query.Matches(m_Model.m_LowerNames[node.Id], m_Model.m_LowerParentPaths[node.Id]) && PassesFilters(node))
                {
                    m_Matched.Add(node.Id);
                    any = true;
                }

                if (any) m_SearchVisible.Add(node.Id);
                return any;
            }

            /// <summary>Show / Status visibility ignoring the search: the node passes, or a descendant does. Memoized, computed on demand.</summary>
            private bool FilterVisible(CatalogNode node)
            {
                if (m_Filtering == false) return true;
                if (m_FilterVisible.TryGetValue(node.Id, out var cached)) return cached;

                var visible = node.Kind != NodeKind.Namespace && PassesFilters(node);
                foreach (var child in m_Catalog.ChildrenOf(node.Id)) visible |= FilterVisible(child);

                m_FilterVisible[node.Id] = visible;
                return visible;
            }

            private bool PassesFilters(CatalogNode node)
            {
                if (m_Status.HasValue && node.Status != m_Status.Value) return false;
                if (m_Show == ShowFilter.All) return true;

                var isSelected = IsShownSelected(node.Id);
                return m_Show == ShowFilter.Selected ? isSelected : isSelected == false;
            }

            private bool IsShownSelected(string id)
            {
                if (m_States != null && m_States.TryGetValue(id, out var state) && state != CheckState.None) return true;
                return m_Selection != null && m_Selection.AutoIncludedBy.ContainsKey(id);
            }

            /// <param name="revealed">Shown because an open ancestor shows all its children, not because of the search path.</param>
            private void Emit(CatalogNode node, int depth, bool revealed)
            {
                var children = m_Catalog.ChildrenOf(node.Id);
                var canOpen = Searching == false || revealed || m_Matched.Contains(node.Id);
                var open = canOpen && m_Expanded != null && m_Expanded.Contains(node.Id);

                var hasChildren = false;
                foreach (var child in children)
                {
                    var shown = canOpen ? FilterVisible(child) : m_SearchVisible.Contains(child.Id);
                    if (shown == false) continue;
                    hasChildren = true;
                    break;
                }

                m_Rows.Add(new BrowserRow(node.Id, depth, hasChildren, canOpen && hasChildren));
                if (hasChildren == false) return;

                foreach (var child in children)
                {
                    if (open && FilterVisible(child)) Emit(child, depth + 1, true);
                    else if (Searching && m_SearchVisible.Contains(child.Id)) Emit(child, depth + 1, revealed);
                }
            }
        }
    }
}
